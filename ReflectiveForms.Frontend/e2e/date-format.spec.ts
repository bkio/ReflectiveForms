import { test, expect } from './helpers';

/**
 * Date Format Tests
 *
 * Verifies that DatePicker fields work correctly with different
 * date_format values (yyyyMMdd, yyyy-MM-dd). The frontend must
 * normalize the HTML date input value (always yyyy-MM-dd) to
 * whatever format the backend expects.
 *
 * Uses blog-post (scheduled_date: yyyyMMdd) and the Sample1
 * event entity (start_date: yyyyMMdd, end_date: yyyyMMdd).
 */

const TS = () => Date.now().toString(36);

test.describe('Date Field Format Handling', () => {
  test.describe.configure({ mode: 'serial' });

  test.afterAll(async ({ request }) => {
    const { ApiHelper } = await import('./helpers');
    const api = new ApiHelper(request);
    await api.login();
    await api.deleteAll('blog-post');
  });

  test('create blog-post with yyyyMMdd date and save without format error', async ({ page, ui, api }) => {
    await api.deleteAll('blog-post');
    await ui.gotoNewEntity('blog-post');

    const title = `Date-Test ${TS()}`;
    await ui.fillTitle(title);
    await ui.fillTextField('URL Slug', `date-test-${TS()}`);
    // Fill required WYSIWYG
    const wysiwyg = page.locator('.wysiwyg-editor');
    await wysiwyg.locator('button', { hasText: /html/i }).click();
    await wysiwyg.locator('textarea').fill('<p>Date test content.</p>');
    await wysiwyg.locator('button', { hasText: /preview/i }).click();

    // Set status to "scheduled" so the date field becomes visible
    await ui.selectOption('Post Status', 'scheduled');

    // Fill the date field — HTML input always produces yyyy-MM-dd
    await ui.fillDate('Scheduled Publish Date', '2026-12-25');

    // No need for conditionally-visible fields — scheduled_date already shown

    // Save and verify
    await ui.clickSaveNow();
    await ui.waitForSave();

    // Navigate to view page and check the date appears
    const url = new URL(page.url());
    const id = url.searchParams.get('id');
    await page.goto(`/entities-view/blog-post?id=${id}`);
    await page.waitForLoadState('networkidle');

    // The date should be rendered somewhere on the page
    await expect(page.locator('body')).toContainText('2026');
  });

  test('leave date empty on optional field and save without error', async ({ page, ui, api }) => {
    await api.deleteAll('blog-post');
    await ui.gotoNewEntity('blog-post');

    const title = `Date-Empty ${TS()}`;
    await ui.fillTitle(title);
    await ui.fillTextField('URL Slug', `date-empty-${TS()}`);
    const wysiwyg = page.locator('.wysiwyg-editor');
    await wysiwyg.locator('button', { hasText: /html/i }).click();
    await wysiwyg.locator('textarea').fill('<p>No date fill.</p>');
    await wysiwyg.locator('button', { hasText: /preview/i }).click();

    // Fill conditionally-visible fields (scheduled_date is option so these need filling)
    await ui.fillTextField('Meta Title', 'E2E Meta');
    await ui.fillTextArea('Meta Description', 'E2E Description');

    // Do NOT fill the date — it's optional

    await ui.clickSaveNow();
    await ui.waitForSave();

    // Save must succeed
    const url = new URL(page.url());
    expect(url.searchParams.get('id')).not.toBe('new');
  });
});
