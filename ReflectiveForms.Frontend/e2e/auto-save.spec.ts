import { test, expect } from './helpers';

const ENTITY = 'team-member';
const TS = () => Date.now().toString(36);

/**
 * E2E tests for Auto-save and Validation functionality.
 * Uses the team-member entity (no author required, simple fields).
 */
test.describe('Auto-save & Validation', () => {
  test.describe.configure({ mode: 'serial' });

  test.afterAll(async ({ request }) => {
    const { ApiHelper } = await import('./helpers');
    const api = new ApiHelper(request);
    await api.login();
    await api.deleteAll(ENTITY);
  });

  test('should show auto-save pending message on field change', async ({ ui }) => {
    await ui.gotoNewEntity(ENTITY);
    await ui.fillTitle(`Autosave Pending ${TS()}`);
    await expect(ui.page.locator('[data-testid="autosave-countdown"]')).toBeVisible({ timeout: 10000 });
  });

  test('should show error toast when saving with empty required fields', async ({ page, ui }) => {
    await ui.gotoNewEntity(ENTITY);
    // Fill title to pass Zod validation, but leave required backend fields empty
    await ui.fillTitle(`Incomplete ${TS()}`);
    await ui.clickSaveNow();
    // Backend sanity check should fail — expect an error via autosave indicator
    const errorIndicator = page.locator('[data-testid="autosave-error"], [data-testid="autosave-validation-error"]');
    await expect(errorIndicator.first()).toBeVisible({ timeout: 15000 });
  });

  test('should save and reload preserving form state', async ({ page, ui, api }) => {
    await api.deleteAll(ENTITY);
    await ui.gotoNewEntity(ENTITY);
    const title = `Persist ${TS()}`;
    await ui.fillTitle(title);
    await ui.fillTextField('Work Email', 'persist@example.com');
    await ui.selectOption('Department', 'engineering');
    await ui.fillTextField('Job Title', 'Engineer');
    await ui.fillDate('Hire Date', '2024-01-15');
    // is_remote defaults to false so Office Address group is visible with mandatory fields
    await ui.fillTextField('Street Address', '123 Main St');
    await ui.fillTextField('City', 'Testville');
    await ui.fillTextField('Postal Code', '90210');
    // Emergency Contacts repeater has minimumRows=1, so one row is pre-populated
    await ui.fillTextField('Contact Name', 'Jane Doe');
    await ui.fillTextField('Phone Number', '+1 555 123 4567');
    await ui.clickSaveNow();
    await ui.waitForSave();

    // Find the saved entity via API
    const list = await api.peekAll(ENTITY);
    const saved = list.find(e => e.title === title);
    expect(saved).toBeTruthy();

    // Navigate to edit it and verify title persisted
    await ui.gotoEditEntity(ENTITY, saved!.id);
    await expect(page.locator('input[name="title.rendered"]')).toHaveValue(title);
  });
});
