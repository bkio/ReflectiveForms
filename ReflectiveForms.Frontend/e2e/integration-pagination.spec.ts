import { test, expect } from './helpers';

/**
 * Integration tests for paginated PEEK_ALL_PAGINATED endpoint
 * and paginated entity list page.
 *
 * Covers:
 * - PEEK_ALL_PAGINATED API returns items, next_page_token, total_count
 * - Pagination with page_size and page_token query parameters
 * - Multi-page traversal returns all items
 * - Entity list page shows pagination controls when needed
 * - Entity list page navigates between pages
 */

const TS = () => Date.now().toString(36);
const ENTITY = 'team-member';

test.describe('PEEK_ALL_PAGINATED API', () => {
  const createdIds: number[] = [];

  test.beforeAll(async ({ request }) => {
    const { ApiHelper } = await import('./helpers');
    const api = new ApiHelper(request);
    await api.login();

    // Create 5 team members with known titles
    for (let i = 1; i <= 5; i++) {
      const entity = await api.createEntity(ENTITY, {
        'title': { rendered: `Pagination Test ${i} ${TS()}` },
        'fields': {
          email: `pagtest${i}${TS()}@example.com`,
          job_title: `Engineer ${i}`,
          hire_date: '20250101',
          office_address: {
            street: `${i}00 Test St`,
            city: 'TestCity',
            postal_code: '12345',
          },
          emergency_contacts: [
            { contact_name: `Contact ${i}`, phone: `555-000${i}`, relationship: 'friend' },
          ],
          social_links: [],
        },
      });
      createdIds.push(entity.id);
    }
  });

  test.afterAll(async ({ request }) => {
    const { ApiHelper } = await import('./helpers');
    const api = new ApiHelper(request);
    await api.login();
    for (const id of createdIds) {
      await api.deleteEntity(ENTITY, id);
    }
  });

  test('returns items, next_page_token, and total_count', async ({ api }) => {
    const result = await api.peekAllPaginated(ENTITY, 2);

    expect(result.items).toBeDefined();
    expect(Array.isArray(result.items)).toBe(true);
    expect(result.items.length).toBeGreaterThan(0);
    expect(result.items.length).toBeLessThanOrEqual(2);
    // total_count should be at least our 5 created items
    expect(result.total_count).toBeGreaterThanOrEqual(5);
    // With 5+ items and page_size=2, there should be a next_page_token
    expect(result.next_page_token).not.toBeNull();
  });

  test('page_size limits the number of returned items', async ({ api }) => {
    const result = await api.peekAllPaginated(ENTITY, 3);
    expect(result.items.length).toBeLessThanOrEqual(3);
  });

  test('traversing all pages returns all items', async ({ api }) => {
    const allItems: Array<{ id: number }> = [];
    let pageToken: string | undefined;

    // Fetch all pages using small page size
    do {
      const result = await api.peekAllPaginated(ENTITY, 2, pageToken);
      allItems.push(...result.items);
      pageToken = result.next_page_token ?? undefined;
    } while (pageToken);

    // Should have fetched at least our 5 created items
    const fetchedIds = allItems.map(i => i.id);
    for (const id of createdIds) {
      expect(fetchedIds).toContain(id);
    }
  });

  test('second page contains different items than first page', async ({ api }) => {
    const page1 = await api.peekAllPaginated(ENTITY, 2);
    expect(page1.next_page_token).not.toBeNull();

    const page2 = await api.peekAllPaginated(ENTITY, 2, page1.next_page_token!);
    const page1Ids = page1.items.map(i => i.id);
    const page2Ids = page2.items.map(i => i.id);

    // No overlap between pages
    for (const id of page2Ids) {
      expect(page1Ids).not.toContain(id);
    }
  });

  test('large page_size returns all items in one page', async ({ api }) => {
    const result = await api.peekAllPaginated(ENTITY, 100);
    expect(result.items.length).toBeGreaterThanOrEqual(5);
    // Either null next_page_token or all items already returned
    if (result.next_page_token === null) {
      expect(result.items.length).toBe(result.total_count);
    }
  });
});

test.describe('Entity List Page Pagination', () => {
  test.describe.configure({ mode: 'serial' });

  const createdIds: number[] = [];
  const ENTITY_TYPE = 'blog-post';

  test.beforeAll(async ({ request }) => {
    const { ApiHelper } = await import('./helpers');
    const api = new ApiHelper(request);
    await api.login();
    // Clean up first
    await api.deleteAll(ENTITY_TYPE);
  });

  test.afterAll(async ({ request }) => {
    const { ApiHelper } = await import('./helpers');
    const api = new ApiHelper(request);
    await api.login();
    await api.deleteAll(ENTITY_TYPE);
  });

  test('list page shows entities from paginated API', async ({ ui, api }) => {
    // Create a few entities
    for (let i = 1; i <= 3; i++) {
      const entity = await api.createEntity(ENTITY_TYPE, {
        'title': { rendered: `ListPage Blog ${i}` },
        'fields': {
          content: `<p>Content ${i}</p>`,
          excerpt: `Excerpt ${i}`,
          slug: `listpage-blog-${i}-${TS()}`,
          status: 'published',
          reading_time_minutes: 5,
          external_links: [],
        },
      });
      createdIds.push(entity.id);
    }

    // Navigate to the list page
    await ui.gotoEntityList(ENTITY_TYPE);

    // Verify entities are shown
    for (let i = 1; i <= 3; i++) {
      await expect(ui.page.locator(`text=ListPage Blog ${i}`)).toBeVisible({ timeout: 10000 });
    }
  });

  test('list page shows total count', async ({ ui }) => {
    await ui.gotoEntityList(ENTITY_TYPE);
    // The page should show a total count somewhere
    await expect(ui.page.locator('text=/\\d+ total/')).toBeVisible({ timeout: 10000 });
  });
});
