import { test, expect } from './helpers';

const ENTITY = 'blog-post';
const TS = () => Date.now().toString(36);

/**
 * E2E tests for Repeater field operations.
 * Uses the blog-post entity which has an "External Links" repeater
 * with link_text (Text) and link_url (Url) sub-fields.
 */
test.describe('Repeater Field Operations', () => {
  test.describe.configure({ mode: 'serial' });

  test.afterAll(async ({ request }) => {
    const { ApiHelper } = await import('./helpers');
    const api = new ApiHelper(request);
    await api.login();
    await api.deleteAll(ENTITY);
  });

  test('should add a repeater item', async ({ ui }) => {
    await ui.gotoNewEntity(ENTITY);
    await ui.fillTitle(`Repeater Add ${TS()}`);

    // Count initial items (should be 0)
    const items = ui.repeaterItems('External Links');
    await expect(items).toHaveCount(0);

    // Add an item
    await ui.addRepeaterItem('External Links');
    await expect(items).toHaveCount(1);
  });

  test('should add multiple repeater items', async ({ ui }) => {
    await ui.gotoNewEntity(ENTITY);
    await ui.fillTitle(`Repeater Multi ${TS()}`);

    await ui.addRepeaterItem('External Links');
    await ui.addRepeaterItem('External Links');
    await ui.addRepeaterItem('External Links');

    const items = ui.repeaterItems('External Links');
    await expect(items).toHaveCount(3);
  });

  test('should fill nested fields in repeater items', async ({ ui }) => {
    await ui.gotoNewEntity(ENTITY);
    await ui.fillTitle(`Repeater Fill ${TS()}`);

    await ui.addRepeaterItem('External Links');

    // Fill the sub-fields inside the first repeater item
    await ui.fillTextField('Link Title', 'Example');
    await ui.fillTextField('URL', 'https://example.com');

    // Verify values
    const items = ui.repeaterItems('External Links');
    await expect(items).toHaveCount(1);
    const linkTitle = items.first().locator('input').first();
    await expect(linkTitle).toHaveValue('Example');
  });

  test('should save repeater data successfully', async ({ api, ui }) => {
    await api.deleteAll(ENTITY);
    await ui.gotoNewEntity(ENTITY);
    const title = `Repeater Save ${TS()}`;
    await ui.fillTitle(title);

    // Fill required fields for blog-post
    await ui.fillWysiwyg('Post Content', '<p>Test content for repeater save</p>');
    await ui.fillTextArea('Excerpt', 'Test excerpt');
    await ui.fillTextField('URL Slug', `rpt-save-${TS()}`);

    // Select author (blog-post has has_author — uses SearchableSelect)
    const authorWrapper = ui.page.locator('[data-testid="author-select"]');
    const authorTrigger = authorWrapper.locator('button[aria-haspopup="listbox"]');
    await authorTrigger.click();
    const authorFirstOpt = authorWrapper.locator('[role="option"]').nth(1);
    await expect(authorFirstOpt).toBeVisible({ timeout: 10000 });
    await authorFirstOpt.click();

    await ui.addRepeaterItem('External Links');
    await ui.fillTextField('Link Title', 'Saved Link');
    await ui.fillTextField('URL', 'https://saved.example.com');
    await ui.clickSaveNow();
    await ui.waitForSave();

    // Verify entity was created
    const list = await api.peekAll(ENTITY);
    expect(list.length).toBeGreaterThan(0);
  });
});
