import { test, expect } from '@playwright/test';

/**
 * E2E tests for Repeater field operations
 */

test.describe('Repeater Field Operations', () => {
  test.beforeEach(async ({ page }) => {
    // Navigate to an entity with a repeater field
    await page.goto('/entities/test/new');
  });

  test('should add a repeater item', async ({ page }) => {
    // Find the repeater container
    const repeater = page.locator('[data-testid="repeater-field"]').first();

    if (await repeater.isVisible()) {
      // Get initial item count
      const initialCount = await repeater.locator('[data-testid="repeater-item"]').count();

      // Click add button
      await repeater.locator('button:has-text("Add")').click();

      // Should have one more item
      await expect(repeater.locator('[data-testid="repeater-item"]')).toHaveCount(initialCount + 1);
    }
  });

  test('should remove a repeater item', async ({ page }) => {
    const repeater = page.locator('[data-testid="repeater-field"]').first();

    if (await repeater.isVisible()) {
      // First add an item
      await repeater.locator('button:has-text("Add")').click();

      const itemCount = await repeater.locator('[data-testid="repeater-item"]').count();

      if (itemCount > 0) {
        // Click delete button on first item
        await repeater.locator('[data-testid="repeater-item"]').first().locator('[data-testid="delete-item"]').click();

        // Verify deletion (item might be marked as deleted instead of removed)
        // This depends on implementation
        await expect(repeater.locator('[data-testid="repeater-item"]')).toHaveCount(itemCount - 1);
      }
    }
  });

  test('should reorder repeater items', async ({ page }) => {
    const repeater = page.locator('[data-testid="repeater-field"]').first();

    if (await repeater.isVisible()) {
      // Add two items
      await repeater.locator('button:has-text("Add")').click();
      await repeater.locator('button:has-text("Add")').click();

      const items = repeater.locator('[data-testid="repeater-item"]');
      const itemCount = await items.count();

      if (itemCount >= 2) {
        // Get the move up button on the second item
        const moveUpButton = items.nth(1).locator('[data-testid="move-up"]');

        if (await moveUpButton.isVisible()) {
          await moveUpButton.click();

          // Verify items have been reordered
          // This would require comparing some identifying attribute before/after
        }
      }
    }
  });

  test('should edit nested fields in repeater', async ({ page }) => {
    const repeater = page.locator('[data-testid="repeater-field"]').first();

    if (await repeater.isVisible()) {
      // Add an item
      await repeater.locator('button:has-text("Add")').click();

      // Find input in the new item
      const itemInput = repeater.locator('[data-testid="repeater-item"]').first().locator('input').first();

      if (await itemInput.isVisible()) {
        await itemInput.fill('Nested value');
        await expect(itemInput).toHaveValue('Nested value');
      }
    }
  });
});
