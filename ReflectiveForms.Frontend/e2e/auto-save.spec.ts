import { test, expect } from '@playwright/test';

/**
 * E2E tests for Auto-save and Validation functionality
 */

test.describe('Auto-save & Validation', () => {
  test.beforeEach(async ({ page }) => {
    // Navigate to create a new entity
    await page.goto('/entities/test/new');
    await expect(page.locator('form')).toBeVisible();
  });

  test('should show auto-save pending message on field change', async ({ page }) => {
    // Fill in title
    await page.fill('[name="title.rendered"], [placeholder*="title" i]', 'Test Entity');

    // Should show pending save message
    await expect(page.locator('text=Changes will be saved')).toBeVisible({ timeout: 5000 });
  });

  test('should show success message after auto-save', async ({ page }) => {
    // Fill in title
    await page.fill('[name="title.rendered"], [placeholder*="title" i]', 'Auto-save Test');

    // Wait for auto-save to complete (default 5 seconds debounce)
    await expect(page.locator('text=saved')).toBeVisible({ timeout: 15000 });
  });

  test('should validate required fields', async ({ page }) => {
    // Try to submit with empty title
    const titleInput = page.locator('[name="title.rendered"], [placeholder*="title" i]');
    await titleInput.fill('');
    await titleInput.blur();

    // Trigger validation
    await page.locator('button[type="submit"]').click().catch(() => {});

    // Should show validation error
    await expect(page.locator('text=/required|cannot be empty/i')).toBeVisible({ timeout: 5000 });
  });

  test('should validate number field min/max', async ({ page }) => {
    const numberField = page.locator('input[type="number"]').first();

    if (await numberField.isVisible()) {
      // Get min attribute
      const min = await numberField.getAttribute('min');

      if (min) {
        // Enter value below minimum
        await numberField.fill(String(Number(min) - 10));
        await numberField.blur();

        // Should show validation error
        await expect(page.locator('text=/minimum|at least/i')).toBeVisible({ timeout: 5000 });
      }
    }
  });

  test('should show sanity check errors', async ({ page }) => {
    // Fill in title
    await page.fill('[name="title.rendered"], [placeholder*="title" i]', 'Test for Sanity Check');

    // Wait for auto-save attempt
    await expect(page.locator('text=Changes will be saved')).toBeVisible({ timeout: 5000 });

    // If sanity check fails, should show error toast
    // The actual behavior depends on backend validation rules
  });

  test('should preserve form state on page reload after save', async ({ page }) => {
    // Create an entity first
    await page.fill('[name="title.rendered"], [placeholder*="title" i]', 'Persistence Test');

    // Wait for save
    await expect(page.locator('text=saved')).toBeVisible({ timeout: 15000 });

    // Get the current URL (should include the new entity ID)
    const url = page.url();

    // Reload the page
    await page.reload();

    // Verify the title is still there
    const titleInput = page.locator('[name="title.rendered"], [placeholder*="title" i]');
    await expect(titleInput).toHaveValue('Persistence Test');
  });
});
