import { test, expect, ApiHelper } from './helpers';

const API_BASE = 'http://localhost:9000/rf/api';
const APP_PREFIX = '/rf/app';

/**
 * Authorization E2E Tests
 *
 * Creates a limited IAM role and a test user, then verifies the UI
 * enforces capabilities: sidebar visibility, button visibility,
 * edit page redirect, and view page edit button.
 */
test.describe('Authorization & Capabilities', () => {
  test.describe.configure({ mode: 'serial' });

  const TEST_USER_EMAIL = `e2e-authz-${Date.now()}@test.com`;
  const TEST_USER_PASSWORD = 'testpass123';
  let roleId: number;
  let userId: number;

  // Keep track of entities created during setup
  let blogPostId: number;
  let teamMemberId: number;
  let eventId: number;

  test.beforeAll(async ({ request }, testInfo) => {
    testInfo.setTimeout(300000); // 5min — user creation hook is slow
    const adminApi = new ApiHelper(request);
    await adminApi.login();

    // 1. Create a limited IAM role
    const roleRes = await request.post(
      `${API_BASE}/crud?operation=CREATE&type=iam-role`,
      {
        data: {
          title: { rendered: `E2E Limited Role ${Date.now()}` },
          tags: [], categories: [], author: 2, parent: -1,
          fields: {
            capabilities: [
              {
                entity_type: 'blog-post',
                allow_peek_all: true,
                allow_read: false,
                allow_create: false,
                allow_update: false,
                allow_delete: false,
              },
              {
                entity_type: 'team-member',
                allow_peek_all: true,
                allow_read: true,
                allow_create: false,
                allow_update: false,
                allow_delete: false,
              },
              {
                entity_type: 'event',
                allow_peek_all: true,
                allow_read: true,
                allow_create: false,
                allow_update: true,
                allow_delete: false,
              },
            ],
          },
        },
        timeout: 60000,
      },
    );
    expect(roleRes.ok(), `Role creation failed: ${roleRes.status()}`).toBeTruthy();
    const roleData = await roleRes.json();
    roleId = roleData.id;

    // 2. Create test entities so list pages have data (sequential to avoid conflicts)
    const ts = Date.now();
    const blogResult = await adminApi.createEntity('blog-post', {
      title: { rendered: `Auth Test Blog Post ${ts}` },
      fields: {
        content: '<p>Test content</p>',
        excerpt: '',
        featured_image: '',
        status: 'draft',
        scheduled_date: '',
        is_featured: false,
        allow_comments: true,
        reading_time_minutes: 5,
        seo_metadata: { meta_title: '', meta_description: '', meta_keywords: '', canonical_url: '' },
        external_links: [],
        slug: `auth-test-${ts}`,
        publication_year: '',
      },
    });
    blogPostId = blogResult.id;

    const teamResult = await adminApi.createEntity('team-member', {
      title: { rendered: `Auth Test Team Member ${ts}` },
      fields: {
        email: `e2e-tm-${ts}@test.com`,
        job_title: 'Tester',
        department: 'engineering',
        years_of_experience: 1,
        performance_score: 5,
        is_remote: false,
        hire_date: '20240101',
        salary: 50000,
        emergency_contacts: [{ contact_name: 'EC', relationship: 'friend', phone: '+1 555-0000', email: 'ec@test.com' }],
        social_links: [],
        avatar: '',
        bio: '',
        office_address: { street: '1 Test St', city: 'Test', state: 'TS', postal_code: '00000', country: 'US' },
        favorite_blog_post: -1,
      },
    });
    teamMemberId = teamResult.id;

    const eventResult = await adminApi.createEntity('event', {
      title: { rendered: `Auth Test Event ${ts}` },
      fields: {
        description: '<p>Test event</p>',
        event_type: 'meetup',
        start_date: '20250101',
        end_date: '20250102',
        is_online: true,
        meeting_url: 'https://example.com/meet',
        venue: { venue_name: '', venue_address: { street: '', city: '', state: '', postal_code: '', country: 'US' }, capacity: 0, venue_url: '' },
        max_attendees: 10,
        ticket_price: 0,
        registration_email: `e2e-ev-${ts}@test.com`,
        banner_image: '',
        sessions: [],
        sponsors: [],
        event_coordinator: -1,
        registration_url: '',
      },
    });
    eventId = eventResult.id;

    // 3. Create a test user with this limited role
    //    Pre-computed SHA256 hash to minimize post-create hook work
    const userRes = await request.post(
      `${API_BASE}/crud?operation=CREATE&type=users`,
      {
        data: {
          title: { rendered: `E2E Auth Test User ${Date.now()}` },
          tags: [], categories: [], author: 2, parent: -1,
          fields: {
            email_address: TEST_USER_EMAIL,
            optional_custom_password: TEST_USER_PASSWORD,
            generate_password: false,
            password_sha256: '',
            roles: [{ role: roleId }],
          },
        },
        timeout: 180000,
      },
    );
    const userBody = await userRes.text();
    expect(userRes.ok(), `User creation failed (${userRes.status()}): ${userBody}`).toBeTruthy();
    const userData = JSON.parse(userBody);
    userId = userData.id;

    // 4. Poll until login succeeds — the user creation hook hashes the
    //    password synchronously now, but keep a retry loop as a safety net
    const maxWaitMs = 30_000;
    const pollIntervalMs = 2_000;
    const start = Date.now();
    let loginOk = false;
    while (Date.now() - start < maxWaitMs) {
      const loginRes = await request.post(`${API_BASE}/login`, {
        data: { email: TEST_USER_EMAIL, password: TEST_USER_PASSWORD },
      });
      if (loginRes.ok()) {
        loginOk = true;
        break;
      }
      await new Promise(r => setTimeout(r, pollIntervalMs));
    }
    expect(loginOk, `Test user login never succeeded after ${maxWaitMs / 1000}s`).toBeTruthy();
  });

  test.afterAll(async ({ request }) => {
    const adminApi = new ApiHelper(request);
    await adminApi.login();

    // Cleanup: delete test entities
    if (blogPostId) await adminApi.deleteEntity('blog-post', blogPostId).catch(() => {});
    if (teamMemberId) await adminApi.deleteEntity('team-member', teamMemberId).catch(() => {});
    if (eventId) await adminApi.deleteEntity('event', eventId).catch(() => {});
    if (userId) await adminApi.deleteEntity('users', userId).catch(() => {});
    if (roleId) await adminApi.deleteEntity('iam-role', roleId).catch(() => {});
  });

  // Helper: login as the limited test user and inject auth cookies
  async function loginAsTestUser(page: import('@playwright/test').Page, request: import('@playwright/test').APIRequestContext) {
    const loginRes = await request.post(`${API_BASE}/login`, {
      data: { email: TEST_USER_EMAIL, password: TEST_USER_PASSWORD },
    });
    expect(loginRes.ok(), `Test user login failed: ${loginRes.status()}`).toBeTruthy();

    const setCookieHeaders = loginRes.headersArray().filter(h => h.name.toLowerCase() === 'set-cookie');
    for (const header of setCookieHeaders) {
      const parts = header.value.split(';').map(p => p.trim());
      const [nameValue] = parts;
      const [name, ...rest] = nameValue.split('=');
      const value = rest.join('=');
      await page.context().addCookies([{
        name,
        value,
        domain: 'localhost',
        path: '/',
        httpOnly: true,
        sameSite: 'Strict',
      }]);
    }
  }

  test('sidebar should only show entity types the user can list', async ({ page, request }) => {
    await loginAsTestUser(page, request);
    await page.goto(`${APP_PREFIX}/`);
    await page.waitForSelector('h1');

    // Wait for capabilities to load and sidebar to settle
    await page.waitForTimeout(2000);

    const sidebar = page.locator('nav');

    // User has peek_all for: blog-post, team-member, event
    // Should see these in sidebar
    await expect(sidebar.locator('a', { hasText: 'Blog Posts' })).toBeVisible();
    await expect(sidebar.locator('a', { hasText: 'Team Members' })).toBeVisible();
    await expect(sidebar.locator('a', { hasText: 'Events' })).toBeVisible();

    // User does NOT have peek_all for: objective
    await expect(sidebar.locator('a', { hasText: 'Objectives' })).not.toBeVisible();

    // Reserved entity types the user has no access to
    await expect(sidebar.locator('a', { hasText: /^Users$/i })).not.toBeVisible();
  });

  test('dashboard should only show entity types the user can list', async ({ page, request }) => {
    await loginAsTestUser(page, request);
    await page.goto(`${APP_PREFIX}/`);
    await page.waitForSelector('h1');
    await page.waitForTimeout(2000);

    // Entity cards should exist for accessible types
    await expect(page.locator('h3', { hasText: 'Blog Posts' })).toBeVisible();
    await expect(page.locator('h3', { hasText: 'Team Members' })).toBeVisible();
    await expect(page.locator('h3', { hasText: 'Events' })).toBeVisible();

    // No card for objectives
    await expect(page.locator('h3', { hasText: 'Objectives' })).not.toBeVisible();
  });

  test('dashboard should hide Create New button when user lacks create capability', async ({ page, request }) => {
    await loginAsTestUser(page, request);
    await page.goto(`${APP_PREFIX}/`);
    await page.waitForSelector('h1');
    await page.waitForTimeout(2000);

    // blog-post card — no create capability → no "Create New" button
    const blogCard = page.locator('.bg-white.rounded-lg').filter({ hasText: 'Blog Posts' });
    await expect(blogCard.locator('a', { hasText: 'Create New' })).not.toBeVisible();

    // event card — has update but no create → no "Create New" button
    const eventCard = page.locator('.bg-white.rounded-lg').filter({ hasText: 'Events' });
    await expect(eventCard.locator('a', { hasText: 'Create New' })).not.toBeVisible();
  });

  test('list page for peek-only entity hides all action buttons except title', async ({ page, request }) => {
    await loginAsTestUser(page, request);
    await page.goto(`${APP_PREFIX}/entities/blog-post`);
    await page.waitForSelector('table', { timeout: 15000 });
    await page.waitForTimeout(2000);

    // "Add New" button should NOT be visible (no create)
    await expect(page.locator('a', { hasText: 'Add New' })).not.toBeVisible();

    // Action buttons in table rows
    const firstRow = page.locator('tbody tr').first();
    // View button should NOT be visible (no read)
    await expect(firstRow.locator('a[title="View"]')).not.toBeVisible();
    // Edit button should NOT be visible (no update)
    await expect(firstRow.locator('a[title="Edit"]')).not.toBeVisible();
    // Clone button should NOT be visible (no create)
    await expect(firstRow.locator('a[title="Clone"]')).not.toBeVisible();
    // Delete button should NOT be visible (no delete)
    await expect(firstRow.locator('button[title="Delete"]')).not.toBeVisible();
  });

  test('list page for read-only entity shows View but no Edit/Create/Delete', async ({ page, request }) => {
    await loginAsTestUser(page, request);
    await page.goto(`${APP_PREFIX}/entities/team-member`);
    await page.waitForSelector('table', { timeout: 15000 });
    await page.waitForTimeout(2000);

    // "Add New" button should NOT be visible
    await expect(page.locator('a', { hasText: 'Add New' })).not.toBeVisible();

    // If there are rows, check action buttons
    const rows = page.locator('tbody tr').filter({ hasNot: page.locator('td[colspan]') });
    const count = await rows.count();
    if (count > 0) {
      const firstRow = rows.first();
      // View should be visible (can_read = true)
      await expect(firstRow.locator('a[title="View"]')).toBeVisible();
      // Edit, Clone, Delete should NOT be visible
      await expect(firstRow.locator('a[title="Edit"]')).not.toBeVisible();
      await expect(firstRow.locator('a[title="Clone"]')).not.toBeVisible();
      await expect(firstRow.locator('button[title="Delete"]')).not.toBeVisible();
    }
  });

  test('list page for update-allowed entity shows View and Edit but no Create/Delete', async ({ page, request }) => {
    await loginAsTestUser(page, request);
    await page.goto(`${APP_PREFIX}/entities/event`);
    await page.waitForSelector('table', { timeout: 15000 });
    await page.waitForTimeout(2000);

    // "Add New" button should NOT be visible (no create)
    await expect(page.locator('a', { hasText: 'Add New' })).not.toBeVisible();

    // If there are rows, check action buttons
    const rows = page.locator('tbody tr').filter({ hasNot: page.locator('td[colspan]') });
    const count = await rows.count();
    if (count > 0) {
      const firstRow = rows.first();
      // View and Edit should be visible
      await expect(firstRow.locator('a[title="View"]')).toBeVisible();
      await expect(firstRow.locator('a[title="Edit"]')).toBeVisible();
      // Clone should NOT be visible (no create)
      await expect(firstRow.locator('a[title="Clone"]')).not.toBeVisible();
      // Delete should NOT be visible (no delete)
      await expect(firstRow.locator('button[title="Delete"]')).not.toBeVisible();
    }
  });

  test('edit page redirects to list when user lacks create capability', async ({ page, request }) => {
    await loginAsTestUser(page, request);

    // Try to navigate to "new blog-post" — user has no create for blog-post
    await page.goto(`${APP_PREFIX}/entities-admin/blog-post?id=new`);

    // Should redirect to list page
    await page.waitForURL('**/entities/blog-post', { timeout: 15000 });
    await expect(page.locator('h1')).toContainText('Blog Posts');
  });

  test('edit page redirects to view when user lacks update capability', async ({ page, request }) => {
    await loginAsTestUser(page, request);

    // Try to edit blog-post (user has peek_all only, not update)
    // We need an existing blog-post ID
    if (!blogPostId) return;

    await page.goto(`${APP_PREFIX}/entities-admin/blog-post?id=${blogPostId}`);

    // Should redirect to view page
    await page.waitForURL(`**/entities-view/blog-post?id=${blogPostId}`, { timeout: 15000 });
  });

  test('view page hides Edit button when user lacks update capability', async ({ page, request }) => {
    await loginAsTestUser(page, request);

    // View the team-member entity we created (user has peek + read, but NOT update)
    await page.goto(`${APP_PREFIX}/entities-view/team-member?id=${teamMemberId}`);
    await page.waitForSelector('h1', { timeout: 15000 });
    await page.waitForTimeout(1000);

    // Edit button should NOT be visible
    await expect(page.locator('a', { hasText: 'Edit' })).not.toBeVisible();
  });

  test('backend enforces permissions — CREATE returns 403 when user lacks capability', async ({ request }) => {
    // Login as the limited user
    const limitedLogin = await request.post(`${API_BASE}/login`, {
      data: { email: TEST_USER_EMAIL, password: TEST_USER_PASSWORD },
    });
    expect(limitedLogin.ok()).toBeTruthy();

    // Try to create a blog-post (user has no create for blog-post)
    const res = await request.post(
      `${API_BASE}/crud?operation=CREATE&type=blog-post`,
      {
        data: {
          title: { rendered: 'Should Fail' },
          fields: {},
          tags: [],
          categories: [],
          author: 2,
          parent: -1,
        },
      },
    );
    expect(res.status()).toBe(403);
  });

  test('backend enforces permissions — PEEK_ALL returns 403 for unauthorized entity type', async ({ request }) => {
    // Login as the limited user
    await request.post(`${API_BASE}/login`, {
      data: { email: TEST_USER_EMAIL, password: TEST_USER_PASSWORD },
    });

    // Try to peek objectives (user has no access to objectives at all)
    const res = await request.post(
      `${API_BASE}/crud?operation=PEEK_ALL&type=objective`,
      { data: {} },
    );
    expect(res.status()).toBe(403);
  });

  test('capabilities endpoint returns correct permissions for limited user', async ({ request }) => {
    // Login as the limited user
    await request.post(`${API_BASE}/login`, {
      data: { email: TEST_USER_EMAIL, password: TEST_USER_PASSWORD },
    });

    const res = await request.post(`${API_BASE}/capabilities`);
    expect(res.ok()).toBeTruthy();
    const caps = await res.json() as Record<string, Record<string, boolean>>;

    // blog-post: peek only
    expect(caps['blog-post'].can_peek_all).toBe(true);
    expect(caps['blog-post'].can_read).toBe(false);
    expect(caps['blog-post'].can_create).toBe(false);
    expect(caps['blog-post'].can_update).toBe(false);
    expect(caps['blog-post'].can_delete).toBe(false);

    // team-member: peek + read
    expect(caps['team-member'].can_peek_all).toBe(true);
    expect(caps['team-member'].can_read).toBe(true);
    expect(caps['team-member'].can_create).toBe(false);
    expect(caps['team-member'].can_update).toBe(false);
    expect(caps['team-member'].can_delete).toBe(false);

    // event: peek + read + update
    expect(caps['event'].can_peek_all).toBe(true);
    expect(caps['event'].can_read).toBe(true);
    expect(caps['event'].can_create).toBe(false);
    expect(caps['event'].can_update).toBe(true);
    expect(caps['event'].can_delete).toBe(false);

    // objective: no access
    expect(caps['objective'].can_peek_all).toBe(false);
    expect(caps['objective'].can_read).toBe(false);
    expect(caps['objective'].can_create).toBe(false);
    expect(caps['objective'].can_update).toBe(false);
    expect(caps['objective'].can_delete).toBe(false);
  });
});
