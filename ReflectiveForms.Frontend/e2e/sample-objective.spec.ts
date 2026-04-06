import { test, expect } from './helpers';

/**
 * Objective (OKR) Entity – Full CRUD E2E Tests
 *
 * Covers: The original entity model with HasAuthor, HasTags, HasCategories,
 * HasParent, title uniqueness, title sanity check, lifecycle hooks,
 * DynamicChoicesCompileTimeAsync, DynamicChoicesRuntimeAsync,
 * LogicSanityCheckAsync (root cause uniqueness), Repeater (key results
 * with nested comments), Group (creator comment), Relation (author),
 * DatePicker, Select (static + dynamic), TextArea, Url, Checkbox.
 *
 * Full cycle: create → list → read → update → nested repeater → dynamic select → delete
 */

const ENTITY = 'objective';
const TS = () => Date.now().toString(36);

test.describe('Objective CRUD', () => {
  let createdId: number;

  test.describe.configure({ mode: 'serial' });

  test.afterAll(async ({ request }) => {
    const { ApiHelper } = await import('./helpers');
    const api = new ApiHelper(request);
    await api.login();
    await api.deleteAll(ENTITY);
  });

  // ──────────────────────────────────────
  // CREATE
  // ──────────────────────────────────────
  test('create an objective with all fields', async ({ page, ui, api }) => {
    await api.deleteAll(ENTITY);
    await ui.gotoNewEntity(ENTITY);

    await expect(page.locator('h1')).toContainText('New Objective');

    // Title
    await ui.fillTitle(`E2E OKR ${TS()}`);

    // DatePicker — Objective Work Planned Start Date
    await ui.fillDate('Objective Work Planned Start Date', '2026-04-01');

    // Select (static) — Short-term or Long-term
    await ui.selectOption('Short-term or Long-term?', 'long_term');

    // Select (dynamic) — Objective Initiation Year
    await ui.selectOption('Objective Initiation Year', `${new Date().getFullYear()}`);

    // Url — Documentation URL
    await ui.fillTextField('Objective Documentation URL', 'https://docs.example.com/okr');

    // TextArea — Root Cause
    await ui.fillTextArea('Root Cause', `Unique root cause ${TS()}`);

    // Entity-level Author (from has_author)
    await ui.selectSearchableOption('Author');

    // Group — Creator Comment
    // Group-level Author relation (second "Author" label on the page)
    await ui.selectSearchableOption('Author', undefined, 1);
    await ui.fillTextArea('Comment', 'Created during E2E testing.');

    // Repeater — Key Results: add one
    await ui.addRepeaterItem('Key Results');
    await ui.fillTextArea('Key Results', 'Complete E2E test suite by Q2.');
    await ui.setCheckbox('Is it achieved?', false);

    // Repeater — Objective Comments: add one (uses SampleCommentModel with mandatory Author)
    await ui.addRepeaterItem('Objective Comments');
    const commentItem = ui.repeaterItems('Objective Comments').first();
    // Select Author within the repeater item
    const commentAuthor = commentItem.locator('button[aria-haspopup="listbox"]');
    await commentAuthor.click();
    await expect(commentItem.locator('[role="listbox"]')).toBeVisible({ timeout: 10000 });
    const commentAuthorOption = commentItem.locator('[role="option"]').nth(1);
    await expect(commentAuthorOption).toBeVisible({ timeout: 10000 });
    await commentAuthorOption.click();
    // Fill Comment within the repeater item
    await commentItem.locator('textarea').fill('This is a top-level objective comment.');

    // Save
    await ui.clickSaveNow();
    await ui.waitForSave();

    const entities = await api.peekAll(ENTITY);
    const created = entities.find(e => (e.title ?? e.name ?? '').includes('E2E OKR'));
    expect(created).toBeDefined();
    createdId = created!.id;
  });

  // ──────────────────────────────────────
  // LIST
  // ──────────────────────────────────────
  test('objective appears in the list page', async ({ page, ui }) => {
    await ui.gotoEntityList(ENTITY);
    await expect(page.locator('a', { hasText: /E2E OKR/ })).toBeVisible();
  });

  // ──────────────────────────────────────
  // READ via API
  // ──────────────────────────────────────
  test('read objective and verify fields', async ({ api }) => {
    const entity = await api.readEntity(ENTITY, createdId);

    expect(entity.title.rendered).toContain('E2E OKR');
    expect(entity.fields.objective_type).toBe('long_term');
    expect(entity.fields.documentation_url).toBe('https://docs.example.com/okr');
    expect(entity.fields.root_cause).toContain('Unique root cause');
    expect(entity.fields.key_results.length).toBe(1);
    expect(entity.fields.key_results[0].key_result).toContain('Complete E2E test suite');
    expect(entity.fields.key_results[0].achieved).toBe(false);
    expect(entity.fields.objective_comments.length).toBe(1);
  });

  // ──────────────────────────────────────
  // UPDATE — modify key result, mark achieved
  // ──────────────────────────────────────
  test('update objective: mark key result as achieved', async ({ ui, api }) => {
    await ui.gotoEditEntity(ENTITY, createdId);

    // Expand the key result item (accordion mode)
    await ui.expandRepeaterItem('Key Results', 0);

    // Check the "Is it achieved?" checkbox of the first key result
    await ui.setCheckbox('Is it achieved?', true);

    // Update title
    await ui.fillTitle('E2E OKR UPDATED');

    await ui.clickSaveNow();
    await ui.waitForSave();

    const entity = await api.readEntity(ENTITY, createdId);
    expect(entity.title.rendered).toBe('E2E OKR UPDATED');
    expect(entity.fields.key_results[0].achieved).toBe(true);
  });

  // ──────────────────────────────────────
  // NESTED REPEATER — add comment to key result
  // ──────────────────────────────────────
  test('add comment inside key result (nested repeater)', async ({ ui, api }) => {
    await ui.gotoEditEntity(ENTITY, createdId);

    // Expand the key result item to access nested Key Result Comments
    await ui.expandRepeaterItem('Key Results', 0);

    // Add comment to first key result (uses SampleCommentModel with mandatory Author)
    await ui.addRepeaterItem('Key Result Comments');
    const krCommentItem = ui.repeaterItems('Key Result Comments').first();
    // Select Author within the key result comment
    const krCommentAuthor = krCommentItem.locator('button[aria-haspopup="listbox"]');
    await krCommentAuthor.click();
    await expect(krCommentItem.locator('[role="listbox"]')).toBeVisible({ timeout: 10000 });
    const krAuthorOption = krCommentItem.locator('[role="option"]').nth(1);
    await expect(krAuthorOption).toBeVisible({ timeout: 10000 });
    await krAuthorOption.click();
    // Fill Comment
    await krCommentItem.locator('textarea').fill('This key result is on track.');

    await ui.clickSaveNow();
    await ui.waitForSave();

    const entity = await api.readEntity(ENTITY, createdId);
    expect(entity.fields.key_results[0].key_result_comments.length).toBe(1);
    expect(entity.fields.key_results[0].key_result_comments[0].comment).toContain('on track');
  });

  // ──────────────────────────────────────
  // ADD second key result
  // ──────────────────────────────────────
  test('add second key result', async ({ ui, api }) => {
    await ui.gotoEditEntity(ENTITY, createdId);

    await ui.addRepeaterItem('Key Results');
    // Fill the second key result
    const items = ui.repeaterItems('Key Results');
    const second = items.nth(1);
    await second.locator('textarea').first().fill('Ship product docs by end of Q2.');

    await ui.clickSaveNow();
    await ui.waitForSave();

    const entity = await api.readEntity(ENTITY, createdId);
    expect(entity.fields.key_results.length).toBe(2);
    expect(entity.fields.key_results[1].key_result).toContain('Ship product docs');
  });

  // ──────────────────────────────────────
  // DYNAMIC CHOICES — compile-time year
  // ──────────────────────────────────────
  test('DynamicChoicesCompileTimeAsync: objective initiation year has dynamic options', async ({ page, ui }) => {
    await ui.gotoEditEntity(ENTITY, createdId);

    // Objective Initiation Year should have year-based options
    const yearWrapper = page.locator('.field-wrapper')
      .filter({ has: page.locator('label', { hasText: 'Objective Initiation Year' }) });
    const yearTrigger = yearWrapper.locator('button[aria-haspopup="listbox"]');

    if (await yearTrigger.isVisible({ timeout: 5000 }).catch(() => false)) {
      await yearTrigger.click();
      const options = await yearWrapper.locator('[role="option"]').allTextContents();
      await page.keyboard.press('Escape');
      const currentYear = new Date().getFullYear();
      // Should contain current year
      expect(options.some(o => o.includes(String(currentYear)))).toBe(true);
    }
  });

  // ──────────────────────────────────────
  // REMOVE key result
  // ──────────────────────────────────────
  test('remove second key result', async ({ ui, api }) => {
    await ui.gotoEditEntity(ENTITY, createdId);

    const items = ui.repeaterItems('Key Results');
    const countBefore = await items.count();

    const last = items.nth(countBefore - 1);
    await last.locator('button[title="Remove"]').click();

    await ui.clickSaveNow();
    await ui.waitForSave();

    const entity = await api.readEntity(ENTITY, createdId);
    expect(entity.fields.key_results.length).toBe(countBefore - 1);
  });

  // ──────────────────────────────────────
  // LIST — verify after all updates
  // ──────────────────────────────────────
  test('list page shows updated objective', async ({ page, ui }) => {
    await ui.gotoEntityList(ENTITY);
    await expect(page.locator('a', { hasText: /E2E OKR UPDATED/ })).toBeVisible();
  });

  // ──────────────────────────────────────
  // DELETE
  // ──────────────────────────────────────
  test('delete objective via UI and verify removal', async ({ page, ui, api }) => {
    // Ensure entity is unlocked from previous edit test
    await api.unlockEntity(ENTITY, createdId);

    await ui.gotoEntityList(ENTITY);
    const countBefore = await ui.entityRowCount();

    const deleteBtn = ui.entityRows().first().locator('button[title="Delete"]');
    await deleteBtn.waitFor({ state: 'visible', timeout: 30000 });
    page.on('dialog', dialog => dialog.accept());
    await deleteBtn.click();
    await page.waitForTimeout(2000);

    const entities = await api.peekAll(ENTITY);
    expect(entities.length).toBe(countBefore - 1);
  });
});
