import { test, expect } from './helpers';

/**
 * Authentication & Dashboard E2E Tests
 *
 * Verifies login/session mechanics, dashboard rendering, navigation,
 * and schema availability for every registered entity type.
 */

test.describe('Authentication & Dashboard', () => {
  test('backend schema endpoint returns all 6 custom entity types plus reserved', async ({ api }) => {
    const schemas = await api.getAllSchemas();
    const names = Object.keys(schemas);

    // Custom entities
    expect(names).toContain('objective');
    expect(names).toContain('blog-post');
    expect(names).toContain('team-member');
    expect(names).toContain('product');
    expect(names).toContain('event');
    expect(names).toContain('survey');

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

  test('hidden entity (ShowInNavigation=false) is NOT visible in dashboard', async ({ page, ui }) => {
    await ui.gotoDashboard();

    // Survey has ShowInNavigation=false — should not appear on dashboard
    await expect(page.locator('h3', { hasText: 'Surveys' })).not.toBeVisible();
  });

  test('hidden entity (ShowInNavigation=false) is NOT visible in sidebar navigation', async ({ page, ui }) => {
    await ui.gotoDashboard();

    const nav = page.locator('nav');
    // Survey should not appear in the sidebar
    await expect(nav.locator('a', { hasText: 'Surveys' })).not.toBeVisible();
  });

  test('hidden entity list page is still accessible via direct URL', async ({ page, ui }) => {
    await ui.gotoEntityList('survey');

    // The entity list page should still render
    await expect(page.locator('h1')).toContainText('Surveys');
  });

  test('hidden entity new form is still accessible via direct URL', async ({ page, ui }) => {
    await ui.gotoNewEntity('survey');

    // The create form should still render
    await expect(page.locator('h1')).toContainText('New Survey');
  });

  test('hidden entity schema returns show_in_navigation=false', async ({ api }) => {
    const schemas = await api.getAllSchemas();
    const survey = schemas['survey'] as any;

    expect(survey).toBeDefined();
    expect(survey.features.show_in_navigation).toBe(false);
    // The entity should still be fully functional otherwise
    expect(survey.features.supports_frontend_edit).toBe(true);
    expect(survey.fields).toBeDefined();
    expect(survey.fields.length).toBeGreaterThan(0);
  });

  test('visible entity schema returns show_in_navigation=true', async ({ api }) => {
    const schemas = await api.getAllSchemas();
    const blogPost = schemas['blog-post'] as any;

    expect(blogPost).toBeDefined();
    expect(blogPost.features.show_in_navigation).toBe(true);
  });
});
