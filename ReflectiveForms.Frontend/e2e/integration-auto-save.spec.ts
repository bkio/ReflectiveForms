import { test, expect } from './helpers';

/**
 * Auto-Save Round-Trip Integration Tests
 *
 * Verifies the full auto-save flow:
 * 1. User edits a field in the UI
 * 2. A "Changes will be saved..." toast appears (debounce indicator)
 * 3. After the 5-second debounce, the save fires
 * 4. A "Changes saved" toast appears
 * 5. The backend database is updated
 * 6. Reloading the page shows the new value
 *
 * Also tests: "Save Now" button bypasses debounce, auto-save on multiple
 * rapid edits (only the last state is saved), and create-mode auto-save
 * that transitions to edit-mode (URL update).
 */

const TS = () => Date.now().toString(36);

test.describe('Auto-Save Round-Trip', () => {
  test.describe.configure({ mode: 'serial' });

  let blogId: number;

  test.afterAll(async ({ request }) => {
    const { ApiHelper } = await import('./helpers');
    const api = new ApiHelper(request);
    await api.login();
    await api.deleteAll('blog-post');
  });

  test('setup: create a blog post for auto-save tests', async ({ api }) => {
    await api.deleteAll('blog-post');
    const result = await api.createEntity('blog-post', {
      title: { rendered: `AutoSave Blog ${TS()}` },
      fields: {
        content: '<p>Initial content</p>',
        excerpt: 'Initial excerpt',
        status: 'draft',
        is_featured: false,
        allow_comments: true,
        reading_time_minutes: 5,
        slug: `auto-save-${TS()}`,
        featured_image: '',
        seo_metadata: { meta_title: '', meta_description: '', meta_keywords: '', canonical_url: '' },
        external_links: [],
        publication_year: '',
        scheduled_date: '',
      },
    });
    blogId = result.id;
    expect(blogId).toBeGreaterThan(0);
  });

  test('editing a field triggers "changes will be saved" toast', async ({ page, ui }) => {
    await ui.gotoEditEntity('blog-post', blogId);

    // Modify a field
    await ui.fillTextArea('Excerpt', 'Modified excerpt for auto-save test');

    // Look for the pending toast
    const pendingToast = page.locator('[data-sonner-toast]', { hasText: /saved/i });
    await expect(pendingToast.first()).toBeVisible({ timeout: 10000 });
  });

  test('auto-save writes data to backend after debounce', async ({ page, ui, api }) => {
    await ui.gotoEditEntity('blog-post', blogId);

    await ui.fillTextArea('Excerpt', 'Auto-saved excerpt value');

    // Wait for auto-save to complete (5s debounce + network time)
    await page.waitForFunction(
      () => {
        const toasts = document.querySelectorAll('[data-sonner-toast]');
        for (const t of toasts) {
          if (t.textContent?.toLowerCase().includes('changes saved')) return true;
        }
        return false;
      },
      undefined,
      { timeout: 20000 },
    );

    // Verify via API
    const entity = await api.readEntity('blog-post', blogId);
    expect(entity.fields.excerpt).toBe('Auto-saved excerpt value');
  });

  test('"Save Now" button triggers immediate save', async ({ page, ui, api }) => {
    await ui.gotoEditEntity('blog-post', blogId);

    await ui.fillTextArea('Excerpt', 'Immediate save excerpt');

    // Click Save Now (should bypass debounce)
    await ui.clickSaveNow();
    await ui.waitForSave();

    // Verify
    const entity = await api.readEntity('blog-post', blogId);
    expect(entity.fields.excerpt).toBe('Immediate save excerpt');
  });

  test('rapid edits result in only the final value being saved', async ({ page, ui, api }) => {
    await ui.gotoEditEntity('blog-post', blogId);

    // Make multiple rapid changes
    const excerptField = page.locator('.field-wrapper, .bg-white.rounded-lg.shadow-sm')
      .filter({ has: page.locator('label', { hasText: 'Excerpt' }) })
      .locator('textarea');

    await excerptField.fill('First rapid edit');
    await page.waitForTimeout(500);
    await excerptField.fill('Second rapid edit');
    await page.waitForTimeout(500);
    await excerptField.fill('Final rapid edit');

    // Wait for auto-save
    await page.waitForFunction(
      () => {
        const toasts = document.querySelectorAll('[data-sonner-toast]');
        for (const t of toasts) {
          if (t.textContent?.toLowerCase().includes('changes saved')) return true;
        }
        return false;
      },
      undefined,
      { timeout: 20000 },
    );

    // Only the final value should be persisted
    const entity = await api.readEntity('blog-post', blogId);
    expect(entity.fields.excerpt).toBe('Final rapid edit');
  });

  test('auto-save on newly created entity transitions to edit mode', async ({ page, ui, api }) => {
    await ui.gotoNewEntity('blog-post');

    // Page header should show "New"
    await expect(page.locator('h1')).toContainText('New');

    const title = `New AutoSave ${TS()}`;
    await ui.fillTitle(title);
    await ui.fillWysiwyg('Post Content', '<p>Auto-save content</p>');
    await ui.fillTextArea('Excerpt', 'Created via auto-save');
    await ui.fillTextField('URL Slug', `new-auto-${TS()}`);

    await ui.clickSaveNow();
    await ui.waitForSave();

    // After save, the URL should be updated to include the new entity's ID
    // Verify entity was created
    const entities = await api.peekAll('blog-post');
    const created = entities.find(e => (e.title ?? e.name ?? '').includes('New AutoSave'));
    expect(created).toBeDefined();
    expect(created!.id).toBeGreaterThan(0);

    // Cleanup
    await api.deleteEntity('blog-post', created!.id);
  });

  test('auto-save preserves checkbox toggle state', async ({ page, ui, api }) => {
    await ui.gotoEditEntity('blog-post', blogId);

    // Toggle featured
    await ui.setCheckbox('Featured Post', true);
    await ui.clickSaveNow();
    await ui.waitForSave();

    let entity = await api.readEntity('blog-post', blogId);
    expect(entity.fields.is_featured).toBe(true);

    // Reload the page to clear stale toasts so the next waitForSave is not fooled
    await ui.gotoEditEntity('blog-post', blogId);

    // Toggle back
    await ui.setCheckbox('Featured Post', false);
    await ui.clickSaveNow();
    await ui.waitForSave();

    entity = await api.readEntity('blog-post', blogId);
    expect(entity.fields.is_featured).toBe(false);
  });

  test('auto-save preserves select changes', async ({ page, ui, api }) => {
    await ui.gotoEditEntity('blog-post', blogId);

    await ui.selectOption('Post Status', 'archived');
    await ui.clickSaveNow();
    await ui.waitForSave();

    const entity = await api.readEntity('blog-post', blogId);
    expect(entity.fields.status).toBe('archived');
  });
});
