import { test, expect } from '@playwright/test';

/**
 * E2E tests for Conditional Field visibility
 */

test.describe('Conditional Fields', () => {
  test.beforeEach(async ({ page }) => {
    // Navigate to an entity with conditional fields
    await page.goto('/entities/test/new');
    await expect(page.locator('form')).toBeVisible();
  });

  test('should show conditional field when condition is met', async ({ page }) => {
    // Find a checkbox that controls visibility
    const triggerCheckbox = page.locator('[data-testid="condition-trigger"]');

    if (await triggerCheckbox.isVisible()) {
      // Find the dependent field (should be hidden initially)
      const dependentField = page.locator('[data-testid="conditional-field"]');

      // Should be hidden initially
      await expect(dependentField).not.toBeVisible();

      // Check the trigger
      await triggerCheckbox.check();

      // Dependent field should now be visible
      await expect(dependentField).toBeVisible();
    }
  });

  test('should hide conditional field when condition is not met', async ({ page }) => {
    // Find a checkbox that controls visibility
    const triggerCheckbox = page.locator('[data-testid="condition-trigger"]');

    if (await triggerCheckbox.isVisible()) {
      // Check it first
      await triggerCheckbox.check();

      const dependentField = page.locator('[data-testid="conditional-field"]');
      await expect(dependentField).toBeVisible();

      // Uncheck it
      await triggerCheckbox.uncheck();

      // Dependent field should be hidden
      await expect(dependentField).not.toBeVisible();
    }
  });

  test('should handle select-based conditions', async ({ page }) => {
    // Find a select that controls visibility
    const triggerSelect = page.locator('select[data-testid="condition-trigger-select"]');

    if (await triggerSelect.isVisible()) {
      const dependentField = page.locator('[data-testid="select-conditional-field"]');

      // Select a value that shows the field
      const options = await triggerSelect.locator('option').allTextContents();

      if (options.length > 1) {
        // Select option that should trigger field visibility
        await triggerSelect.selectOption({ index: 1 });

        // Check if dependent field visibility changed
        // (depends on your actual condition configuration)
      }
    }
  });

  test('should preserve conditional field data when toggled', async ({ page }) => {
    const triggerCheckbox = page.locator('[data-testid="condition-trigger"]');

    if (await triggerCheckbox.isVisible()) {
      // Show the field
      await triggerCheckbox.check();

      const dependentField = page.locator('[data-testid="conditional-field"] input');

      if (await dependentField.isVisible()) {
        // Enter some data
        await dependentField.fill('Test data');

        // Hide the field
        await triggerCheckbox.uncheck();

        // Show it again
        await triggerCheckbox.check();

        // Data should be preserved
        await expect(dependentField).toHaveValue('Test data');
      }
    }
  });

  test('should handle nested conditional fields', async ({ page }) => {
    // Find nested conditional structure
    const level1Trigger = page.locator('[data-testid="level1-trigger"]');

    if (await level1Trigger.isVisible()) {
      // Show level 1
      await level1Trigger.check();

      const level2Trigger = page.locator('[data-testid="level2-trigger"]');

      if (await level2Trigger.isVisible()) {
        // Show level 2
        await level2Trigger.check();

        const level2Field = page.locator('[data-testid="level2-field"]');
        await expect(level2Field).toBeVisible();

        // Hide level 1 (should also hide level 2)
        await level1Trigger.uncheck();
        await expect(level2Field).not.toBeVisible();
      }
    }
  });
});
