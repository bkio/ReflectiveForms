import { test, expect } from '@playwright/test';

/**
 * E2E tests for different form field types
 */

test.describe('Form Field Types', () => {
  // Assuming we have a test entity with various field types
  test.beforeEach(async ({ page }) => {
    // Navigate to create a new test entity
    await page.goto('/entities/test/new');
    await expect(page.locator('form')).toBeVisible();
  });

  test('should handle text field input', async ({ page }) => {
    const textField = page.locator('input[type="text"]').first();

    if (await textField.isVisible()) {
      await textField.fill('Sample text');
      await expect(textField).toHaveValue('Sample text');
    }
  });

  test('should handle textarea input', async ({ page }) => {
    const textarea = page.locator('textarea').first();

    if (await textarea.isVisible()) {
      await textarea.fill('Line 1\nLine 2\nLine 3');
      await expect(textarea).toHaveValue('Line 1\nLine 2\nLine 3');
    }
  });

  test('should handle select field', async ({ page }) => {
    const select = page.locator('select').first();

    if (await select.isVisible()) {
      // Get available options
      const options = await select.locator('option').allTextContents();

      if (options.length > 1) {
        // Select the second option
        await select.selectOption({ index: 1 });

        // Verify selection
        const selectedValue = await select.inputValue();
        expect(selectedValue).toBeTruthy();
      }
    }
  });

  test('should handle checkbox field', async ({ page }) => {
    const checkbox = page.locator('input[type="checkbox"]').first();

    if (await checkbox.isVisible()) {
      // Initially should be unchecked
      await expect(checkbox).not.toBeChecked();

      // Check it
      await checkbox.check();
      await expect(checkbox).toBeChecked();

      // Uncheck it
      await checkbox.uncheck();
      await expect(checkbox).not.toBeChecked();
    }
  });

  test('should handle number field', async ({ page }) => {
    const numberField = page.locator('input[type="number"]').first();

    if (await numberField.isVisible()) {
      await numberField.fill('42');
      await expect(numberField).toHaveValue('42');
    }
  });

  test('should handle date picker', async ({ page }) => {
    const datePicker = page.locator('input[type="date"]').first();

    if (await datePicker.isVisible()) {
      await datePicker.fill('2024-12-25');
      await expect(datePicker).toHaveValue('2024-12-25');
    }
  });

  test('should handle range field', async ({ page }) => {
    const rangeField = page.locator('input[type="range"]').first();

    if (await rangeField.isVisible()) {
      // Set value via JavaScript since range inputs can be tricky
      await rangeField.fill('50');

      const value = await rangeField.inputValue();
      expect(Number(value)).toBeGreaterThan(0);
    }
  });
});
