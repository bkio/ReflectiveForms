import { test, expect } from './helpers';

/**
 * E2E tests for DynamicDefaultValueAsync — verifies that the backend populates
 * dynamic default values in the schema and the frontend uses them correctly.
 */
test.describe('DynamicDefaultValueAsync', () => {
  test.describe('Schema API returns dynamic defaults', () => {
    test('event start_date has today as default value', async ({ request }) => {
      const res = await request.get('http://localhost:9000/rf/api/schema?type=event');
      const schema = await res.json();

      const startDate = schema.fields.find((f: { name: string }) => f.name === 'start_date');
      expect(startDate).toBeTruthy();
      expect(startDate.default_value).toBeTruthy();

      // Should be today in yyyyMMdd format
      const today = new Date();
      const expected = `${today.getFullYear()}${String(today.getMonth() + 1).padStart(2, '0')}${String(today.getDate()).padStart(2, '0')}`;
      expect(startDate.default_value).toBe(expected);
    });

    test('event end_date has tomorrow as default value', async ({ request }) => {
      const res = await request.get('http://localhost:9000/rf/api/schema?type=event');
      const schema = await res.json();

      const endDate = schema.fields.find((f: { name: string }) => f.name === 'end_date');
      expect(endDate).toBeTruthy();
      expect(endDate.default_value).toBeTruthy();

      // Should be tomorrow in yyyyMMdd format
      const tomorrow = new Date();
      tomorrow.setDate(tomorrow.getDate() + 1);
      const expected = `${tomorrow.getFullYear()}${String(tomorrow.getMonth() + 1).padStart(2, '0')}${String(tomorrow.getDate()).padStart(2, '0')}`;
      expect(endDate.default_value).toBe(expected);
    });

    test('objective work start date has today as default value', async ({ request }) => {
      const res = await request.get('http://localhost:9000/rf/api/schema?type=objective');
      const schema = await res.json();

      const workStartDate = schema.fields.find((f: { name: string }) => f.name === 'objective_work_start_date');
      expect(workStartDate).toBeTruthy();
      expect(workStartDate.default_value).toBeTruthy();

      const today = new Date();
      const expected = `${today.getFullYear()}${String(today.getMonth() + 1).padStart(2, '0')}${String(today.getDate()).padStart(2, '0')}`;
      expect(workStartDate.default_value).toBe(expected);
    });

    test('fields without DynamicDefaultValue retain static defaults', async ({ request }) => {
      const res = await request.get('http://localhost:9000/rf/api/schema?type=event');
      const schema = await res.json();

      // event_type has static default "conference"
      const eventType = schema.fields.find((f: { name: string }) => f.name === 'event_type');
      expect(eventType).toBeTruthy();
      expect(eventType.default_value).toBe('conference');

      // is_online has static default false
      const isOnline = schema.fields.find((f: { name: string }) => f.name === 'is_online');
      expect(isOnline).toBeTruthy();
      expect(isOnline.default_value).toBe(false);
    });
  });

  test.describe('Frontend form uses dynamic defaults', () => {
    test('new event form pre-fills start date with today', async ({ ui, page }) => {
      await ui.gotoNewEntity('event');

      // The start date should be pre-filled with today's date
      const wrapper = page.locator('.field-wrapper')
        .filter({ has: page.locator('label', { hasText: 'Start Date' }) });
      const input = wrapper.locator('input[type="date"]');

      const val = await input.inputValue();
      // Dynamic default is yyyyMMdd, but the input converts to yyyy-MM-dd
      const today = new Date();
      const expectedInput = `${today.getFullYear()}-${String(today.getMonth() + 1).padStart(2, '0')}-${String(today.getDate()).padStart(2, '0')}`;
      expect(val).toBe(expectedInput);
    });

    test('new event form pre-fills end date with tomorrow', async ({ ui, page }) => {
      await ui.gotoNewEntity('event');

      const wrapper = page.locator('.field-wrapper')
        .filter({ has: page.locator('label', { hasText: 'End Date' }) });
      const input = wrapper.locator('input[type="date"]');

      const val = await input.inputValue();
      const tomorrow = new Date();
      tomorrow.setDate(tomorrow.getDate() + 1);
      const expectedInput = `${tomorrow.getFullYear()}-${String(tomorrow.getMonth() + 1).padStart(2, '0')}-${String(tomorrow.getDate()).padStart(2, '0')}`;
      expect(val).toBe(expectedInput);
    });

    test('new objective form pre-fills work start date with today', async ({ ui, page }) => {
      await ui.gotoNewEntity('objective');

      const wrapper = page.locator('.field-wrapper')
        .filter({ has: page.locator('label', { hasText: 'Objective Work Planned Start Date' }) });
      const input = wrapper.locator('input[type="date"]');

      const val = await input.inputValue();
      const today = new Date();
      const expectedInput = `${today.getFullYear()}-${String(today.getMonth() + 1).padStart(2, '0')}-${String(today.getDate()).padStart(2, '0')}`;
      expect(val).toBe(expectedInput);
    });

    test('static defaults still work alongside dynamic defaults', async ({ ui, page }) => {
      await ui.gotoNewEntity('event');

      // Event type should default to "conference"
      const typeWrapper = page.locator('.field-wrapper')
        .filter({ has: page.locator('label', { hasText: 'Event Type' }) });
      const trigger = typeWrapper.locator('button[aria-haspopup="listbox"]');
      const selectedValue = await trigger.getAttribute('data-value');
      expect(selectedValue).toBe('conference');
    });
  });
});
