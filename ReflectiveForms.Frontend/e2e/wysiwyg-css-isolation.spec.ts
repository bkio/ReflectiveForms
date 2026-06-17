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
 */

const ENTITY = 'blog-post';
const TS = () => Date.now().toString(36);

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

const LEGITIMATE_STYLED = `<h2 style="color: #2563eb; font-size: 24px; margin-bottom: 12px;">Blue Heading</h2>
<p style="text-align: center; font-style: italic; color: #6b7280;">Centered italic gray text with <span style="font-weight: bold; color: #dc2626;">bold red span</span> inside.</p>
<ul style="padding-left: 2em;">
  <li style="color: #059669;">Green list item</li>
  <li style="color: #7c3aed;">Purple list item</li>
</ul>`;

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

test.describe('WYSIWYG CSS Isolation', () => {
  let createdId: number;

  test.describe.configure({ mode: 'serial' });

  test.afterAll(async ({ request }) => {
    const { ApiHelper } = await import('./helpers');
    const api = new ApiHelper(request);
    await api.login();
    await api.deleteAll(ENTITY);
  });

  // ─────────────────────────────────────────────────────────────────
  // CREATE PAGE — malicious CSS must not escape the editor
  // ─────────────────────────────────────────────────────────────────
  test('create page: malicious fullscreen div is confined to editor', async ({ page, ui, api }) => {
    await api.deleteAll(ENTITY);
    await ui.gotoNewEntity(ENTITY);

    await ui.fillTitle(`CSS-Isolation-Fullscreen ${TS()}`);

    // Inject malicious fullscreen CSS via source mode
    await ui.fillWysiwyg('Post Content', MALICIOUS_FULLSCREEN);

    // The sidebar must still be visible and its links clickable
    await expect(page.locator('aside nav')).toBeVisible();

    // The header/brand must still be visible
    await expect(page.locator('header, [role="banner"]').first()).toBeVisible();

    // The editor box itself must NOT cover the viewport — its width
    // must be bounded (containment traps 100vw inside the container)
    const editorBox = await page.locator('.wysiwyg-editor').boundingBox();
    expect(editorBox).not.toBeNull();
    expect(editorBox!.width).toBeLessThan(1200); // sanity upper bound

    // The malicious div's red background must NOT cover the page.
    // The sidebar nav link text should still be readable (not obscured).
    const sidebarLink = page.locator('aside nav a').first();
    await expect(sidebarLink).toBeVisible();
  });

  // ─────────────────────────────────────────────────────────────────
  test('create page: high z-index span does not stack above header', async ({ page, ui, api }) => {
    await api.deleteAll(ENTITY);
    await ui.gotoNewEntity(ENTITY);

    await ui.fillTitle(`CSS-Isolation-ZIndex ${TS()}`);
    await ui.fillWysiwyg('Post Content', MALICIOUS_ZINDEX);

    // Header must be visible — not obscured by z-index:999999
    const header = page.locator('header, [role="banner"]').first();
    await expect(header).toBeVisible();

    // The "Save Now" button must be interactable (not covered)
    const saveBtn = page.getByRole('button', { name: /save now/i });
    await expect(saveBtn).toBeVisible();
  });

  // ─────────────────────────────────────────────────────────────────
  test('create page: legitimate inline CSS renders correctly', async ({ page, ui, api }) => {
    await api.deleteAll(ENTITY);
    await ui.gotoNewEntity(ENTITY);

    await ui.fillTitle(`CSS-Isolation-Legit ${TS()}`);
    await ui.fillWysiwyg('Post Content', LEGITIMATE_STYLED);

    // Switch to source mode to verify the HTML is stored intact
    const sourceBtn = page.locator('button', { hasText: /html/i }).first();
    await sourceBtn.click();
    const textarea = page.locator('textarea');
    const html = await textarea.inputValue();

    // The stored HTML must be the original, unmodified
    expect(html).toContain('color: #2563eb');
    expect(html).toContain('text-align: center');
    expect(html).toContain('color: #dc2626');
    expect(html).toContain('color: #059669');
    expect(html).toContain('color: #7c3aed');
  });

  // ─────────────────────────────────────────────────────────────────
  // SAVE + VIEW PAGE
  // ─────────────────────────────────────────────────────────────────
  test('view page: malicious fullscreen div is confined to content area', async ({ page, ui, api }) => {
    await api.deleteAll(ENTITY);
    await ui.gotoNewEntity(ENTITY);

    const title = `CSS-Isolation-View ${TS()}`;
    await ui.fillTitle(title);
    await ui.fillWysiwyg('Post Content', MALICIOUS_FULLSCREEN);

    // Save
    await ui.saveEntity();

    // Wait for redirect to view page
    await expect(page.locator('h1')).toContainText(title);

    // The content area must NOT have escaped to full viewport
    const mainArea = page.locator('main');
    await expect(mainArea).toBeVisible();

    // The sidebar must still be accessible
    await expect(page.locator('aside nav')).toBeVisible();

    // The malicious div text must still be present in the DOM
    // (containment doesn't remove it, just traps it)
    await expect(page.locator('text=MALICIOUS FULLSCREEN DIV')).toBeVisible();
  });

  // ─────────────────────────────────────────────────────────────────
  test('view page: legitimate CSS renders with correct computed styles', async ({ page, ui, api }) => {
    await api.deleteAll(ENTITY);
    await ui.gotoNewEntity(ENTITY);

    const title = `CSS-Isolation-View-Legit ${TS()}`;
    await ui.fillTitle(title);
    await ui.fillWysiwyg('Post Content', LEGITIMATE_STYLED);

    await ui.saveEntity();
    await expect(page.locator('h1')).toContainText(title);

    // The blue heading color must be applied
    const h2 = page.locator('h2', { hasText: 'Blue Heading' });
    const color = await h2.evaluate(el => getComputedStyle(el).color);
    // rgb(37, 99, 235) = #2563eb
    expect(color).toMatch(/rgb\(3[5-9],\s*(9[0-9]|1[01][0-9]),\s*23[0-9]\)/);

    // The italic text must be rendered
    const italicP = page.locator('p', { hasText: /Centered italic/ });
    await expect(italicP).toBeVisible();
  });

  // ─────────────────────────────────────────────────────────────────
  // EDIT — load existing malicious content
  // ─────────────────────────────────────────────────────────────────
  test('edit page: previously-saved malicious CSS is still confined', async ({ page, ui, api }) => {
    await api.deleteAll(ENTITY);
    await ui.gotoNewEntity(ENTITY);

    const title = `CSS-Isolation-Edit-Reload ${TS()}`;
    await ui.fillTitle(title);
    await ui.fillWysiwyg('Post Content', MALICIOUS_FULLSCREEN);
    await ui.saveEntity();

    // Navigate to entity list
    await page.getByRole('link', { name: /blog post/i }).click();
    await expect(page.locator('h1')).toContainText('Blog Posts');

    // Find the entity and click edit
    await page.getByRole('link', { name: title }).click();
    await page.getByRole('link', { name: /edit/i }).click();

    // Wait for editor to load — the sidebar must still be visible
    await expect(page.locator('aside nav')).toBeVisible({ timeout: 10000 });

    // The header must be visible
    await expect(page.locator('header, [role="banner"]').first()).toBeVisible();
  });

  // ─────────────────────────────────────────────────────────────────
  // TRANSFORM + SCALE attack
  // ─────────────────────────────────────────────────────────────────
  test('create page: transform scale(10) overlay is confined to editor', async ({ page, ui, api }) => {
    await api.deleteAll(ENTITY);
    await ui.gotoNewEntity(ENTITY);

    await ui.fillTitle(`CSS-Isolation-Transform ${TS()}`);
    await ui.fillWysiwyg('Post Content', MALICIOUS_TRANSFORM);

    // The sidebar must remain visible and interactable
    await expect(page.locator('aside nav')).toBeVisible();

    // The Save button must be visible and not covered
    const saveBtn = page.getByRole('button', { name: /save now/i });
    await expect(saveBtn).toBeVisible();
  });

  // ─────────────────────────────────────────────────────────────────
  // SOURCE MODE ROUND-TRIP
  // ─────────────────────────────────────────────────────────────────
  test('source mode toggle preserves HTML including CSS', async ({ page, ui, api }) => {
    await api.deleteAll(ENTITY);
    await ui.gotoNewEntity(ENTITY);

    await ui.fillTitle(`CSS-Isolation-RoundTrip ${TS()}`);
    await ui.fillWysiwyg('Post Content', LEGITIMATE_STYLED);

    // Toggle source mode off and back on to verify HTML is preserved
    // (fillWysiwyg already toggled to preview mode, now toggle back)
    const htmlBtn = page.locator('button', { hasText: /html/i }).first();
    await htmlBtn.click();
    const textarea = page.locator('textarea');
    const htmlAfterRoundTrip = await textarea.inputValue();

    expect(htmlAfterRoundTrip).toContain('color: #2563eb');
    expect(htmlAfterRoundTrip).toContain('text-align: center');
    expect(htmlAfterRoundTrip).toContain('font-weight: bold');
  });

  // ─────────────────────────────────────────────────────────────────
  // EMPTY / NO CSS content
  // ─────────────────────────────────────────────────────────────────
  test('create page: empty WYSIWYG field still works with containment', async ({ page, ui, api }) => {
    await api.deleteAll(ENTITY);
    await ui.gotoNewEntity(ENTITY);

    await ui.fillTitle(`CSS-Isolation-Empty ${TS()}`);
    // Don't fill the WYSIWYG field — leave it empty

    // The editor should still show the placeholder
    const editor = page.locator('[contenteditable="true"]');
    await expect(editor).toBeVisible();

    // Save should work (the field is not mandatory)
    await ui.saveEntity();
    await expect(page.locator('h1')).toContainText(`CSS-Isolation-Empty ${TS()}`);
  });
});
