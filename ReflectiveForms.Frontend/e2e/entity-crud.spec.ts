import { test, expect } from './helpers';

/**
 * E2E tests for Entity CRUD operations via the React SPA.
 * Uses authenticated UI helper with proper routes.
 */
test.describe('Entity CRUD Operations', () => {
  test.describe.configure({ mode: 'serial' });
  const ENTITY = 'team-member';
  let createdId: number;

  test.afterAll(async ({ api }) => {
    if (createdId) {
      await api.deleteEntity(ENTITY, createdId).catch(() => {});
    }
  });

  test('should display dashboard with entity types', async ({ ui }) => {
    await ui.gotoDashboard();
    await expect(ui.page.locator('h1')).toContainText(/dashboard/i);
    // Sidebar should list entity types
    await expect(ui.page.locator('nav')).toBeVisible();
  });

  test('should navigate to entity list page', async ({ ui }) => {
    await ui.gotoEntityList(ENTITY);
    await expect(ui.page.locator('h1')).toBeVisible();
    await expect(ui.page.locator('table')).toBeVisible();
  });

  test('should create a new entity', async ({ ui, api }) => {
    await ui.gotoNewEntity(ENTITY);

    // Fill in the title
    await ui.fillTitle(`CRUD Test Entity ${Date.now()}`);

    // Fill fields for team-member (all required fields)
    await ui.fillTextField('Job Title', 'Engineer');
    await ui.fillTextField('Work Email', 'john@test.com');
    // Keep Remote Worker unchecked (default false) so Office Address group is visible
    // (it has DisplayCondition "is_remote == false")
    await ui.fillDate('Hire Date', '2026-01-01');

    // Office Address group (visible when is_remote == false)
    await ui.fillTextField('Street Address', '123 Test St');
    await ui.fillTextField('City', 'Testville');
    await ui.fillTextField('Postal Code', '12345');

    // Emergency Contacts (one item auto-created by min_items=1)
    await ui.fillTextField('Contact Name', 'Emergency Person');
    await ui.fillTextField('Phone Number', '555-0000');
    await ui.selectOption('Relationship', 'friend');

    await ui.clickSaveNow();
    await ui.waitForSave();

    // Verify entity was created via API
    const entities = await api.peekAll(ENTITY);
    const found = entities.find(e => (e.title ?? '').includes('CRUD Test Entity'));
    expect(found).toBeTruthy();
    createdId = found!.id;
  });

  test('should edit an existing entity', async ({ ui, api }) => {
    await ui.gotoEditEntity(ENTITY, createdId);

    // Modify the title
    await ui.fillTitle('Updated CRUD Test Entity');

    await ui.clickSaveNow();
    await ui.waitForSave();

    // Verify via API
    const entity = await api.readEntity(ENTITY, createdId);
    expect(entity.title.rendered).toBe('Updated CRUD Test Entity');
  });

  test('should delete an entity', async ({ ui, api, page }) => {
    // Ensure we have an entity to delete
    const beforeList = await api.peekAll(ENTITY);
    const countBefore = beforeList.length;

    await ui.gotoEntityList(ENTITY);

    // Find the row and delete it
    const rows = ui.entityRows();
    const rowCount = await rows.count();
    expect(rowCount).toBeGreaterThan(0);

    // Click delete on the first row and accept the confirm dialog
    page.on('dialog', dialog => dialog.accept());
    await ui.clickDeleteOnRow(0);

    // Wait for the deletion to complete
    await ui.page.waitForTimeout(2000);

    // Verify via API
    const afterList = await api.peekAll(ENTITY);
    expect(afterList.length).toBeLessThan(countBefore);
    createdId = 0; // Already deleted
  });
});
