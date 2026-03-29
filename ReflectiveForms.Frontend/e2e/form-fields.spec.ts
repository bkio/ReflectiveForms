import { test, expect } from './helpers';

const ENTITY = 'product';
const TS = () => Date.now().toString(36);

/**
 * E2E tests for different form field types.
 * Uses the product entity which covers Text, TextArea, Number, Checkbox,
 * Select, Range, Url, and Repeater fields.
 */
test.describe('Form Field Types', () => {
  test.describe.configure({ mode: 'serial' });

  test.afterAll(async ({ request }) => {
    const { ApiHelper } = await import('./helpers');
    const api = new ApiHelper(request);
    await api.login();
    await api.deleteAll(ENTITY);
  });

  test('should handle text input and title', async ({ page, ui }) => {
    await ui.gotoNewEntity(ENTITY);
    const title = `Field Types ${TS()}`;
    await ui.fillTitle(title);
    await expect(page.locator('input[name="title.rendered"]')).toHaveValue(title);
  });

  test('should handle wysiwyg editor', async ({ ui }) => {
    await ui.gotoNewEntity(ENTITY);
    await ui.fillTitle(`Wysiwyg Test ${TS()}`);
    await ui.fillWysiwyg('Full Description', '<p>Line 1</p><p>Line 2</p>');
    const field = ui.page.locator('label', { hasText: /^\s*Full Description\s*\*?\s*$/ }).first()
      .locator('xpath=ancestor::div[contains(@class,"field-wrapper")][1]');
    // WYSIWYG renders as contenteditable or textarea in source mode
    const editor = field.locator('[contenteditable="true"], textarea');
    await expect(editor.first()).toBeVisible({ timeout: 5000 });
  });

  test('should handle textarea input', async ({ ui }) => {
    await ui.gotoNewEntity(ENTITY);
    await ui.fillTitle(`Textarea Test ${TS()}`);
    await ui.fillTextArea('Short Description', 'Line 1\nLine 2\nLine 3');
    const field = ui.page.locator('label', { hasText: /^\s*Short Description\s*\*?\s*$/ }).first()
      .locator('xpath=ancestor::div[contains(@class,"field-wrapper")][1]');
    const ta = field.locator('textarea');
    await expect(ta).toHaveValue('Line 1\nLine 2\nLine 3');
  });

  test('should handle select field', async ({ ui }) => {
    await ui.gotoNewEntity(ENTITY);
    await ui.fillTitle(`Select Test ${TS()}`);
    await ui.selectOption('Product Category', 'electronics');
    // Verify the select has the chosen value
    const field = ui.page.locator('label', { hasText: /^\s*Product Category\s*\*?\s*$/ }).first()
      .locator('xpath=ancestor::div[contains(@class,"field-wrapper")][1]');
    const sel = field.locator('button[aria-haspopup="listbox"]');
    await expect(sel).toHaveAttribute('data-value', 'electronics');
  });

  test('should handle checkbox field', async ({ ui }) => {
    await ui.gotoNewEntity(ENTITY);
    await ui.fillTitle(`Checkbox Test ${TS()}`);
    await ui.setCheckbox('Digital Product', true);
    const field = ui.page.locator('label', { hasText: /^\s*Digital Product\s*\*?\s*$/ }).first()
      .locator('xpath=ancestor::div[contains(@class,"field-wrapper")][1]');
    const cb = field.locator('input[type="checkbox"]');
    await expect(cb).toBeChecked();
  });

  test('should handle number field', async ({ ui }) => {
    await ui.gotoNewEntity(ENTITY);
    await ui.fillTitle(`Number Test ${TS()}`);
    await ui.fillNumber('Weight (kg)', '3.5');
    const field = ui.page.locator('label', { hasText: /^\s*Weight \(kg\)\s*\*?\s*$/ }).first()
      .locator('xpath=ancestor::div[contains(@class,"field-wrapper")][1]');
    const input = field.locator('input[type="number"]');
    await expect(input).toHaveValue('3.5');
  });
});
