import { test, expect } from '@playwright/test';

/**
 * E2E tests for Entity CRUD operations
 */

test.describe('Entity CRUD Operations', () => {
  test.beforeEach(async ({ page }) => {
    // Navigate to the dashboard
    await page.goto('/');
  });

  test('should display dashboard with entity types', async ({ page }) => {
    // Wait for the dashboard to load
    await expect(page.locator('h1')).toContainText(/dashboard|entities/i);
  });

  test('should navigate to entity list page', async ({ page }) => {
    // Click on an entity type link (adjust selector based on actual UI)
    await page.click('[data-testid="entity-link"]');

    // Should show entity list
    await expect(page.locator('[data-testid="entity-list"]')).toBeVisible();
  });

  test('should create a new entity', async ({ page }) => {
    // Navigate to entity list
    await page.goto('/entities/test');

    // Click "Add New" button
    await page.click('text=Add New');

    // Fill in the title
    await page.fill('[name="title.rendered"], [placeholder*="title" i]', 'Test Entity');

    // Wait for auto-save notification
    await expect(page.locator('text=Changes will be saved')).toBeVisible({ timeout: 10000 });

    // Wait for successful save
    await expect(page.locator('text=saved')).toBeVisible({ timeout: 15000 });
  });

  test('should edit an existing entity', async ({ page }) => {
    // Navigate to entity list
    await page.goto('/entities/test');

    // Click on first entity in the list
    await page.click('[data-testid="entity-row"]:first-child');

    // Wait for form to load
    await expect(page.locator('[name="title.rendered"]')).toBeVisible();

    // Modify the title
    const titleInput = page.locator('[name="title.rendered"]');
    await titleInput.fill('Updated Entity Title');

    // Wait for auto-save
    await expect(page.locator('text=saved')).toBeVisible({ timeout: 15000 });
  });

  test('should delete an entity', async ({ page }) => {
    // Navigate to entity list
    await page.goto('/entities/test');

    // Get initial count
    const initialCount = await page.locator('[data-testid="entity-row"]').count();

    // Click delete button on first entity
    await page.click('[data-testid="entity-row"]:first-child [data-testid="delete-button"]');

    // Confirm deletion (if there's a confirmation dialog)
    const confirmButton = page.locator('text=Confirm, button:has-text("Yes")');
    if (await confirmButton.isVisible()) {
      await confirmButton.click();
    }

    // Wait for deletion to complete
    await expect(page.locator('[data-testid="entity-row"]')).toHaveCount(initialCount - 1);
  });
});
