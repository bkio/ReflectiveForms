import { test, expect, Page } from '@playwright/test';

/**
 * E2E tests for Entity Locking functionality
 */

test.describe('Entity Locking', () => {
  test('should acquire lock when editing entity', async ({ page }) => {
    // Navigate to an existing entity
    await page.goto('/entities/test/1');

    // Wait for the form to load
    await expect(page.locator('form')).toBeVisible();

    // Should NOT show lock warning (we acquired the lock)
    await expect(page.locator('text=This entity is locked')).not.toBeVisible();
  });

  test('should show lock warning when entity is locked by another user', async ({ browser }) => {
    // Open first browser context and navigate to entity
    const context1 = await browser.newContext();
    const page1 = await context1.newPage();
    await page1.goto('/entities/test/1');
    await expect(page1.locator('form')).toBeVisible();

    // Open second browser context and navigate to same entity
    const context2 = await browser.newContext();
    const page2 = await context2.newPage();
    await page2.goto('/entities/test/1');

    // Second user should see lock warning
    await expect(page2.locator('text=This entity is locked')).toBeVisible({ timeout: 10000 });

    // Clean up
    await context1.close();
    await context2.close();
  });

  test('should release lock when leaving page', async ({ browser }) => {
    // Open first context
    const context1 = await browser.newContext();
    const page1 = await context1.newPage();
    await page1.goto('/entities/test/1');
    await expect(page1.locator('form')).toBeVisible();

    // Close first page (should release lock)
    await page1.close();
    await context1.close();

    // Wait a moment for lock to be released
    await new Promise(resolve => setTimeout(resolve, 1000));

    // Open second context - should be able to edit
    const context2 = await browser.newContext();
    const page2 = await context2.newPage();
    await page2.goto('/entities/test/1');

    // Should NOT show lock warning
    await expect(page2.locator('text=This entity is locked')).not.toBeVisible({ timeout: 5000 });

    await context2.close();
  });

  test('should disable form fields when locked', async ({ browser }) => {
    // Open first browser context and lock the entity
    const context1 = await browser.newContext();
    const page1 = await context1.newPage();
    await page1.goto('/entities/test/1');
    await expect(page1.locator('form')).toBeVisible();

    // Open second browser context
    const context2 = await browser.newContext();
    const page2 = await context2.newPage();
    await page2.goto('/entities/test/1');

    // Wait for lock warning
    await expect(page2.locator('text=This entity is locked')).toBeVisible({ timeout: 10000 });

    // Form should be disabled or have disabled styling
    const form = page2.locator('form');
    const fieldset = form.locator('fieldset[disabled]');

    // Either fieldset is disabled or inputs are disabled
    const isFieldsetDisabled = await fieldset.count() > 0;
    const titleInput = page2.locator('[name="title.rendered"]');
    const isInputDisabled = await titleInput.isDisabled();

    expect(isFieldsetDisabled || isInputDisabled).toBeTruthy();

    // Clean up
    await context1.close();
    await context2.close();
  });
});
