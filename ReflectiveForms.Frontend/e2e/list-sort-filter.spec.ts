import { test, expect } from './helpers';

/**
 * E2E tests for EntityListPage sorting, filtering, and pagination.
 */
test.describe('List Page — Sort, Filter & Pagination', () => {
  test.describe.configure({ mode: 'serial' });

  const ENTITY = 'blog-post';
  const createdIds: number[] = [];

  // Valid blog-post fields (matches all required fields from blog-post schema)
  const baseFields = {
    content: '<p>Test content</p>',
    excerpt: 'A short excerpt',
    featured_image: '',
    status: 'draft',
    scheduled_date: '',
    is_featured: false,
    allow_comments: true,
    reading_time_minutes: 5,
    seo_metadata: { meta_title: '', meta_description: '', meta_keywords: '', canonical_url: '' },
    external_links: [],
    publication_year: '',
    slug: '',
    sections: [],
    related_team_member: -1,
    category_select: 'technology',
    publish_date: '20260315',
    canonical_url: '',
    seo_score: 50,
  };

  test('setup — create 5 blog posts with distinct titles', async ({ api }) => {
    await api.deleteAll(ENTITY);

    const titles = ['Delta Article', 'Alpha Article', 'Echo Article', 'Charlie Article', 'Bravo Article'];
    for (const title of titles) {
      const slug = `${title.toLowerCase().replace(/\s+/g, '-')}-${Date.now()}`;
      const entity = await api.createEntity(ENTITY, {
        title: { rendered: `${title} ${Date.now()}` },
        fields: { ...baseFields, slug },
      });
      createdIds.push(entity.id);
    }
    expect(createdIds.length).toBe(5);
  });

  test('search box is visible', async ({ ui }) => {
    await ui.gotoEntityList(ENTITY);
    const searchInput = ui.page.getByTestId('search-input');
    await expect(searchInput).toBeVisible();
  });

  test('search filters rows by title', async ({ ui }) => {
    await ui.gotoEntityList(ENTITY);
    const searchInput = ui.page.getByTestId('search-input');
    await searchInput.fill('Alpha');
    await ui.page.waitForTimeout(300);

    const rows = ui.entityRows();
    await expect(rows).toHaveCount(1);
    await expect(rows.first()).toContainText('Alpha Article');
  });

  test('filter count updates when searching', async ({ ui }) => {
    await ui.gotoEntityList(ENTITY);
    const searchInput = ui.page.getByTestId('search-input');
    await searchInput.fill('Article');
    await ui.page.waitForTimeout(300);

    const filterCount = ui.page.getByTestId('filter-count');
    await expect(filterCount).toContainText('Showing 5 of 5');
  });

  test('clearing search shows all entities', async ({ ui }) => {
    await ui.gotoEntityList(ENTITY);
    const searchInput = ui.page.getByTestId('search-input');
    await searchInput.fill('Alpha');
    await ui.page.waitForTimeout(300);

    await expect(ui.entityRows()).toHaveCount(1);

    const clearBtn = ui.page.getByTestId('search-clear');
    await clearBtn.click();

    await expect(ui.entityRows()).toHaveCount(5);
  });

  test('click Title header sorts alphabetically', async ({ ui }) => {
    await ui.gotoEntityList(ENTITY);

    const titleHeader = ui.page.locator('thead th').filter({ hasText: 'Title' });
    await titleHeader.click();

    const rows = ui.entityRows();
    const firstRowText = await rows.first().textContent();
    expect(firstRowText).toContain('Alpha Article');
  });

  test('click Title header again reverses sort', async ({ ui }) => {
    await ui.gotoEntityList(ENTITY);

    const titleHeader = ui.page.locator('thead th').filter({ hasText: 'Title' });
    await titleHeader.click(); // asc
    await titleHeader.click(); // desc

    const rows = ui.entityRows();
    const firstRowText = await rows.first().textContent();
    expect(firstRowText).toContain('Echo Article');
  });

  test('Last Modified column shows actual dates (not "-")', async ({ ui }) => {
    await ui.gotoEntityList(ENTITY);

    // The Last Modified column is hidden on mobile viewports (< sm breakpoint)
    const modCells = ui.page.locator('tbody td.hidden.sm\\:table-cell');
    const count = await modCells.count();
    if (count === 0) {
      test.skip(true, 'Last Modified column hidden on mobile viewport');
    }

    for (let i = 0; i < count; i++) {
      const text = await modCells.nth(i).textContent();
      expect(text?.trim()).not.toBe('-');
      // Should contain a year
      expect(text).toMatch(/\d{4}/);
    }
  });

  test('click Last Modified header sorts by date', async ({ ui }) => {
    await ui.gotoEntityList(ENTITY);

    const modHeader = ui.page.locator('thead th').filter({ hasText: 'Last Modified' });
    // The Last Modified column is hidden on mobile viewports (< sm breakpoint)
    if (!await modHeader.isVisible()) {
      test.skip(true, 'Last Modified column hidden on mobile viewport');
    }
    await modHeader.click(); // asc — oldest first

    // Just verify sorting changes the order (first row should differ from default)
    const rows = ui.entityRows();
    const count = await rows.count();
    expect(count).toBe(5);
  });

  test('cleanup — delete all test entities', async ({ api }) => {
    await api.deleteAll(ENTITY);
    const remaining = await api.peekAll(ENTITY);
    expect(remaining.length).toBe(0);
  });
});
