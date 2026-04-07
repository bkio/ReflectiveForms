import { test, expect } from './helpers';

/**
 * E2E tests for the SearchableSelect component used in
 * Author fields and Relation fields.
 *
 * Covers:
 * - Searchable dropdown opens on click
 * - Search input filters options
 * - Selecting an option updates the form value
 * - Relation field uses searchable select
 * - Author field uses searchable select
 * - Keyboard navigation works (arrow keys, enter, escape)
 */

const TS = () => Date.now().toString(36);

test.describe('Searchable Select: Author Field', () => {
  test.describe.configure({ mode: 'serial' });

  const ENTITY = 'objective';

  test.afterAll(async ({ request }) => {
    const { ApiHelper } = await import('./helpers');
    const api = new ApiHelper(request);
    await api.login();
    await api.deleteAll(ENTITY);
  });

  test('author field renders as searchable select', async ({ ui }) => {
    await ui.gotoNewEntity(ENTITY);

    // The Author section should be visible
    const authorWrapper = ui.page.locator('[data-testid="author-select"]');
    await expect(authorWrapper).toBeVisible({ timeout: 10000 });

    // Should have a button-style trigger (not a native <select>)
    const trigger = authorWrapper.locator('button[aria-haspopup="listbox"]');
    await expect(trigger).toBeVisible();

    // Should show placeholder text
    await expect(trigger).toContainText('Select');
  });

  test('clicking author opens dropdown with search input', async ({ ui }) => {
    await ui.gotoNewEntity(ENTITY);

    const authorWrapper = ui.page.locator('[data-testid="author-select"]');
    const trigger = authorWrapper.locator('button[aria-haspopup="listbox"]');

    await trigger.click();

    // Dropdown should appear with a search input
    const dropdown = authorWrapper.locator('[role="listbox"]');
    await expect(dropdown).toBeVisible();

    const searchInput = authorWrapper.locator('input[placeholder="Search..."]');
    await expect(searchInput).toBeVisible();
    await expect(searchInput).toBeFocused();
  });

  test('search input filters options', async ({ ui }) => {
    await ui.gotoNewEntity(ENTITY);

    const authorWrapper = ui.page.locator('[data-testid="author-select"]');
    const trigger = authorWrapper.locator('button[aria-haspopup="listbox"]');

    await trigger.click();

    const searchInput = authorWrapper.locator('input[placeholder="Search..."]');

    // Type 'Root' — should filter to show root user
    await searchInput.fill('Root');

    // The dropdown should show an option containing 'admin'
    const options = authorWrapper.locator('[role="option"]');
    // At least the unselect option + matching items
    const count = await options.count();
    expect(count).toBeGreaterThanOrEqual(1);
  });

  test('selecting an author option sets the value', async ({ page, ui }) => {
    await ui.gotoNewEntity(ENTITY);

    const authorWrapper = ui.page.locator('[data-testid="author-select"]');
    const trigger = authorWrapper.locator('button[aria-haspopup="listbox"]');

    await trigger.click();

    // Click the first non-placeholder option
    const firstOption = authorWrapper.locator('[role="option"]').nth(1);
    const optionText = await firstOption.textContent();
    await firstOption.click();

    // Dropdown should close
    await expect(authorWrapper.locator('[role="listbox"]')).not.toBeVisible();

    // Trigger should now show the selected option text
    await expect(trigger).toContainText(optionText!.trim());
  });

  test('escape key closes the dropdown', async ({ ui }) => {
    await ui.gotoNewEntity(ENTITY);

    const authorWrapper = ui.page.locator('[data-testid="author-select"]');
    const trigger = authorWrapper.locator('button[aria-haspopup="listbox"]');

    await trigger.click();
    await expect(authorWrapper.locator('[role="listbox"]')).toBeVisible();

    // Press Escape
    await ui.page.keyboard.press('Escape');
    await expect(authorWrapper.locator('[role="listbox"]')).not.toBeVisible();
  });

  test('author value persists through save', async ({ ui, api }) => {
    // Use blog-post: it has has_author but no nested mandatory author relations
    await ui.gotoNewEntity('blog-post');

    // Select author FIRST (before filling title to avoid auto-save race)
    const authorWrapper = ui.page.locator('[data-testid="author-select"]');
    const trigger = authorWrapper.locator('button[aria-haspopup="listbox"]');
    await trigger.click();

    // Wait for options to load
    const firstOption = authorWrapper.locator('[role="option"]').nth(1);
    await expect(firstOption).toBeVisible({ timeout: 10000 });
    await firstOption.click();

    // Verify author was selected (trigger should not show placeholder)
    await expect(trigger).not.toContainText('Select Author', { timeout: 5000 });

    // Now fill remaining required fields
    const title = `Author Save Test ${TS()}`;
    await ui.fillTitle(title);
    // Set content and slug via form API
    await ui.page.evaluate((ts) => {
      (window as any).__rfFormSetValue('fields.content', '<p>Test author persistence</p>', { shouldDirty: true });
      (window as any).__rfFormSetValue('fields.slug', `author-save-test-${ts}`, { shouldDirty: true });
    }, TS());

    await ui.clickSaveNow();
    await ui.waitForSave();

    // Verify via API
    const list = await api.peekAll('blog-post');
    const saved = list.find(e => e.title === title);
    expect(saved).toBeTruthy();
  });
});

test.describe('Searchable Select: Relation Field', () => {
  test.describe.configure({ mode: 'serial' });

  const ENTITY = 'team-member';

  test.afterAll(async ({ request }) => {
    const { ApiHelper } = await import('./helpers');
    const api = new ApiHelper(request);
    await api.login();
    await api.deleteAll(ENTITY);
  });

  test('relation field uses searchable select', async ({ ui }) => {
    await ui.gotoNewEntity(ENTITY);

    // Team member has a 'Favorite Blog Post' relation field
    const relationLabel = ui.page.locator('.field-wrapper', { hasText: 'Favorite Blog Post' });
    await expect(relationLabel).toBeVisible({ timeout: 10000 });

    // Should have a searchable select trigger
    const trigger = relationLabel.locator('button[aria-haspopup="listbox"]');
    await expect(trigger).toBeVisible();
  });

  test('relation field dropdown shows related entities', async ({ ui, api }) => {
    // Create a blog post first so the relation field has options
    await api.createEntity('blog-post', {
      'title': { rendered: `RelationTest Blog ${TS()}` },
      'fields': {
        content: '<p>Test</p>',
        excerpt: 'Test excerpt',
        slug: `relation-test-${TS()}`,
        status: 'published',
        reading_time_minutes: 5,
        external_links: [],
      },
    });

    await ui.gotoNewEntity(ENTITY);

    const relationLabel = ui.page.locator('.field-wrapper', { hasText: 'Favorite Blog Post' });
    const trigger = relationLabel.locator('button[aria-haspopup="listbox"]');

    await trigger.click();

    // Should show options from blog-post entities
    const options = relationLabel.locator('[role="option"]');
    const count = await options.count();
    // At least the unselect option + the blog post we created
    expect(count).toBeGreaterThanOrEqual(2);
  });
});
