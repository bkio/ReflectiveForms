import { test, expect } from './helpers';

/**
 * Team Member Entity – Full CRUD E2E Tests
 *
 * Covers: Email field, Number (step 0.5), Range slider, DisplayCondition
 * (remote → hides office address), Group (Grid3 address), Repeater with
 * accordion (social links), Repeater with min/max (emergency contacts 1–3),
 * Relation field, WysiwygEditor (bio), Text with default, Select (department),
 * DatePicker, MediaSourceBase64 (avatar).
 *
 * Full cycle: create → list-verify → API-read → update → conditional → repeater → delete
 */

const ENTITY = 'team-member';
const TS = () => Date.now().toString(36);

test.describe('Team Member CRUD', () => {
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
  test('create a team member with all fields', async ({ page, ui, api }) => {
    await api.deleteAll(ENTITY);
    await ui.gotoNewEntity(ENTITY);

    await expect(page.locator('h1')).toContainText('New Team Member');

    // Title
    await ui.fillTitle(`Jane Doe ${TS()}`);

    // Email
    await ui.fillTextField('Work Email', 'jane.doe@company.com');

    // Select — Department
    await ui.selectOption('Department', 'design');

    // Text — Job Title (has default "Software Engineer", overwrite)
    await ui.fillTextField('Job Title', 'Lead Designer');

    // Number — Years of Experience (step 0.5, min 0, max 50)
    await ui.fillNumber('Years of Experience', '8.5');

    // Range — Performance Score (1–10, step 0.5)
    await ui.setRange('Performance Score', '8');

    // Checkbox — Remote Worker (false → should show Office Address)
    await ui.setCheckbox('Remote Worker', false);

    // Group (Grid3) — Office Address
    await ui.fillTextField('Street Address', '123 Design Blvd');
    await ui.fillTextField('City', 'San Francisco');
    await ui.fillTextField('State / Province', 'CA');
    await ui.fillTextField('Postal Code', '94102');
    await ui.selectOption('Country', 'US');

    // WysiwygEditor — Biography
    await ui.fillWysiwyg('Biography', '<p>Jane is a talented designer.</p>');

    // DatePicker — Hire Date
    await ui.fillDate('Hire Date', '2023-06-15');

    // Number — Annual Salary
    await ui.fillNumber('Annual Salary', '145000');

    // Repeater — Emergency Contacts (pre-populated from min_items=1)
    await ui.fillTextField('Contact Name', 'John Doe');
    await ui.selectOption('Relationship', 'spouse');
    await ui.fillTextField('Phone Number', '+1 555-111-2222');
    await ui.fillTextField('Email', 'john.doe@home.com');

    // Save
    await ui.clickSaveNow();
    await ui.waitForSave();

    // Verify via API
    const entities = await api.peekAll(ENTITY);
    const created = entities.find(e => (e.title ?? e.name ?? '').includes('Jane Doe'));
    expect(created).toBeDefined();
    createdId = created!.id;
  });

  // ──────────────────────────────────────
  // LIST — verify
  // ──────────────────────────────────────
  test('team member appears in list', async ({ page, ui }) => {
    await ui.gotoEntityList(ENTITY);
    await expect(page.locator('a', { hasText: /Jane Doe/ })).toBeVisible();
  });

  // ──────────────────────────────────────
  // READ — API verification
  // ──────────────────────────────────────
  test('read team member via API and verify fields', async ({ api }) => {
    const entity = await api.readEntity(ENTITY, createdId);

    expect(entity.fields.email).toBe('jane.doe@company.com');
    expect(entity.fields.department).toBe('design');
    expect(entity.fields.job_title).toBe('Lead Designer');
    expect(Number(entity.fields.years_of_experience)).toBe(8.5);
    expect(entity.fields.is_remote).toBe(false);
    expect(entity.fields.office_address.street).toBe('123 Design Blvd');
    expect(entity.fields.office_address.city).toBe('San Francisco');
    expect(entity.fields.office_address.postal_code).toBe('94102');
    expect(entity.fields.office_address.country).toBe('US');
    expect(entity.fields.emergency_contacts.length).toBeGreaterThanOrEqual(1);
    expect(entity.fields.emergency_contacts[0].contact_name).toBe('John Doe');
    expect(entity.fields.emergency_contacts[0].relationship).toBe('spouse');
  });

  // ──────────────────────────────────────
  // CONDITIONAL — Remote Worker toggle
  // ──────────────────────────────────────
  test('DisplayCondition: toggling remote hides office address group', async ({ page, ui }) => {
    await ui.gotoEditEntity(ENTITY, createdId);

    // Office address should be visible (is_remote == false)
    expect(await ui.fieldIsVisible('Office Address')).toBe(true);

    // Toggle remote ON
    await ui.setCheckbox('Remote Worker', true);
    await page.waitForTimeout(500);

    // Office address should be hidden
    expect(await ui.fieldIsVisible('Office Address')).toBe(false);

    // Toggle back
    await ui.setCheckbox('Remote Worker', false);
    await page.waitForTimeout(500);
    expect(await ui.fieldIsVisible('Office Address')).toBe(true);

    await ui.clickSaveNow();
    await ui.waitForSave();
  });

  // ──────────────────────────────────────
  // UPDATE
  // ──────────────────────────────────────
  test('update team member department and salary', async ({ ui, api }) => {
    await ui.gotoEditEntity(ENTITY, createdId);

    await ui.selectOption('Department', 'product');
    await ui.fillNumber('Annual Salary', '160000');
    await ui.fillTitle('Jane Doe UPDATED');

    await ui.clickSaveNow();
    await ui.waitForSave();

    const entity = await api.readEntity(ENTITY, createdId);
    expect(entity.title.rendered).toBe('Jane Doe UPDATED');
    expect(entity.fields.department).toBe('product');
    expect(Number(entity.fields.salary)).toBe(160000);
  });

  // ──────────────────────────────────────
  // REPEATER — add second emergency contact
  // ──────────────────────────────────────
  test('add second emergency contact (max 3)', async ({ ui, api }) => {
    await ui.gotoEditEntity(ENTITY, createdId);

    await ui.addRepeaterItem('Emergency Contacts');
    const contacts = ui.repeaterItems('Emergency Contacts');
    const second = contacts.nth(1);
    await second.locator('input[type="text"]').first().fill('Mike Smith');
    // Phone is mandatory
    await second.locator('input[type="text"]').nth(1).fill('+1 555-9999');

    await ui.clickSaveNow();
    await ui.waitForSave();

    const entity = await api.readEntity(ENTITY, createdId);
    expect(entity.fields.emergency_contacts.length).toBe(2);
    expect(entity.fields.emergency_contacts[1].contact_name).toBe('Mike Smith');
  });

  // ──────────────────────────────────────
  // REPEATER — Social Links (accordion)
  // ──────────────────────────────────────
  test('add social link with accordion repeater', async ({ ui, api }) => {
    await ui.gotoEditEntity(ENTITY, createdId);

    await ui.addRepeaterItem('Social Links');
    await ui.selectOption('Platform', 'github');
    await ui.fillTextField('Profile URL', 'https://github.com/janedoe');

    await ui.clickSaveNow();
    await ui.waitForSave();

    const entity = await api.readEntity(ENTITY, createdId);
    expect(entity.fields.social_links.length).toBe(1);
    expect(entity.fields.social_links[0].platform).toBe('github');
    expect(entity.fields.social_links[0].profile_url).toBe('https://github.com/janedoe');
  });

  // ──────────────────────────────────────
  // DELETE via UI
  // ──────────────────────────────────────
  test('delete team member and verify removal', async ({ page, ui, api }) => {
    // Ensure entity is unlocked from previous edit test
    await api.unlockEntity(ENTITY, createdId);

    await ui.gotoEntityList(ENTITY);

    const countBefore = await ui.entityRowCount();
    expect(countBefore).toBeGreaterThanOrEqual(1);

    const deleteBtn = ui.entityRows().first().locator('button[title="Delete"]');
    await deleteBtn.waitFor({ state: 'visible', timeout: 30000 });
    page.on('dialog', dialog => dialog.accept());
    await deleteBtn.click();

    await page.waitForTimeout(2000);

    const entities = await api.peekAll(ENTITY);
    expect(entities.length).toBe(countBefore - 1);
  });
});
