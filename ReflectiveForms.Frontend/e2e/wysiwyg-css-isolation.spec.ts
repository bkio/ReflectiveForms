import { test, expect } from './helpers';

/**
 * WYSIWYG CSS Isolation Tests
 *
 * Verifies that CSS injection via WYSIWYG content cannot escape the
 * editor/view container and trash the application layout.
 *
 * The `contain: layout paint` CSS property on the contentEditable and
 * view containers traps position:fixed, z-index, viewport units, etc.
 * inside the box — without modifying the user's stored HTML.
 *
 * Uses the `blog-post` entity (Sample1) which has a WYSIWYG "Post Content" field.
 */

const ENTITY = 'blog-post';
const TS = () => Date.now().toString(36);

// ── Attack payloads ──────────────────────────────────────────────
const MALICIOUS_FULLSCREEN = `<div style="
  position: fixed;
  top: 0;
  left: 0;
  width: 100vw;
  height: 100vh;
  z-index: 999999;
  background: #ff0000 !important;
  color: white;
  font-size: 48px;
  display: flex;
  align-items: center;
  justify-content: center;
">MALICIOUS FULLSCREEN DIV</div>`;

const MALICIOUS_ZINDEX = `<span style="
  position: relative;
  z-index: 999999;
  background: purple;
  color: white;
  padding: 20px;
  font-size: 32px;
">HIGH Z-INDEX SPAN</span>`;

const MALICIOUS_TRANSFORM = `<div style="
  position: fixed;
  top: 50%;
  left: 50%;
  transform: translate(-50%, -50%) scale(10);
  z-index: 999998;
  background: black;
  color: lime;
  padding: 40px;
  font-size: 20px;
">SCALED OVERLAY</div>`;

const MALICIOUS_VIEWPORT_UNITS = `<div style="
  width: 100vw;
  height: 100vh;
  position: absolute;
  top: 0;
  left: 0;
  background: linear-gradient(45deg, red, blue);
  z-index: 99999;
">VIEWPORT BREAKOUT</div>`;

const LEGITIMATE_STYLED = `<h2 style="color: #2563eb; font-size: 24px; margin-bottom: 12px;">Blue Heading</h2>
<p style="text-align: center; font-style: italic; color: #6b7280;">Centered italic gray text with <span style="font-weight: bold; color: #dc2626;">bold red span</span> inside.</p>
<ul style="padding-left: 2em;">
  <li style="color: #059669;">Green list item</li>
  <li style="color: #7c3aed;">Purple list item</li>
</ul>`;

// ── Helper: fill WYSIWYG via source mode ──────────────────────
async function fillWysiwygSource(page: any, html: string) {
  // Click HTML source mode button inside the WYSIWYG editor
  const wysiwyg = page.locator('.wysiwyg-editor');
  const htmlBtn = wysiwyg.locator('button', { hasText: /html/i });
  await htmlBtn.click();
  // Fill the textarea inside the WYSIWYG editor only
  const textarea = wysiwyg.locator('textarea');
  await textarea.fill(html);
  // Switch back to preview mode
  const previewBtn = wysiwyg.locator('button', { hasText: /preview/i });
  await previewBtn.click();
}

// ── Helper: fill required blog-post fields for save to succeed ──
async function fillBlogPostRequired(page: any, ui: any, title: string) {
  await ui.fillTitle(title);
  await ui.fillTextField('URL Slug', `css-test-${Date.now().toString(36)}`);
}

// ── Helper: assert page chrome is not obscured ───────────────────
async function assertPageChromeIntact(page: any) {
  await expect(page.locator('aside nav')).toBeVisible({ timeout: 5000 });
  await expect(page.locator('header, [role="banner"]').first()).toBeVisible();
  // Sidebar nav links must be clickable
  const firstSidebarLink = page.locator('aside nav a').first();
  await expect(firstSidebarLink).toBeVisible();
}

// ── Helper: assert editor is bounded ─────────────────────────────
async function assertEditorBounded(page: any) {
  const editorBox = await page.locator('.wysiwyg-editor').boundingBox();
  expect(editorBox).not.toBeNull();
  // Editor must NOT stretch to full viewport
  expect(editorBox!.width).toBeLessThan(1200);
  expect(editorBox!.height).toBeLessThan(1000);
}

test.describe('WYSIWYG CSS Isolation', () => {
  test.describe.configure({ mode: 'serial' });

  test.afterAll(async ({ request }) => {
    const { ApiHelper } = await import('./helpers');
    const api = new ApiHelper(request);
    await api.login();
    await api.deleteAll(ENTITY);
  });

  // ═══════════════════════════════════════════════════════════════
  // CREATE PAGE — malicious CSS must not escape
  // ═══════════════════════════════════════════════════════════════
  test('create page: malicious fullscreen div is confined to editor', async ({ page, ui, api }) => {
    await api.deleteAll(ENTITY);
    await ui.gotoNewEntity(ENTITY);

    await ui.fillTitle(`CSS-Fullscreen ${TS()}`);
    await fillWysiwygSource(page, MALICIOUS_FULLSCREEN);

    await assertPageChromeIntact(page);
    await assertEditorBounded(page);

    // "Save Now" must be clickable
    await expect(page.getByRole('button', { name: /save now/i })).toBeVisible();
  });

  test('create page: high z-index span does not stack above header', async ({ page, ui, api }) => {
    await api.deleteAll(ENTITY);
    await ui.gotoNewEntity(ENTITY);

    await ui.fillTitle(`CSS-ZIndex ${TS()}`);
    await fillWysiwygSource(page, MALICIOUS_ZINDEX);

    await assertPageChromeIntact(page);
    await expect(page.getByRole('button', { name: /save now/i })).toBeVisible();
  });

  test('create page: transform scale overlay is confined', async ({ page, ui, api }) => {
    await api.deleteAll(ENTITY);
    await ui.gotoNewEntity(ENTITY);

    await ui.fillTitle(`CSS-Transform ${TS()}`);
    await fillWysiwygSource(page, MALICIOUS_TRANSFORM);

    await assertPageChromeIntact(page);
    await assertEditorBounded(page);
  });

  test('create page: viewport unit breakout is confined', async ({ page, ui, api }) => {
    await api.deleteAll(ENTITY);
    await ui.gotoNewEntity(ENTITY);

    await ui.fillTitle(`CSS-VP-Units ${TS()}`);
    await fillWysiwygSource(page, MALICIOUS_VIEWPORT_UNITS);

    await assertPageChromeIntact(page);
    // The editor should NOT be viewport-sized
    const editorBox = await page.locator('.wysiwyg-editor').boundingBox();
    expect(editorBox).not.toBeNull();
    // If containment failed, this would be ~viewport width
    expect(editorBox!.width).toBeLessThan(1200);
  });

  // ═══════════════════════════════════════════════════════════════
  // LEGITIMATE CSS — must still work
  // ═══════════════════════════════════════════════════════════════
  test('create page: legitimate inline CSS renders and is stored intact', async ({ page, ui, api }) => {
    await api.deleteAll(ENTITY);
    await ui.gotoNewEntity(ENTITY);

    await ui.fillTitle(`CSS-Legit ${TS()}`);
    await fillWysiwygSource(page, LEGITIMATE_STYLED);

    // Toggle source mode to verify HTML is untouched
    const wysiwyg = page.locator('.wysiwyg-editor');
    const htmlBtn = wysiwyg.locator('button', { hasText: /html/i });
    await htmlBtn.click();
    const textarea = wysiwyg.locator('textarea');
    const html = await textarea.inputValue();

    expect(html).toContain('color: #2563eb');
    expect(html).toContain('text-align: center');
    expect(html).toContain('color: #dc2626');
    expect(html).toContain('color: #059669');
    expect(html).toContain('color: #7c3aed');

    // Toggle back to preview and verify page chrome intact
    await wysiwyg.locator('button', { hasText: /preview/i }).click();
    await assertPageChromeIntact(page);
  });

  // ═══════════════════════════════════════════════════════════════
  // SOURCE MODE ROUND-TRIP
  // ═══════════════════════════════════════════════════════════════
  test('source mode toggle round-trips HTML preserving all CSS', async ({ page, ui, api }) => {
    await api.deleteAll(ENTITY);
    await ui.gotoNewEntity(ENTITY);

    await ui.fillTitle(`CSS-RoundTrip ${TS()}`);
    await fillWysiwygSource(page, LEGITIMATE_STYLED);

    // Toggle source → preview → source again
    const wysiwyg = page.locator('.wysiwyg-editor');
    const htmlBtn = wysiwyg.locator('button', { hasText: /html/i });
    await htmlBtn.click();
    const previewBtn = wysiwyg.locator('button', { hasText: /preview/i });
    await previewBtn.click();
    await htmlBtn.click();

    const textarea = wysiwyg.locator('textarea');
    const html = await textarea.inputValue();
    expect(html).toContain('color: #2563eb');
    expect(html).toContain('font-weight: bold');
    expect(html).toContain('text-align: center');
  });

  // ═══════════════════════════════════════════════════════════════
  // SAVE + VIEW PAGE
  // ═══════════════════════════════════════════════════════════════
  test('view page: saved malicious CSS is confined in view mode', async ({ page, ui, api }) => {
    await api.deleteAll(ENTITY);

    await ui.gotoNewEntity(ENTITY);
    const title = `CSS-View-FS ${TS()}`;
    await fillBlogPostRequired(page, ui, title);
    await fillWysiwygSource(page, MALICIOUS_FULLSCREEN);
    await ui.clickSaveNow();
    await ui.waitForSave();

    // Extract entity ID from URL and navigate to view page
    const url = new URL(page.url());
    const id = url.searchParams.get('id');
    await page.goto(`/entities-view/${ENTITY}?id=${id}`);
    await page.waitForLoadState('networkidle');

    // View page chrome must be intact
    await assertPageChromeIntact(page);

    // The malicious text must be in the DOM (containment traps, not removes)
    await expect(page.locator('text=MALICIOUS FULLSCREEN DIV')).toBeVisible();
  });

  test('view page: saved legitimate CSS renders correctly', async ({ page, ui, api }) => {
    await api.deleteAll(ENTITY);

    await ui.gotoNewEntity(ENTITY);
    const title = `CSS-View-Legit ${TS()}`;
    await fillBlogPostRequired(page, ui, title);
    await fillWysiwygSource(page, LEGITIMATE_STYLED);
    await ui.clickSaveNow();
    await ui.waitForSave();

    const url = new URL(page.url());
    const id = url.searchParams.get('id');
    await page.goto(`/entities-view/${ENTITY}?id=${id}`);
    await page.waitForLoadState('networkidle');

    await assertPageChromeIntact(page);

    // Blue heading should be visible
    await expect(page.locator('h2', { hasText: 'Blue Heading' })).toBeVisible();
    // Italic text should be visible
    await expect(page.locator('text=Centered italic gray text')).toBeVisible();
  });

  // ═══════════════════════════════════════════════════════════════
  // EDIT — reload previously saved entity
  // ═══════════════════════════════════════════════════════════════
  test('edit page: reloading saved malicious CSS is still confined', async ({ page, ui, api }) => {
    await api.deleteAll(ENTITY);

    await ui.gotoNewEntity(ENTITY);
    const title = `CSS-Edit-Reload ${TS()}`;
    await fillBlogPostRequired(page, ui, title);
    await fillWysiwygSource(page, MALICIOUS_FULLSCREEN);
    await ui.clickSaveNow();
    await ui.waitForSave();

    // Navigate directly to the edit page for this entity
    const url = new URL(page.url());
    const id = url.searchParams.get('id');
    await page.goto(`/entities-admin/${ENTITY}?id=${id}`);
    await page.waitForLoadState('networkidle');

    // Edit page loaded — chrome must be intact
    await assertPageChromeIntact(page);
    await assertEditorBounded(page);
  });

  // ═══════════════════════════════════════════════════════════════
  // EMPTY / PLAIN TEXT — containment must not break normal usage
  // ═══════════════════════════════════════════════════════════════
  test('create page: minimal WYSIWYG still works with containment', async ({ page, ui, api }) => {
    await api.deleteAll(ENTITY);
    await ui.gotoNewEntity(ENTITY);

    const title = `CSS-Minimal ${TS()}`;
    await fillBlogPostRequired(page, ui, title);
    await fillWysiwygSource(page, '<p>minimal content</p>');

    const editor = page.locator('[contenteditable="true"]');
    await expect(editor).toBeVisible();

    // Save must work
    await ui.clickSaveNow();
    await ui.waitForSave();

    // Navigate to view page and verify title
    const url = new URL(page.url());
    const id = url.searchParams.get('id');
    await page.goto(`/entities-view/${ENTITY}?id=${id}`);
    await expect(page.locator('h1')).toContainText(title, { timeout: 10000 });
  });

  test('create page: toolbar buttons work with containment class present', async ({ page, ui, api }) => {
    await api.deleteAll(ENTITY);
    await ui.gotoNewEntity(ENTITY);

    await ui.fillTitle(`CSS-Toolbar ${TS()}`);

    // Verify contentEditable has containment class
    const editable = page.locator('[contenteditable="true"]');
    await expect(editable).toHaveClass(/contain-layout-paint/);

    // Toolbar buttons must be visible and not disabled
    const boldBtn = page.getByRole('button', { name: /bold/i });
    await expect(boldBtn).toBeVisible();
    await expect(boldBtn).not.toBeDisabled();

    const italicBtn = page.getByRole('button', { name: /italic/i });
    await expect(italicBtn).toBeVisible();

    const underlineBtn = page.getByRole('button', { name: /underline/i });
    await expect(underlineBtn).toBeVisible();

    // HTML source toggle must be present
    await expect(page.locator('button', { hasText: /html/i }).first()).toBeVisible();
  });

  test('create page: character count still works with containment', async ({ page, ui, api }) => {
    await api.deleteAll(ENTITY);
    await ui.gotoNewEntity(ENTITY);

    await ui.fillTitle(`CSS-Chars ${TS()}`);
    await fillWysiwygSource(page, '<p>Hello World</p>');

    // Character count must show "11 characters"
    await expect(page.locator('text=/11 character/')).toBeVisible();
  });

  // ═══════════════════════════════════════════════════════════════
  // CONTAINMENT CLASS PROPAGATION
  // ═══════════════════════════════════════════════════════════════
  test('view page: WYSIWYG content div has contain-layout-paint class', async ({ page, ui, api }) => {
    await api.deleteAll(ENTITY);

    await ui.gotoNewEntity(ENTITY);
    const title = `CSS-ClassCheck ${TS()}`;
    await fillBlogPostRequired(page, ui, title);
    await fillWysiwygSource(page, '<p>test</p>');
    await ui.clickSaveNow();
    await ui.waitForSave();

    // Navigate to view page
    const url = new URL(page.url());
    const id = url.searchParams.get('id');
    await page.goto(`/entities-view/${ENTITY}?id=${id}`);
    await page.waitForLoadState('networkidle');

    // The div that renders the WYSIWYG HTML must have the containment class
    const proseDiv = page.locator('.contain-layout-paint').first();
    await expect(proseDiv).toBeVisible();
  });

  // ═══════════════════════════════════════════════════════════════
  // <STYLE> TAG INJECTION — must be stripped from editor rendering
  // ═══════════════════════════════════════════════════════════════
  test('create page: style tag CSS is stripped, inline styles survive', async ({ page, ui, api }) => {
    await api.deleteAll(ENTITY);
    await ui.gotoNewEntity(ENTITY);

    await ui.fillTitle(`CSS-StyleTag ${TS()}`);

    // Inject content with both <style> and inline style
    const htmlWithStyleTag = `<style>body { display: none !important; }</style>
<p style="color: green;">This paragraph should remain green.</p>`;

    await fillWysiwygSource(page, htmlWithStyleTag);

    // The page must NOT be blank — <style> must have been stripped
    await assertPageChromeIntact(page);

    // Toggle source mode to verify <style> was stripped but inline style survived
    const wysiwyg = page.locator('.wysiwyg-editor');
    const htmlBtn = wysiwyg.locator('button', { hasText: /html/i });
    await htmlBtn.click();

    const textarea = wysiwyg.locator('textarea');
    const html = await textarea.inputValue();

    // Inline style must survive
    expect(html).toContain('color: green');
    // <style> tag must be gone (DOMPurify strips it)
    expect(html).not.toMatch(/<style/i);
  });
});

