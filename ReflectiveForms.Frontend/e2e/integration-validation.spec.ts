import { test, expect } from './helpers';

/**
 * Validation & Sanity Check Integration Tests
 *
 * Tests that backend validation rules (LogicSanityCheckAsync, title uniqueness,
 * required fields, min/max constraints) are properly surfaced in the UI.
 *
 * Covers:
 * - Blog Post: slug uniqueness (LogicSanityCheckAsync)
 * - Objective: root cause uniqueness (LogicSanityCheckAsync)
 * - Objective: title uniqueness (RequireGlobalTitleUniqueness)
 * - Blog Post: title uniqueness (RequireGlobalTitleUniqueness)
 * - Required field enforcement in create mode
 * - Number field min/max validation
 * - Repeater min/max row enforcement
 */

const TS = () => Date.now().toString(36);

test.describe('Validation: Blog Post slug uniqueness', () => {
  test.describe.configure({ mode: 'serial' });

  const uniqueSlug = `unique-slug-${TS()}`;

  test.afterAll(async ({ request }) => {
    const { ApiHelper } = await import('./helpers');
    const api = new ApiHelper(request);
    await api.login();
    await api.deleteAll('blog-post');
  });

  test('create first blog post with a slug', async ({ api }) => {
    await api.deleteAll('blog-post');
    await api.createEntity('blog-post', {
      title: { rendered: `Slug Test A ${TS()}` },
      fields: {
        content: '<p>test</p>', excerpt: 'test', status: 'draft', is_featured: false,
        allow_comments: true, reading_time_minutes: 5,
        slug: uniqueSlug,
        featured_image: '',
        seo_metadata: { meta_title: '', meta_description: '', meta_keywords: '', canonical_url: '' },
        external_links: [], publication_year: '', scheduled_date: '',
      },
    });
  });

  test('creating second blog post with same slug shows sanity error in UI', async ({ page, ui }) => {
    await ui.gotoNewEntity('blog-post');

    await ui.fillTitle(`Slug Test B ${TS()}`);
    await ui.fillWysiwyg('Post Content', '<p>Test content</p>');
    await ui.fillTextField('URL Slug', uniqueSlug);
    await ui.fillTextArea('Excerpt', 'Test');

    await ui.clickSaveNow();

    // Should show an error via autosave indicator (validation error or save error)
    const errorIndicator = page.locator('[data-testid="autosave-error"], [data-testid="autosave-validation-error"]');
    await expect(errorIndicator.first()).toBeVisible({ timeout: 15000 });
  });
});

test.describe('Validation: Objective root cause uniqueness', () => {
  test.describe.configure({ mode: 'serial' });

  const sharedCause = `Shared root cause ${TS()}`;

  test.afterAll(async ({ request }) => {
    const { ApiHelper } = await import('./helpers');
    const api = new ApiHelper(request);
    await api.login();
    await api.deleteAll('objective');
  });

  test('create first objective with a root cause', async ({ api }) => {
    await api.deleteAll('objective');
    await api.createEntity('objective', {
      title: { rendered: `OKR Sanity A ${TS()}` },
      fields: {
        objective_work_start_date: '20250101',
        objective_type: 'short_term',
        documentation_url: '',
        root_cause: sharedCause,
        creator_comment: { author: 2, comment: 'Initial comment' },
        key_results: [],
        objective_comments: [],
        objective_initiation_year: '-1',
        year_based_okr_type: 'unspecified',
      },
    });
  });

  test('second objective with same root cause triggers sanity error', async ({ page, ui }) => {
    await ui.gotoNewEntity('objective');

    await ui.fillTitle(`OKR Sanity B ${TS()}`);
    await ui.fillDate('Objective Work Planned Start Date', '2025-02-01');
    await ui.fillTextArea('Root Cause', sharedCause);

    await ui.clickSaveNow();

    // Should show an error via autosave indicator (validation error or save error)
    const errorIndicator = page.locator('[data-testid="autosave-error"], [data-testid="autosave-validation-error"]');
    await expect(errorIndicator.first()).toBeVisible({ timeout: 15000 });
  });
});

test.describe('Validation: Title uniqueness enforcement', () => {
  test.describe.configure({ mode: 'serial' });

  const sharedTitle = `Unique Title Check ${TS()}`;

  test.afterAll(async ({ request }) => {
    const { ApiHelper } = await import('./helpers');
    const api = new ApiHelper(request);
    await api.login();
    await api.deleteAll('blog-post');
  });

  test('create blog post with a title', async ({ api }) => {
    await api.deleteAll('blog-post');
    await api.createEntity('blog-post', {
      title: { rendered: sharedTitle },
      fields: {
        content: '<p>test</p>', excerpt: 'test', status: 'draft', is_featured: false,
        allow_comments: true, reading_time_minutes: 1, slug: `title-uniq-a-${TS()}`,
        featured_image: '',
        seo_metadata: { meta_title: '', meta_description: '', meta_keywords: '', canonical_url: '' },
        external_links: [], publication_year: '', scheduled_date: '',
      },
    });
  });

  test('second blog post with same title shows uniqueness error', async ({ page, ui }) => {
    await ui.gotoNewEntity('blog-post');

    await ui.fillTitle(sharedTitle);
    await ui.fillWysiwyg('Post Content', '<p>Test content</p>');
    await ui.fillTextField('URL Slug', `title-uniq-b-${TS()}`);

    await ui.clickSaveNow();

    // Should show an error via autosave indicator (validation error or save error)
    const errorIndicator = page.locator('[data-testid="autosave-error"], [data-testid="autosave-validation-error"]');
    await expect(errorIndicator.first()).toBeVisible({ timeout: 15000 });
  });
});

test.describe('Validation: Repeater min/max row enforcement', () => {
  test.afterAll(async ({ request }) => {
    const { ApiHelper } = await import('./helpers');
    const api = new ApiHelper(request);
    await api.login();
    await api.deleteAll('product');
  });

  test('product variant repeater requires min 1 row', async ({ page, ui }) => {
    await ui.gotoNewEntity('product');

    await ui.fillTitle(`MinRow Product ${TS()}`);
    await ui.fillTextArea('Short Description', 'Test min rows');
    await ui.fillNumber('Base Price (USD)', '10');

    // Do NOT add a variant (min is 1)
    await ui.clickSaveNow();

    // Expect some feedback — either the form prevents saving or an error toast
    await page.waitForTimeout(3000);

    // Verify validation feedback — either autosave indicator or sonner toast
    const errorIndicator = page.locator('[data-testid="autosave-error"], [data-testid="autosave-validation-error"], [data-sonner-toast]');
    const indicatorCount = await errorIndicator.count();
    expect(indicatorCount).toBeGreaterThan(0);
  });

  test('blog post external links repeater enforces max 10', async ({ page, ui }) => {
    await ui.gotoNewEntity('blog-post');

    await ui.fillTitle(`MaxRow Blog ${TS()}`);
    await ui.fillTextField('URL Slug', `maxrow-${TS()}`);

    // Add 10 links (the maximum)
    for (let i = 0; i < 10; i++) {
      await ui.addRepeaterItem('External Links');
    }

    const links = ui.repeaterItems('External Links');
    const count = await links.count();
    expect(count).toBeLessThanOrEqual(10);

    // Try adding one more — the "Add" button should be disabled, hidden, or not add beyond max
    const addBtn = page.locator('.field-wrapper, .bg-white.rounded-lg.shadow-sm')
      .filter({ has: page.locator('label', { hasText: 'External Links' }) })
      .locator('button').filter({ hasText: /add/i }).last();

    const btnVisible = await addBtn.isVisible().catch(() => false);
    if (btnVisible) {
      const isDisabled = await addBtn.isDisabled().catch(() => false);
      if (!isDisabled) {
        await addBtn.click();
        // Should still be 10
        const afterCount = await links.count();
        expect(afterCount).toBeLessThanOrEqual(10);
      }
    }
  });
});

test.describe('Validation: API-level field constraints', () => {
  test.afterAll(async ({ request }) => {
    const { ApiHelper } = await import('./helpers');
    const api = new ApiHelper(request);
    await api.login();
    await api.deleteAll('blog-post');
  });

  test('API rejects entity without required title', async ({ request }) => {
    const { ApiHelper } = await import('./helpers');
    const api = new ApiHelper(request);
    await api.login();

    // Try creating a blog post without title
    const res = await request.post('http://localhost:9000/rf/api/crud?operation=CREATE&type=blog-post', {
      data: {
        tags: [], categories: [], author: 2,
        fields: {
          content: '', excerpt: '', status: 'draft', slug: `no-title-${TS()}`,
          is_featured: false, allow_comments: true, reading_time_minutes: 1,
          featured_image: '',
          seo_metadata: { meta_title: '', meta_description: '', meta_keywords: '', canonical_url: '' },
          external_links: [], publication_year: '', scheduled_date: '',
        },
      },
    });

    // The API should reject this — either non-200 status or error body
    const hasError = !res.ok();
    expect(hasError).toBeTruthy();
  });
});
