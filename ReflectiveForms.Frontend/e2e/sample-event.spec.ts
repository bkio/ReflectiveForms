import { test, expect } from './helpers';

/**
 * Event Entity – Full CRUD E2E Tests
 *
 * Covers: Deeply nested Groups (Event → Venue → Address), 2 Repeaters
 * (sessions with many field types, sponsors with media), DisplayCondition
 * (online vs. in-person: meeting_url / venue), Range slider (ticket pricing),
 * 2 DatePickers, Email, WysiwygEditor, Select (7 options), Url × 2,
 * Number (max attendees), Checkbox, Relation to team-member.
 *
 * Full cycle: create → list → read → update → conditional toggle → repeaters → delete
 */

const ENTITY = 'event';
const TS = () => Date.now().toString(36);

test.describe('Event CRUD', () => {
  let createdId: number;

  test.describe.configure({ mode: 'serial' });

  test.afterAll(async ({ request }) => {
    const { ApiHelper } = await import('./helpers');
    const api = new ApiHelper(request);
    await api.login();
    await api.deleteAll(ENTITY);
  });

  // ──────────────────────────────────────
  // CREATE — in-person event
  // ──────────────────────────────────────
  test('create an in-person event with all fields', async ({ page, ui, api }) => {
    await api.deleteAll(ENTITY);
    await ui.gotoNewEntity(ENTITY);

    await expect(page.locator('h1')).toContainText('New Event');

    // Title
    await ui.fillTitle(`E2E DevConf ${TS()}`);

    // WysiwygEditor — Event Description
    await ui.fillWysiwyg('Event Description', '<p>The premier developer conference.</p>');

    // Select — Event Type
    await ui.selectOption('Event Type', 'conference');

    // DatePicker — Start Date & End Date
    await ui.fillDate('Start Date', '2026-09-15');
    await ui.fillDate('End Date', '2026-09-17');

    // Checkbox — Online Event (false → should show Venue, hide Meeting URL)
    await ui.setCheckbox('Online Event', false);

    // Group — Venue Details → Venue Name
    await ui.fillTextField('Venue Name', 'Bay Area Convention Center');

    // Nested Group — Venue → Address (Grid3)
    await ui.fillTextField('Street Address', '500 Convention Way');
    await ui.fillTextField('City', 'San Jose');
    await ui.fillTextField('State / Province', 'CA');
    await ui.fillTextField('Postal Code', '95101');
    await ui.selectOption('Country', 'US');

    // Number — Venue Capacity
    await ui.fillNumber('Venue Capacity', '2000');

    // Url — Venue Website
    await ui.fillTextField('Venue Website', 'https://bacc.example.com');

    // Number — Maximum Attendees
    await ui.fillNumber('Maximum Attendees', '1500');

    // Range — Ticket Price
    await ui.setRange('Ticket Price (USD)', '250');

    // Email — Registration Contact Email
    await ui.fillTextField('Registration Contact Email', 'events@devconf.com');

    // Url — Registration Page URL
    await ui.fillTextField('Registration Page URL', 'https://eventbrite.com/devconf');

    // Repeater — Sessions: add one keynote
    await ui.addRepeaterItem('Sessions / Agenda');
    await ui.fillTextField('Session Title', 'Opening Keynote');
    await ui.fillTextField('Speaker Name', 'Dr. Ada Lovelace');
    await ui.fillTextField('Speaker Email', 'ada@devconf.com');
    await ui.fillDate('Session Date', '2026-09-15');
    await ui.fillNumber('Duration (minutes)', '60');
    await ui.selectOption('Session Type', 'keynote');
    await ui.fillTextArea('Session Description', 'Welcome and vision for the future.');

    // Repeater — Sponsors: add one
    await ui.addRepeaterItem('Sponsors');
    await ui.fillTextField('Sponsor Name', 'TechCorp');
    await ui.selectOption('Sponsor Tier', 'platinum');
    await ui.fillTextField('Sponsor Website', 'https://techcorp.example.com');

    // Save
    await ui.clickSaveNow();
    await ui.waitForSave();

    const entities = await api.peekAll(ENTITY);
    const created = entities.find(e => (e.title ?? e.name ?? '').includes('E2E DevConf'));
    expect(created).toBeDefined();
    createdId = created!.id;
  });

  // ──────────────────────────────────────
  // LIST
  // ──────────────────────────────────────
  test('event appears in the list page', async ({ page, ui }) => {
    await ui.gotoEntityList(ENTITY);
    await expect(page.locator('a', { hasText: /E2E DevConf/ })).toBeVisible();
  });

  // ──────────────────────────────────────
  // READ via API
  // ──────────────────────────────────────
  test('read event and verify all fields', async ({ api }) => {
    const entity = await api.readEntity(ENTITY, createdId);

    expect(entity.title.rendered).toContain('E2E DevConf');
    expect(entity.fields.event_type).toBe('conference');
    expect(entity.fields.is_online).toBe(false);
    expect(entity.fields.venue.venue_name).toBe('Bay Area Convention Center');
    expect(entity.fields.venue.venue_address.city).toBe('San Jose');
    expect(entity.fields.venue.venue_address.postal_code).toBe('95101');
    expect(entity.fields.venue.venue_url).toBe('https://bacc.example.com');
    expect(Number(entity.fields.venue.capacity)).toBe(2000);
    expect(Number(entity.fields.max_attendees)).toBe(1500);
    expect(entity.fields.registration_email).toBe('events@devconf.com');
    expect(entity.fields.registration_url).toBe('https://eventbrite.com/devconf');
    expect(entity.fields.sessions.length).toBe(1);
    expect(entity.fields.sessions[0].session_title).toBe('Opening Keynote');
    expect(entity.fields.sessions[0].speaker_name).toBe('Dr. Ada Lovelace');
    expect(entity.fields.sessions[0].session_type).toBe('keynote');
    expect(entity.fields.sponsors.length).toBe(1);
    expect(entity.fields.sponsors[0].sponsor_name).toBe('TechCorp');
    expect(entity.fields.sponsors[0].sponsor_tier).toBe('platinum');
  });

  // ──────────────────────────────────────
  // CONDITIONAL — online toggle
  // ──────────────────────────────────────
  test('DisplayCondition: toggling online shows meeting_url, hides venue', async ({ page, ui }) => {
    await ui.gotoEditEntity(ENTITY, createdId);

    // In-person mode: Venue should be visible, Meeting URL should not
    expect(await ui.fieldIsVisible('Venue Details')).toBe(true);
    expect(await ui.fieldIsVisible('Meeting URL')).toBe(false);

    // Toggle online ON
    await ui.setCheckbox('Online Event', true);
    await page.waitForTimeout(500);

    // Now Meeting URL visible, Venue hidden
    expect(await ui.fieldIsVisible('Meeting URL')).toBe(true);
    expect(await ui.fieldIsVisible('Venue Details')).toBe(false);

    // Toggle back to in-person
    await ui.setCheckbox('Online Event', false);
    await page.waitForTimeout(500);

    expect(await ui.fieldIsVisible('Venue Details')).toBe(true);
    expect(await ui.fieldIsVisible('Meeting URL')).toBe(false);

    await ui.clickSaveNow();
    await ui.waitForSave();
  });

  // ──────────────────────────────────────
  // UPDATE — add session, change ticket price
  // ──────────────────────────────────────
  test('update event: add workshop session and change ticket price', async ({ ui, api }) => {
    await ui.gotoEditEntity(ENTITY, createdId);

    // Change ticket price
    await ui.setRange('Ticket Price (USD)', '300');

    // Add second session
    await ui.addRepeaterItem('Sessions / Agenda');
    const sessions = ui.repeaterItems('Sessions / Agenda');
    const second = sessions.nth(1);
    await second.locator('input[type="text"]').first().fill('Hands-on Workshop');
    // Speaker Name is mandatory
    await second.locator('input[type="text"]').nth(1).fill('Dr. Workshop Lead');
    // Session Date is mandatory (yyyyMMdd format)
    await second.locator('input[type="date"]').first().fill('2026-07-15');
    // Duration (number, has default 30)

    await ui.clickSaveNow();
    await ui.waitForSave();

    const entity = await api.readEntity(ENTITY, createdId);
    expect(entity.fields.sessions.length).toBe(2);
    expect(entity.fields.sessions[1].session_title).toBe('Hands-on Workshop');
  });

  // ──────────────────────────────────────
  // UPDATE — add more sponsors
  // ──────────────────────────────────────
  test('add gold sponsor to event', async ({ ui, api }) => {
    await ui.gotoEditEntity(ENTITY, createdId);

    await ui.addRepeaterItem('Sponsors');
    const sponsors = ui.repeaterItems('Sponsors');
    const second = sponsors.nth(1);
    await second.locator('input[type="text"]').first().fill('CloudInc');

    await ui.clickSaveNow();
    await ui.waitForSave();

    const entity = await api.readEntity(ENTITY, createdId);
    expect(entity.fields.sponsors.length).toBe(2);
    expect(entity.fields.sponsors[1].sponsor_name).toBe('CloudInc');
  });

  // ──────────────────────────────────────
  // REPEATER — remove session
  // ──────────────────────────────────────
  test('remove workshop session', async ({ ui, api }) => {
    await ui.gotoEditEntity(ENTITY, createdId);

    const sessions = ui.repeaterItems('Sessions / Agenda');
    const countBefore = await sessions.count();

    const last = sessions.nth(countBefore - 1);
    await ui.safeClick(last.locator('button[title="Remove"]'));

    await ui.clickSaveNow();
    await ui.waitForSave();

    const entity = await api.readEntity(ENTITY, createdId);
    expect(entity.fields.sessions.length).toBe(countBefore - 1);
  });

  // ──────────────────────────────────────
  // LIST — verify after all updates
  // ──────────────────────────────────────
  test('list page reflects event after updates', async ({ page, ui }) => {
    await ui.gotoEntityList(ENTITY);
    await expect(page.locator('a', { hasText: /E2E DevConf/ })).toBeVisible();
  });

  // ──────────────────────────────────────
  // DELETE
  // ──────────────────────────────────────
  test('delete event and verify removal', async ({ page, ui, api }) => {
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
