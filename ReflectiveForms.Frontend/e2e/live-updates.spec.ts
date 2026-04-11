import { test, expect } from './helpers';

const WS_BASE = 'ws://localhost:9000/rf/api/live';

/**
 * E2E tests for WebSocket-based live entity updates.
 *
 * These tests verify the full flow:
 *  1. Editor opens edit page → WebSocket connects as role=editor
 *  2. Viewer opens view page → WebSocket connects as role=viewer
 *  3. Editor types → form.watch fires → broadcastUpdate sends via WS → viewer receives
 *  4. Viewer's EntityViewPage renders the live data
 *
 * The tests use a real backend WebSocket relay and two browser tabs
 * (simulated via Playwright's page + context pattern).
 */
test.describe('Live Updates', () => {
  test.describe.configure({ mode: 'serial' });

  const ENTITY = 'team-member';
  let entityId: number;

  const validFields = {
    job_title: 'Live Test Engineer',
    email: 'live@test.com',
    is_remote: false,
    department: 'engineering',
    bio: '',
    years_of_experience: 1,
    performance_score: 5,
    salary: 50000,
    hire_date: '20260101',
    avatar: '',
    office_address: { street: '1 Live St', city: 'Livetown', postal_code: '00001' },
    emergency_contacts: [{ contact_name: 'EC', relationship: 'friend', phone: '555-0000', email: '' }],
    social_links: [],
    favorite_blog_post: -1,
  };

  test('setup — create test entity for live updates', async ({ api }) => {
    const entity = await api.createEntity(ENTITY, {
      title: { rendered: `Live Test ${Date.now()}` },
      fields: validFields,
    });
    entityId = entity.id;
    expect(entityId).toBeGreaterThan(0);
  });

  test('WebSocket endpoint accepts connections with valid auth', async ({ page, request }) => {
    // Login via the request context and inject auth cookies into the browser
    // page context so that the WebSocket opened inside page.evaluate has valid
    // credentials (page.evaluate runs in the browser, which has its own cookie jar).
    const loginRes = await request.post('http://localhost:9000/rf/api/login', {
      data: { email: 'admin@karasoftware.com', password: '123456' },
    });
    const setCookieHeaders = loginRes.headersArray().filter(h => h.name.toLowerCase() === 'set-cookie');
    for (const header of setCookieHeaders) {
      const parts = header.value.split(';').map(p => p.trim());
      const [nameValue] = parts;
      const [name, ...rest] = nameValue.split('=');
      const value = rest.join('=');
      await page.context().addCookies([{
        name, value, domain: 'localhost', path: '/', httpOnly: true, sameSite: 'Strict' as const,
      }]);
    }

    // Navigate to the app origin so browser cookies are sent for ws://localhost:9000
    await page.goto('http://localhost:9000');

    // Connect to WebSocket as viewer via page.evaluate
    const result = await page.evaluate(
      ({ wsBase, entityName, id }) => {
        return new Promise<string>((resolve) => {
          const ws = new WebSocket(`${wsBase}/${entityName}/${id}?role=viewer`);
          ws.onopen = () => {
            ws.close();
            resolve('connected');
          };
          ws.onerror = () => resolve('error');
          setTimeout(() => {
            ws.close();
            resolve('timeout');
          }, 5000);
        });
      },
      { wsBase: WS_BASE, entityName: ENTITY, id: entityId },
    );

    expect(result).toBe('connected');
  });

  test('view page shows live indicator when editor is connected', async ({ page, context, api, ui }) => {
    // Navigate editor to the edit page (acquires lock, connects WS as editor)
    await ui.gotoEditEntity(ENTITY, entityId);
    await expect(page.locator('form')).toBeVisible();

    // The live indicator only appears once liveData is non-null, which requires
    // the editor to actually send a change (not just connect). Type something
    // to trigger the first broadcast.
    const titleInput = page.locator('input[name="title.rendered"]');
    await titleInput.fill(`Live Indicator ${Date.now()}`);
    // Wait for debounced broadcast to fire
    await page.waitForTimeout(1000);

    // Open a new page as the viewer
    const viewerPage = await context.newPage();
    await viewerPage.goto(`/entities-view/${ENTITY}?id=${entityId}`);
    await viewerPage.waitForSelector('h1', { timeout: 15000 });

    // Wait for live indicator to appear
    const liveIndicator = viewerPage.locator('[data-testid="live-indicator"]');
    await expect(liveIndicator).toBeVisible({ timeout: 10000 });
    await expect(liveIndicator).toContainText('Live');

    await viewerPage.close();
  });

  test('editor changes are streamed live to viewer', async ({ page, context, api, ui }) => {
    // Navigate editor to the edit page
    await ui.gotoEditEntity(ENTITY, entityId);
    await expect(page.locator('form')).toBeVisible();

    // Editor: change the title first so the broadcast fires
    const titleInput = page.locator('input[name="title.rendered"]');
    const uniqueTitle = `Live Title ${Date.now()}`;
    await titleInput.fill(uniqueTitle);

    // Wait for debounced broadcast to fire
    await page.waitForTimeout(1000);

    // Open a viewer page — late-joining viewer receives the LastSnapshot
    const viewerPage = await context.newPage();
    await viewerPage.goto(`/entities-view/${ENTITY}?id=${entityId}`);
    await viewerPage.waitForSelector('h1', { timeout: 15000 });

    // The viewer page should show the updated title via live updates / last snapshot
    await expect(viewerPage.locator('h1')).toContainText(uniqueTitle, { timeout: 10000 });

    await viewerPage.close();
  });

  test('editor field changes appear on viewer in real-time', async ({ page, context, api, ui }) => {
    // Navigate editor to the edit page
    await ui.gotoEditEntity(ENTITY, entityId);
    await expect(page.locator('form')).toBeVisible();

    // Editor: change a text field (Job Title) to trigger broadcast
    const uniqueJobTitle = `Architect ${Date.now()}`;
    await ui.fillTextField('Job Title', uniqueJobTitle);

    // Wait for debounced broadcast to fire
    await page.waitForTimeout(1000);

    // Open a viewer page — late-joining viewer receives the LastSnapshot
    const viewerPage = await context.newPage();
    await viewerPage.goto(`/entities-view/${ENTITY}?id=${entityId}`);
    await viewerPage.waitForSelector('h1', { timeout: 15000 });

    // Viewer should see the updated job title in the view page
    await expect(viewerPage.locator(`text=${uniqueJobTitle}`)).toBeVisible({ timeout: 10000 });

    await viewerPage.close();
  });

  test('viewer does not see live indicator when no editor is connected', async ({ page }) => {
    // Navigate directly to view page without any editor connected
    await page.goto(`/entities-view/${ENTITY}?id=${entityId}`);
    await page.waitForSelector('h1', { timeout: 15000 });

    // Live indicator should NOT be visible (no live data received)
    const liveIndicator = page.locator('[data-testid="live-indicator"]');
    // Wait a reasonable time for WebSocket to connect (if it would)
    await page.waitForTimeout(2000);
    // isLive requires both connected status AND having received at least one update,
    // so even if WS connects, no live indicator without data
    await expect(liveIndicator).not.toBeVisible({ timeout: 3000 });
  });

  test('cleanup — delete test entity', async ({ api }) => {
    if (entityId) {
      await api.unlockEntity(ENTITY, entityId);
      await api.deleteEntity(ENTITY, entityId);
    }
  });
});
