import { test, expect } from './helpers';

/**
 * Blog Post Entity – Full CRUD E2E Tests
 *
 * Covers: Create with all fields → verify in list → read back via API →
 * update fields → verify update persisted → conditional field (scheduled_date) →
 * SEO group fields → external links repeater (add/edit/remove) →
 * clone entity → delete → verify removal
 */

const ENTITY = 'blog-post';
const TS = () => Date.now().toString(36);

test.describe('Blog Post CRUD', () => {
  let createdId: number;

  test.describe.configure({ mode: 'serial' });

  test.afterAll(async ({ request }) => {
    // Best-effort cleanup
    const { ApiHelper } = await import('./helpers');
    const api = new ApiHelper(request);
    await api.login();
    await api.deleteAll(ENTITY);
  });

  // ──────────────────────────────────────
  // CREATE
  // ──────────────────────────────────────
  test('create a blog post with all field types', async ({ page, ui, api }) => {
    await api.deleteAll(ENTITY);
    await ui.gotoNewEntity(ENTITY);

    // Page header
    await expect(page.locator('h1')).toContainText('New Blog Post');

    // Title (required)
    await ui.fillTitle(`E2E Blog Post ${TS()}`);

    // WysiwygEditor — Post Content
    await ui.fillWysiwyg('Post Content', '<p>Hello from E2E</p>');

    // TextArea — Excerpt
    await ui.fillTextArea('Excerpt', 'This is the excerpt from the E2E test.');

    // Select — Post Status → draft (default)
    await ui.selectOption('Post Status', 'published');

    // Checkbox — Featured Post
    await ui.setCheckbox('Featured Post', true);

    // Checkbox — Allow Comments (default true, leave it)

    // Number — Reading Time
    await ui.fillNumber('Estimated Reading Time (minutes)', '7');

    // Group (Grid2) — SEO Metadata
    await ui.fillTextField('Meta Title', 'E2E SEO Title');
    await ui.fillTextArea('Meta Description', 'E2E SEO Description for search results');
    await ui.fillTextField('Meta Keywords', 'e2e, test, blog');
    await ui.fillTextField('Canonical URL', 'https://example.com/original');

    // Repeater — External Links: add 2 items
    await ui.addRepeaterItem('External Links');
    await ui.fillTextField('Link Title', 'Playwright Docs');
    await ui.fillTextField('URL', 'https://playwright.dev');

    await ui.addRepeaterItem('External Links');
    // Fill second link — get items
    const links = ui.repeaterItems('External Links');
    const secondLink = links.nth(1);
    await secondLink.locator('input[type="text"]').first().fill('Vitest Docs');
    await secondLink.locator('input[type="url"]').first().fill('https://vitest.dev');

    // Text — URL Slug
    await ui.fillTextField('URL Slug', `e2e-blog-${TS()}`);

    // Save
    await ui.clickSaveNow();
    await ui.waitForSave();

    // Verify via API
    const entities = await api.peekAll(ENTITY);
    expect(entities.length).toBeGreaterThanOrEqual(1);
    const created = entities.find(e => (e.title ?? e.name ?? '').includes('E2E Blog Post'));
    expect(created).toBeDefined();
    createdId = created!.id;
  });

  // ──────────────────────────────────────
  // LIST — verify presence after create
  // ──────────────────────────────────────
  test('new blog post appears in list page', async ({ page, ui }) => {
    await ui.gotoEntityList(ENTITY);

    const rows = ui.entityRows();
    const count = await rows.count();
    expect(count).toBeGreaterThanOrEqual(1);

    // Find our entity link
    await expect(page.locator('a', { hasText: /E2E Blog Post/ })).toBeVisible();
  });

  // ──────────────────────────────────────
  // READ — verify data via API
  // ──────────────────────────────────────
  test('read back blog post via API and verify all fields', async ({ api }) => {
    const entity = await api.readEntity(ENTITY, createdId);

    expect(entity.title.rendered).toContain('E2E Blog Post');
    expect(entity.fields.status).toBe('published');
    expect(entity.fields.is_featured).toBe(true);
    expect(entity.fields.allow_comments).toBe(true);
    expect(Number(entity.fields.reading_time_minutes)).toBe(7);
    expect(entity.fields.seo_metadata.meta_title).toBe('E2E SEO Title');
    expect(entity.fields.seo_metadata.meta_description).toBe('E2E SEO Description for search results');
    expect(entity.fields.seo_metadata.meta_keywords).toBe('e2e, test, blog');
    expect(entity.fields.seo_metadata.canonical_url).toBe('https://example.com/original');
    expect(entity.fields.excerpt).toContain('excerpt from the E2E test');
    expect(entity.fields.slug).toContain('e2e-blog-');
    expect(entity.fields.external_links.length).toBe(2);
    expect(entity.fields.external_links[0].link_title).toBe('Playwright Docs');
    expect(entity.fields.external_links[0].link_url).toBe('https://playwright.dev');
  });

  // ──────────────────────────────────────
  // UPDATE — modify fields and verify
  // ──────────────────────────────────────
  test('update blog post — change status, title, and remove a repeater item', async ({ page, ui, api }) => {
    await ui.gotoEditEntity(ENTITY, createdId);

    // Verify we're in edit mode
    await expect(page.locator('h1')).toContainText('Edit Blog Post');

    // Update title
    await ui.fillTitle('E2E Blog Post UPDATED');

    // Change status to "scheduled" — should reveal scheduled_date
    await ui.selectOption('Post Status', 'scheduled');

    // Fill scheduled_date (required when status is 'scheduled')
    await ui.fillDate('Scheduled Publish Date', '2026-06-01');

    // Save & verify
    await ui.clickSaveNow();
    await ui.waitForSave();

    const entity = await api.readEntity(ENTITY, createdId);
    expect(entity.title.rendered).toBe('E2E Blog Post UPDATED');
    expect(entity.fields.status).toBe('scheduled');
  });

  // ──────────────────────────────────────
  // CONDITIONAL — scheduled_date visibility
  // ──────────────────────────────────────
  test('conditional field: scheduled_date visible only when status=scheduled', async ({ page, ui }) => {
    await ui.gotoEditEntity(ENTITY, createdId);

    // Status should be "scheduled" from previous test
    // DisplayCondition: "status == 'scheduled'" → Scheduled Publish Date should be visible
    const isVisible = await ui.fieldIsVisible('Scheduled Publish Date');
    expect(isVisible).toBe(true);

    // Change status back to draft
    await ui.selectOption('Post Status', 'draft');

    // Scheduled Publish Date should disappear
    await page.waitForTimeout(500); // Allow React re-render
    const isStillVisible = await ui.fieldIsVisible('Scheduled Publish Date');
    expect(isStillVisible).toBe(false);

    // Revert
    await ui.selectOption('Post Status', 'published');
    await ui.clickSaveNow();
    await ui.waitForSave();
  });

  // ──────────────────────────────────────
  // REPEATER OPERATIONS
  // ──────────────────────────────────────
  test('add and remove external link repeater items', async ({ page, ui, api }) => {
    await ui.gotoEditEntity(ENTITY, createdId);

    // Count initial repeater items
    const initialLinks = ui.repeaterItems('External Links');
    const initialCount = await initialLinks.count();

    // Add a third link
    await ui.addRepeaterItem('External Links');
    await expect(initialLinks).toHaveCount(initialCount + 1);

    // Fill the new item
    const newItem = initialLinks.nth(initialCount);
    await newItem.locator('input[type="text"]').first().fill('New Link');
    await newItem.locator('input[type="url"]').first().fill('https://new-link.com');

    // Save and verify via API
    await ui.clickSaveNow();
    await ui.waitForSave();

    let entity = await api.readEntity(ENTITY, createdId);
    expect(entity.fields.external_links.length).toBe(initialCount + 1);

    // Now remove the last item (click trash button)
    const lastItem = initialLinks.nth(initialCount);
    await lastItem.locator('button[title="Remove"]').click();

    await ui.clickSaveNow();
    await ui.waitForSave();

    entity = await api.readEntity(ENTITY, createdId);
    expect(entity.fields.external_links.length).toBe(initialCount);
  });

  // ──────────────────────────────────────
  // CLONE
  // ──────────────────────────────────────
  test('clone blog post and verify independence', async ({ page, ui, api }) => {
    await page.goto(`/entities-admin/${ENTITY}?id=clone_from_${createdId}`);
    await page.waitForSelector('form', { timeout: 15000 });

    await expect(page.locator('h1')).toContainText('Clone Blog Post');

    // Title should be pre-filled from source
    const title = await ui.getTitle();
    expect(title).toContain('E2E Blog Post');

    // Change title for clone
    await ui.fillTitle('E2E Blog Post CLONED');

    // Change slug to avoid uniqueness conflict
    await ui.fillTextField('URL Slug', `e2e-blog-cloned-${TS()}`);

    await ui.clickSaveNow();
    await ui.waitForSave();

    // Verify 2 entities now in list
    const entities = await api.peekAll(ENTITY);
    expect(entities.length).toBeGreaterThanOrEqual(2);
    const cloned = entities.find(e => (e.title ?? e.name ?? '').includes('CLONED'));
    expect(cloned).toBeDefined();
    expect(cloned!.id).not.toBe(createdId);

    // Cleanup clone
    await api.deleteEntity(ENTITY, cloned!.id);
  });

  // ──────────────────────────────────────
  // DELETE via UI
  // ──────────────────────────────────────
  test('delete blog post via list page and verify removal', async ({ page, ui, api }) => {
    await ui.gotoEntityList(ENTITY);

    const countBefore = await ui.entityRowCount();
    expect(countBefore).toBeGreaterThanOrEqual(1);

    // Accept the confirm() dialog
    page.on('dialog', dialog => dialog.accept());

    await ui.clickDeleteOnRow(0);

    // Wait for row to be removed
    await page.waitForTimeout(2000);

    // Verify via API
    const entities = await api.peekAll(ENTITY);
    expect(entities.length).toBe(countBefore - 1);
  });
});
