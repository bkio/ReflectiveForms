import { test, expect } from './helpers';

/**
 * Authentication & Dashboard E2E Tests
 *
 * Verifies login/session mechanics, dashboard rendering, navigation,
 * and schema availability for every registered entity type.
 */

test.describe('Authentication & Dashboard', () => {
  test('backend schema endpoint returns all 5 custom entity types plus reserved', async ({ api }) => {
    const schemas = await api.getAllSchemas();
    const names = Object.keys(schemas);

    // Custom entities
    expect(names).toContain('objective');
    expect(names).toContain('blog-post');
    expect(names).toContain('team-member');
    expect(names).toContain('product');
    expect(names).toContain('event');

    // Reserved entities
    expect(names).toContain('users');
    expect(names).toContain('iam-role');
    expect(names).toContain('tags');
    expect(names).toContain('categories');
    expect(names).toContain('media');
  });

  test('dashboard renders and shows all frontend-editable entity types', async ({ page, ui }) => {
    await ui.gotoDashboard();

    // Page title
    await expect(page.locator('h1')).toContainText('Dashboard');

    // Should show cards for each entity type
    for (const name of ['Objectives', 'Blog Posts', 'Team Members', 'Products', 'Events']) {
      await expect(page.locator('h3', { hasText: name })).toBeVisible();
    }
  });

  test('dashboard shows correct feature badges per entity', async ({ page, ui }) => {
    await ui.gotoDashboard();

    // Blog Posts card should have "Has Author", "Has Tags", "Has Categories"
    const blogCard = page.locator('.bg-white.rounded-lg').filter({ hasText: 'Blog Posts' });
    await expect(blogCard.locator('text=Has Author')).toBeVisible();
    await expect(blogCard.locator('text=Has Tags')).toBeVisible();
    await expect(blogCard.locator('text=Has Categories')).toBeVisible();

    // Team Members should NOT have tags badge
    const teamCard = page.locator('.bg-white.rounded-lg').filter({ hasText: 'Team Members' });
    await expect(teamCard.locator('text=Has Tags')).not.toBeVisible();
  });

  test('dashboard "View All" links navigate to correct entity list pages', async ({ page, ui }) => {
    await ui.gotoDashboard();

    const blogCard = page.locator('.bg-white.rounded-lg').filter({ hasText: 'Blog Posts' });
    await blogCard.locator('a', { hasText: 'View All' }).click();

    await expect(page).toHaveURL(/\/entities\/blog-post/);
    await expect(page.locator('h1')).toContainText('Blog Posts');
  });

  test('dashboard "Create New" links navigate to new-entity form', async ({ page, ui }) => {
    await ui.gotoDashboard();

    const productCard = page.locator('.bg-white.rounded-lg').filter({ hasText: 'Products' });
    await productCard.locator('a', { hasText: 'Create New' }).click();

    await expect(page).toHaveURL(/\/entities-admin\/product\?id=new/);
    await expect(page.locator('h1')).toContainText('New Product');
  });

  test('sidebar navigation contains all entity types', async ({ page, ui }) => {
    await ui.gotoDashboard();

    const nav = page.locator('nav');
    for (const name of ['Objectives', 'Blog Posts', 'Team Members', 'Products', 'Events']) {
      await expect(nav.locator('a', { hasText: name })).toBeVisible();
    }
  });

  test('empty list page shows "No entities found" message', async ({ api, page, ui }) => {
    // Clean up first
    await api.deleteAll('blog-post');

    await ui.gotoEntityList('blog-post');

    await expect(page.locator('text=No blog posts found')).toBeVisible();
    await expect(page.locator('a', { hasText: 'Create one?' })).toBeVisible();
  });
});
