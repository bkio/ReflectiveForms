import { test, expect } from './helpers';

/**
 * Full Navigation Flow Integration Tests
 *
 * End-to-end navigation journeys through the application:
 * - Dashboard → Entity List → Create → Save → List → Edit → Update → Delete → Verify
 * - Back navigation
 * - Clone flow from list page
 * - Cross-entity navigation (jumping between entity types)
 * - Empty state handling
 * - "View All" and "Create New" links on dashboard
 * - URL parameter handling (id=new, id=N, id=clone_from_N)
 */

const TS = () => Date.now().toString(36);

test.describe('Navigation: Complete lifecycle flow', () => {
  test.describe.configure({ mode: 'serial' });

  let entityId: number;

  test.afterAll(async ({ request }) => {
    const { ApiHelper } = await import('./helpers');
    const api = new ApiHelper(request);
    await api.login();
    await api.deleteAll('blog-post');
  });

  test('start on dashboard, navigate to blog posts list', async ({ page, ui }) => {
    await ui.gotoDashboard();

    // Click "View All" on Blog Posts card
    const blogCard = page.locator('.bg-white.rounded-lg').filter({
      has: page.locator('h3', { hasText: 'Blog Posts' }),
    });
    await blogCard.locator('a', { hasText: 'View All' }).click();

    await page.waitForURL(/\/entities\/blog-post/);
    await expect(page.locator('h1')).toContainText('Blog Posts');
  });

  test('empty state shows "Create one?" link', async ({ page, ui, api }) => {
    await api.deleteAll('blog-post');
    await ui.gotoEntityList('blog-post');

    // Should see empty state message
    await expect(page.locator('td', { hasText: /no.*found/i })).toBeVisible();
    await expect(page.locator('a', { hasText: /create one/i })).toBeVisible();
  });

  test('navigate to create form via "Add New" button', async ({ page, ui }) => {
    await ui.gotoEntityList('blog-post');

    await page.locator('a', { hasText: 'Add New' }).click();
    await page.waitForURL(/entities-admin\/blog-post\?id=new/);

    await expect(page.locator('h1')).toContainText('New Blog Post');
    await expect(page.locator('form')).toBeVisible();
  });

  test('create entity and navigate back to list', async ({ page, ui, api }) => {
    await ui.gotoNewEntity('blog-post');

    const title = `Nav Test Blog ${TS()}`;
    await ui.fillTitle(title);
    await ui.fillWysiwyg('Post Content', '<p>Nav test content</p>');
    await ui.fillTextField('URL Slug', `nav-${TS()}`);
    await ui.fillTextArea('Excerpt', 'Navigation test excerpt');

    await ui.clickSaveNow();
    await ui.waitForSave();

    // Get the created ID
    const entities = await api.peekAll('blog-post');
    const created = entities.find(e => (e.title ?? e.name ?? '').includes('Nav Test Blog'));
    expect(created).toBeDefined();
    entityId = created!.id;

    // Navigate to list
    await ui.gotoEntityList('blog-post');

    // Entity should appear in the list
    await expect(page.locator('a', { hasText: /Nav Test Blog/ })).toBeVisible();
    expect(await ui.entityRowCount()).toBeGreaterThanOrEqual(1);
  });

  test('click entity title in list to open edit form', async ({ page, ui }) => {
    await ui.gotoEntityList('blog-post');

    // Click on the entity title link
    await page.locator('a', { hasText: /Nav Test Blog/ }).click();
    await page.waitForURL(/entities-admin\/blog-post\?id=\d+/);

    await expect(page.locator('h1')).toContainText('Edit Blog Post');
    const title = await ui.getTitle();
    expect(title).toContain('Nav Test Blog');
  });

  test('edit and save, then verify in list', async ({ page, ui, api }) => {
    await ui.gotoEditEntity('blog-post', entityId);

    await ui.fillTitle('Nav Test Blog UPDATED');
    await ui.clickSaveNow();
    await ui.waitForSave();

    // Go back to list
    await ui.gotoEntityList('blog-post');

    // Updated title should appear
    await expect(page.locator('a', { hasText: /Nav Test Blog UPDATED/ })).toBeVisible();

    // Verify via API
    const entity = await api.readEntity('blog-post', entityId);
    expect(entity.title.rendered).toBe('Nav Test Blog UPDATED');
  });

  test('clone from list page via clone icon', async ({ page, ui, api }) => {
    await ui.gotoEntityList('blog-post');

    // Click clone icon on the first row
    const firstRow = ui.entityRows().first();
    await firstRow.locator('a[title="Clone"]').click();

    await page.waitForURL(/entities-admin\/blog-post\?id=clone_from_/);
    await expect(page.locator('h1')).toContainText('Clone Blog Post');

    // Title should be pre-filled
    const title = await ui.getTitle();
    expect(title).toContain('Nav Test Blog');

    // Modify title
    await ui.fillTitle(`Nav Cloned ${TS()}`);
    await ui.fillTextField('URL Slug', `nav-clone-${TS()}`);
    await ui.clickSaveNow();
    await ui.waitForSave();

    // Verify both entities exist
    const entities = await api.peekAll('blog-post');
    expect(entities.length).toBe(2);

    // Cleanup the clone
    const clone = entities.find(e => (e.title ?? e.name ?? '').includes('Nav Cloned'));
    if (clone) await api.deleteEntity('blog-post', clone.id);
  });

  test('delete from list page, row disappears', async ({ page, ui, api }) => {
    await ui.gotoEntityList('blog-post');

    const countBefore = await ui.entityRowCount();
    expect(countBefore).toBeGreaterThanOrEqual(1);

    page.on('dialog', d => d.accept());
    await ui.clickDeleteOnRow(0);

    await page.waitForTimeout(2000);

    const entities = await api.peekAll('blog-post');
    expect(entities.length).toBe(countBefore - 1);
  });
});

test.describe('Navigation: Cross-entity jumping', () => {
  test('can navigate between different entity types rapidly', async ({ page, ui }) => {
    // Dashboard
    await ui.gotoDashboard();
    await expect(page.locator('h1', { hasText: 'Dashboard' })).toBeVisible();

    // Blog posts list
    await ui.gotoEntityList('blog-post');
    await expect(page.locator('h1')).toContainText('Blog Posts');

    // Team members list
    await ui.gotoEntityList('team-member');
    await expect(page.locator('h1')).toContainText('Team Members');

    // Products list
    await ui.gotoEntityList('product');
    await expect(page.locator('h1')).toContainText('Products');

    // Events list
    await ui.gotoEntityList('event');
    await expect(page.locator('h1')).toContainText('Events');

    // Objectives list
    await ui.gotoEntityList('objective');
    await expect(page.locator('h1')).toContainText('Objectives');

    // Back to dashboard
    await ui.gotoDashboard();
    await expect(page.locator('h1', { hasText: 'Dashboard' })).toBeVisible();
  });

  test('can open new-entity forms for all types', async ({ page, ui }) => {
    for (const entity of ['blog-post', 'team-member', 'product', 'event', 'objective']) {
      await ui.gotoNewEntity(entity);
      await expect(page.locator('h1')).toContainText('New');
      await expect(page.locator('form')).toBeVisible();
    }
  });
});

test.describe('Navigation: URL parameter handling', () => {
  test.describe.configure({ mode: 'serial' });
  let blogId: number;

  test.afterAll(async ({ request }) => {
    const { ApiHelper } = await import('./helpers');
    const api = new ApiHelper(request);
    await api.login();
    await api.deleteAll('blog-post');
  });

  test('setup: create a blog post', async ({ api }) => {
    await api.deleteAll('blog-post');
    const result = await api.createEntity('blog-post', {
      title: { rendered: `URL Param Test ${TS()}` },
      fields: {
        content: '<p>test</p>', excerpt: 'URL test', status: 'draft',
        is_featured: false, allow_comments: true, reading_time_minutes: 1,
        slug: `url-param-${TS()}`, featured_image: '',
        seo_metadata: { meta_title: '', meta_description: '', meta_keywords: '', canonical_url: '' },
        external_links: [], publication_year: '', scheduled_date: '',
      },
    });
    blogId = result.id;
  });

  test('?id=new shows create form', async ({ page }) => {
    await page.goto(`/entities-admin/blog-post?id=new`);
    await page.waitForSelector('form', { timeout: 15000 });
    await expect(page.locator('h1')).toContainText('New');
  });

  test('?id=<number> shows edit form', async ({ page, ui }) => {
    await page.goto(`/entities-admin/blog-post?id=${blogId}`);
    await page.waitForSelector('form', { timeout: 15000 });
    await expect(page.locator('h1')).toContainText('Edit');
    await expect(page.locator('p', { hasText: `Editing ID: ${blogId}` })).toBeVisible();
  });

  test('?id=clone_from_<number> shows clone form', async ({ page, ui }) => {
    await page.goto(`/entities-admin/blog-post?id=clone_from_${blogId}`);
    await page.waitForSelector('form', { timeout: 15000 });
    await expect(page.locator('h1')).toContainText('Clone');
  });

  test('navigating to invalid entity ID shows error', async ({ page, ui }) => {
    await page.goto(`/entities-admin/blog-post?id=999999`);
    // Should show an error state or empty form
    await page.waitForTimeout(5000);

    // Either error message or empty content
    const hasError = await page.locator('.text-red-600, .bg-red-50').isVisible().catch(() => false);
    const hasForm = await page.locator('form').isVisible().catch(() => false);

    // At least one of these should be true — the app shouldn't crash
    expect(hasError || hasForm).toBe(true);
  });
});

test.describe('Navigation: Dashboard quick links', () => {
  test('quick links lead to create forms', async ({ page, ui }) => {
    await ui.gotoDashboard();

    // Find quick links section
    const quickLinks = page.locator('a', { hasText: /New .+/ });
    const count = await quickLinks.count();
    expect(count).toBeGreaterThan(0);

    // Click the first quick link
    const firstLink = quickLinks.first();
    const linkText = await firstLink.textContent();
    await firstLink.click();

    await page.waitForURL(/entities-admin\/.*\?id=new/);
    await expect(page.locator('form')).toBeVisible();
  });
});
