import { test, expect } from './helpers';

const ENTITY = 'event';
const TS = () => Date.now().toString(36);

/**
 * E2E tests for Conditional Field visibility.
 * Uses the event entity which has Online Event → Meeting URL / Venue conditions.
 */
test.describe('Conditional Fields', () => {
  let createdId: number;

  test.describe.configure({ mode: 'serial' });

  test.afterAll(async ({ request }) => {
    const { ApiHelper } = await import('./helpers');
    const api = new ApiHelper(request);
    await api.login();
    await api.deleteAll(ENTITY);
  });

  test('create a test event for condition toggling', async ({ api, ui }) => {
    await api.deleteAll(ENTITY);
    await ui.gotoNewEntity(ENTITY);
    await ui.fillTitle(`Cond Test ${TS()}`);
    await ui.fillDate('Start Date', '2026-06-01');
    await ui.fillDate('End Date', '2026-06-03');
    await ui.fillWysiwyg('Event Description', '<p>Condition toggle test</p>');
    await ui.selectOption('Event Type', 'conference');
    await ui.fillTextField('Venue Name', 'Test Venue');
    await ui.fillTextField('Street Address', '123 Event St');
    await ui.fillTextField('City', 'Eventville');
    await ui.fillTextField('State / Province', 'CA');
    await ui.fillTextField('Postal Code', '90210');
    await ui.selectOption('Country', 'US');
    await ui.clickSaveNow();
    await ui.waitForSave();

    const list = await api.peekAll(ENTITY);
    expect(list.length).toBeGreaterThan(0);
    createdId = list[0].id;
  });

  test('should show venue and hide meeting URL for in-person event', async ({ ui }) => {
    await ui.gotoEditEntity(ENTITY, createdId);
    expect(await ui.fieldIsVisible('Venue Details')).toBe(true);
    expect(await ui.fieldIsVisible('Meeting URL')).toBe(false);
  });

  test('should show meeting URL and hide venue when toggled to online', async ({ page, ui }) => {
    await ui.gotoEditEntity(ENTITY, createdId);
    await ui.setCheckbox('Online Event', true);
    await page.waitForTimeout(500);
    expect(await ui.fieldIsVisible('Meeting URL')).toBe(true);
    expect(await ui.fieldIsVisible('Venue Details')).toBe(false);
  });

  test('should toggle back to in-person correctly', async ({ page, ui }) => {
    await ui.gotoEditEntity(ENTITY, createdId);
    // First set to online
    await ui.setCheckbox('Online Event', true);
    await page.waitForTimeout(500);
    expect(await ui.fieldIsVisible('Meeting URL')).toBe(true);

    // Toggle back
    await ui.setCheckbox('Online Event', false);
    await page.waitForTimeout(500);
    expect(await ui.fieldIsVisible('Venue Details')).toBe(true);
    expect(await ui.fieldIsVisible('Meeting URL')).toBe(false);
  });
});
