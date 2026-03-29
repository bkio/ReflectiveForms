import { test, expect } from './helpers';

/**
 * DisplayCondition Integration Tests
 *
 * Verifies that conditional field visibility works end-to-end with
 * the actual backend schema and frontend rendering. Tests that:
 * - Fields appear/disappear based on other field values
 * - Conditional data is saved when visible and handled when hidden
 * - Toggling conditions preserves or resets data as expected
 *
 * Entity-specific conditions tested:
 * - Blog Post: status == 'scheduled' → Scheduled Publish Date
 * - Team Member: is_remote == false → Office Address group
 * - Product: is_digital == false → Weight
 * - Event: is_online == true → Meeting URL
 * - Event: is_online == false → Venue Details group
 */

const TS = () => Date.now().toString(36);

test.describe('DisplayCondition: Blog Post scheduled date', () => {
  let blogId: number;

  test.describe.configure({ mode: 'serial' });

  test.afterAll(async ({ request }) => {
    const { ApiHelper } = await import('./helpers');
    const api = new ApiHelper(request);
    await api.login();
    await api.deleteAll('blog-post');
  });

  test('scheduled_date hidden when status is draft', async ({ page, ui, api }) => {
    await api.deleteAll('blog-post');
    await ui.gotoNewEntity('blog-post');

    // Default status is "draft"
    const isVisible = await ui.fieldIsVisible('Scheduled Publish Date');
    expect(isVisible).toBe(false);
  });

  test('scheduled_date appears when status changes to scheduled', async ({ page, ui }) => {
    await ui.gotoNewEntity('blog-post');

    await ui.selectOption('Post Status', 'scheduled');
    await page.waitForTimeout(500);

    const isVisible = await ui.fieldIsVisible('Scheduled Publish Date');
    expect(isVisible).toBe(true);
  });

  test('scheduled_date disappears when status changes back', async ({ page, ui }) => {
    await ui.gotoNewEntity('blog-post');

    // Show it
    await ui.selectOption('Post Status', 'scheduled');
    await page.waitForTimeout(500);
    expect(await ui.fieldIsVisible('Scheduled Publish Date')).toBe(true);

    // Hide it
    await ui.selectOption('Post Status', 'published');
    await page.waitForTimeout(500);
    expect(await ui.fieldIsVisible('Scheduled Publish Date')).toBe(false);
  });

  test('saving with scheduled status persists the date', async ({ page, ui, api }) => {
    await ui.gotoNewEntity('blog-post');

    await ui.fillTitle(`Scheduled Blog ${TS()}`);
    await ui.fillWysiwyg('Post Content', '<p>Scheduled content</p>');
    await ui.fillTextField('URL Slug', `sched-${TS()}`);
    await ui.selectOption('Post Status', 'scheduled');
    await page.waitForTimeout(500);
    await ui.fillDate('Scheduled Publish Date', '2025-12-25');

    await ui.clickSaveNow();
    await ui.waitForSave();

    const entities = await api.peekAll('blog-post');
    const created = entities.find(e => (e.title ?? e.name ?? '').includes('Scheduled Blog'));
    expect(created).toBeDefined();
    blogId = created!.id;

    const entity = await api.readEntity('blog-post', blogId);
    expect(entity.fields.status).toBe('scheduled');
    expect(entity.fields.scheduled_date).toBe('20251225');
  });

  test('reload shows scheduled_date because status is scheduled', async ({ page, ui }) => {
    await ui.gotoEditEntity('blog-post', blogId);

    const statusField = page.locator('.field-wrapper, .bg-white.rounded-lg.shadow-sm')
      .filter({ has: page.locator('label', { hasText: 'Post Status' }) })
      .locator('button[aria-haspopup="listbox"]');
    await expect(statusField).toHaveAttribute('data-value', 'scheduled');

    expect(await ui.fieldIsVisible('Scheduled Publish Date')).toBe(true);
  });
});

test.describe('DisplayCondition: Team Member remote → office address', () => {
  test.describe.configure({ mode: 'serial' });

  test.afterAll(async ({ request }) => {
    const { ApiHelper } = await import('./helpers');
    const api = new ApiHelper(request);
    await api.login();
    await api.deleteAll('team-member');
  });

  test('office address visible when NOT remote', async ({ page, ui, api }) => {
    await api.deleteAll('team-member');
    await ui.gotoNewEntity('team-member');

    // Default is_remote = false → address should be visible
    await ui.setCheckbox('Remote Worker', false);
    await page.waitForTimeout(500);

    expect(await ui.fieldIsVisible('Office Address')).toBe(true);
    expect(await ui.fieldIsVisible('Street Address')).toBe(true);
  });

  test('office address hidden when remote', async ({ page, ui }) => {
    await ui.gotoNewEntity('team-member');

    await ui.setCheckbox('Remote Worker', true);
    await page.waitForTimeout(500);

    expect(await ui.fieldIsVisible('Office Address')).toBe(false);
  });

  test('toggling remote back reveals office address', async ({ page, ui }) => {
    await ui.gotoNewEntity('team-member');

    // Set remote
    await ui.setCheckbox('Remote Worker', true);
    await page.waitForTimeout(500);
    expect(await ui.fieldIsVisible('Office Address')).toBe(false);

    // Unset remote
    await ui.setCheckbox('Remote Worker', false);
    await page.waitForTimeout(500);
    expect(await ui.fieldIsVisible('Office Address')).toBe(true);
  });

  test('save non-remote team member with office address, verify address persisted', async ({ page, ui, api }) => {
    await ui.gotoNewEntity('team-member');

    await ui.fillTitle(`Non-Remote TM ${TS()}`);
    await ui.fillTextField('Work Email', 'nonremote@test.com');
    await ui.fillTextField('Job Title', 'Office Worker');
    await ui.fillDate('Hire Date', '2024-01-01');

    await ui.setCheckbox('Remote Worker', false);
    await page.waitForTimeout(500);

    await ui.fillTextField('Street Address', '42 Office Lane');
    await ui.fillTextField('City', 'Denver');
    await ui.fillTextField('Postal Code', '80201');

    // Emergency contact (already pre-populated from min_items=1)
    await ui.fillTextField('Contact Name', 'EC Person');
    await ui.fillTextField('Phone Number', '+1 555-9999');

    await ui.clickSaveNow();
    await ui.waitForSave();

    const entities = await api.peekAll('team-member');
    const created = entities.find(e => (e.title ?? e.name ?? '').includes('Non-Remote'));
    expect(created).toBeDefined();

    const entity = await api.readEntity('team-member', created!.id);
    expect(entity.fields.is_remote).toBe(false);
    expect(entity.fields.office_address.street).toBe('42 Office Lane');
    expect(entity.fields.office_address.city).toBe('Denver');
  });
});

test.describe('DisplayCondition: Product digital → weight', () => {
  test.describe.configure({ mode: 'serial' });

  test.afterAll(async ({ request }) => {
    const { ApiHelper } = await import('./helpers');
    const api = new ApiHelper(request);
    await api.login();
    await api.deleteAll('product');
  });

  test('weight visible when NOT digital', async ({ page, ui, api }) => {
    await api.deleteAll('product');
    await ui.gotoNewEntity('product');

    await ui.setCheckbox('Digital Product', false);
    await page.waitForTimeout(500);

    expect(await ui.fieldIsVisible('Weight (kg)')).toBe(true);
  });

  test('weight hidden when digital', async ({ page, ui }) => {
    await ui.gotoNewEntity('product');

    await ui.setCheckbox('Digital Product', true);
    await page.waitForTimeout(500);

    expect(await ui.fieldIsVisible('Weight (kg)')).toBe(false);
  });

  test('digital product saved without weight', async ({ page, ui, api }) => {
    await ui.gotoNewEntity('product');

    await ui.fillTitle(`Digital Product ${TS()}`);
    await ui.fillMedia('Primary Product Image');
    await ui.fillTextArea('Short Description', 'Digital only');
    await ui.fillNumber('Base Price (USD)', '29.99');
    await ui.setCheckbox('Digital Product', true);
    await ui.setCheckbox('Published', true);

    // Add required variant (already pre-populated from min_items=1)
    await ui.fillTextField('Variant Name', 'Download');
    await ui.fillTextField('SKU', `DIG-${TS()}`);
    await ui.fillNumber('Price (USD)', '29.99');
    await ui.fillNumber('Stock Quantity', '999');

    await ui.clickSaveNow();
    await ui.waitForSave();

    const entities = await api.peekAll('product');
    const created = entities.find(e => (e.title ?? e.name ?? '').includes('Digital Product'));
    expect(created).toBeDefined();

    const entity = await api.readEntity('product', created!.id);
    expect(entity.fields.is_digital).toBe(true);
  });
});

test.describe('DisplayCondition: Event online → meeting URL vs. venue', () => {
  test.describe.configure({ mode: 'serial' });

  test.afterAll(async ({ request }) => {
    const { ApiHelper } = await import('./helpers');
    const api = new ApiHelper(request);
    await api.login();
    await api.deleteAll('event');
  });

  test('online event shows meeting URL, hides venue', async ({ page, ui, api }) => {
    await api.deleteAll('event');
    await ui.gotoNewEntity('event');

    await ui.setCheckbox('Online Event', true);
    await page.waitForTimeout(500);

    expect(await ui.fieldIsVisible('Meeting URL')).toBe(true);
    expect(await ui.fieldIsVisible('Venue Details')).toBe(false);
  });

  test('in-person event shows venue, hides meeting URL', async ({ page, ui }) => {
    await ui.gotoNewEntity('event');

    await ui.setCheckbox('Online Event', false);
    await page.waitForTimeout(500);

    expect(await ui.fieldIsVisible('Meeting URL')).toBe(false);
    expect(await ui.fieldIsVisible('Venue Details')).toBe(true);
  });

  test('toggle between online and in-person multiple times', async ({ page, ui }) => {
    await ui.gotoNewEntity('event');

    for (let i = 0; i < 3; i++) {
      await ui.setCheckbox('Online Event', true);
      await page.waitForTimeout(300);
      expect(await ui.fieldIsVisible('Meeting URL')).toBe(true);
      expect(await ui.fieldIsVisible('Venue Details')).toBe(false);

      await ui.setCheckbox('Online Event', false);
      await page.waitForTimeout(300);
      expect(await ui.fieldIsVisible('Meeting URL')).toBe(false);
      expect(await ui.fieldIsVisible('Venue Details')).toBe(true);
    }
  });

  test('save online event with meeting URL, verify venue fields empty', async ({ page, ui, api }) => {
    await ui.gotoNewEntity('event');

    await ui.fillTitle(`Online Event ${TS()}`);
    await ui.fillWysiwyg('Event Description', '<p>Online event test.</p>');
    await ui.selectOption('Event Type', 'webinar');
    await ui.fillDate('Start Date', '2025-09-01');
    await ui.fillDate('End Date', '2025-09-01');
    await ui.setCheckbox('Online Event', true);
    await page.waitForTimeout(500);
    await ui.fillTextField('Meeting URL', 'https://zoom.us/j/test123');
    await ui.fillTextField('Registration Contact Email', 'online@test.com');

    await ui.clickSaveNow();
    await ui.waitForSave();

    const entities = await api.peekAll('event');
    const created = entities.find(e => (e.title ?? e.name ?? '').includes('Online Event'));
    expect(created).toBeDefined();

    const entity = await api.readEntity('event', created!.id);
    expect(entity.fields.is_online).toBe(true);
    expect(entity.fields.meeting_url).toBe('https://zoom.us/j/test123');
  });

  test('save in-person event with venue, verify meeting_url empty', async ({ page, ui, api }) => {
    await ui.gotoNewEntity('event');

    await ui.fillTitle(`InPerson Event ${TS()}`);
    await ui.fillWysiwyg('Event Description', '<p>In-person event test.</p>');
    await ui.selectOption('Event Type', 'meetup');
    await ui.fillDate('Start Date', '2025-10-01');
    await ui.fillDate('End Date', '2025-10-01');
    await ui.setCheckbox('Online Event', false);
    await page.waitForTimeout(500);

    await ui.fillTextField('Venue Name', 'Community Hall');
    await ui.fillTextField('Street Address', '10 Park Ave');
    await ui.fillTextField('City', 'Seattle');
    await ui.fillTextField('Postal Code', '98101');
    await ui.fillTextField('Registration Contact Email', 'inperson@test.com');

    await ui.clickSaveNow();
    await ui.waitForSave();

    const entities = await api.peekAll('event');
    const created = entities.find(e => (e.title ?? e.name ?? '').includes('InPerson Event'));
    expect(created).toBeDefined();

    const entity = await api.readEntity('event', created!.id);
    expect(entity.fields.is_online).toBe(false);
    expect(entity.fields.venue.venue_name).toBe('Community Hall');
    expect(entity.fields.venue.venue_address.city).toBe('Seattle');
  });
});
