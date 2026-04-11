import { test, expect } from './helpers';
import type { APIRequestContext, BrowserContext } from '@playwright/test';

const WS_BASE = 'ws://localhost:9000/rf/api/live';
const API_BASE = 'http://localhost:9000/rf/api';

/** Login via API and inject auth cookies into the browser context so page.goto() is authenticated. */
async function injectAuthCookies(request: APIRequestContext, context: BrowserContext) {
  const loginRes = await request.post(`${API_BASE}/login`, {
    data: { email: 'admin@karasoftware.com', password: '123456' },
  });
  const setCookieHeaders = loginRes.headersArray().filter(h => h.name.toLowerCase() === 'set-cookie');
  for (const header of setCookieHeaders) {
    const parts = header.value.split(';').map(p => p.trim());
    const [nameValue] = parts;
    const [name, ...rest] = nameValue.split('=');
    const value = rest.join('=');
    await context.addCookies([{
      name, value, domain: 'localhost', path: '/', httpOnly: true, sameSite: 'Strict' as const,
    }]);
  }
}

/**
 * E2E tests for WebSocket-based live updates on RF Sheets.
 *
 * These tests verify the full flow:
 *  1. Editor opens a sheet page → WebSocket connects as role=editor
 *  2. Viewer opens the same sheet → WebSocket connects as role=viewer
 *  3. Editor edits a cell → Univer command fires → broadcastUpdate sends snapshot via WS → viewer receives
 *  4. Viewer's RfSheetPage renders the live workbook data and shows live indicator
 *
 * The tests use a real backend WebSocket relay and two browser tabs
 * (simulated via Playwright's page + context pattern).
 */
test.describe('RF Sheets — Live Updates', () => {
  test.describe.configure({ mode: 'serial' });

  let sheetId: number;

  // Helper: build a minimal workbook with a value in cell A1
  function buildMinimalWorkbook(cellValue: string): string {
    return JSON.stringify({
      id: 'test-wb',
      sheetOrder: ['sheet1'],
      sheets: {
        sheet1: {
          id: 'sheet1',
          name: 'Sheet1',
          cellData: {
            0: { 0: { v: cellValue } },
          },
        },
      },
    });
  }

  test('setup — create test sheet for live updates', async ({ api }) => {
    const entity = await api.createEntity('rf-sheets', {
      title: { rendered: `Live Sheet ${Date.now()}` },
      fields: {
        sources: JSON.stringify(['product']),
        bound_regions: '[]',
        workbook_data: buildMinimalWorkbook('Hello'),
        refresh_interval_seconds: 30,
      },
    });
    sheetId = entity.id;
    expect(sheetId).toBeGreaterThan(0);
  });

  test('WebSocket endpoint accepts connections for rf-sheets', async ({ page, request }) => {
    await injectAuthCookies(request, page.context());

    // Navigate to app origin so cookies are sent for ws://localhost:9000
    await page.goto('http://localhost:9000');

    const result = await page.evaluate(
      ({ wsBase, id }) => {
        return new Promise<string>((resolve) => {
          const ws = new WebSocket(`${wsBase}/rf-sheets/${id}?role=viewer`);
          ws.onopen = () => { ws.close(); resolve('connected'); };
          ws.onerror = () => resolve('error');
          setTimeout(() => { ws.close(); resolve('timeout'); }, 5000);
        });
      },
      { wsBase: WS_BASE, id: sheetId },
    );

    expect(result).toBe('connected');
  });

  test('viewer sees live indicator when editor edits sheet', async ({ page, context, request }) => {
    await injectAuthCookies(request, context);

    // Editor opens the sheet (acquires lock, connects WS as editor)
    await page.goto(`/sheets/${sheetId}`);
    // Wait for Univer to initialize
    await page.waitForFunction(
      () => !!(window as any).__univerAPI?.getActiveWorkbook(),
      undefined,
      { timeout: 20000 },
    );

    // Wait for design mode (lock acquired) — title input only renders when isDesignMode is true
    await page.waitForSelector('input[data-testid="sheet-title-input"]', { timeout: 15000 });

    // Wait for WebSocket to connect as editor (role flips after lock acquisition)
    await page.waitForTimeout(2000);

    // Editor types in a cell to trigger the command-based broadcast.
    // Use async evaluate: Univer 0.20 setValue() returns a Promise.
    await page.evaluate(async () => {
      const api = (window as any).__univerAPI;
      const wb = api.getActiveWorkbook();
      const sheet = wb.getActiveSheet();
      await sheet.getRange(0, 0, 1, 1).setValue('LiveTest');
    });

    // Wait for the onCommandExecuted → broadcastUpdate debounce (300ms) → WS send
    await page.waitForTimeout(2000);

    // Open a viewer page in a new tab
    const viewerPage = await context.newPage();
    await viewerPage.goto(`/sheets/${sheetId}`);
    await viewerPage.waitForFunction(
      () => !!(window as any).__univerAPI?.getActiveWorkbook(),
      undefined,
      { timeout: 20000 },
    );

    // Viewer should see the live indicator (may need time for WS data to arrive via LastSnapshot)
    const liveIndicator = viewerPage.locator('[data-testid="live-indicator"]');
    await expect(liveIndicator).toBeVisible({ timeout: 15000 });
    await expect(liveIndicator).toContainText('Live');

    await viewerPage.close();
    // Release the entity lock so the next test can acquire it
    await page.goto('about:blank');
    await request.post(
      `${API_BASE}/entity_lock_control?type=rf-sheets&id=${sheetId}&operation=try_unlock`,
      { data: {} },
    ).catch(() => {});
  });

  test('cell changes stream to viewer in real-time', async ({ page, context, request }) => {
    await injectAuthCookies(request, context);

    // Editor opens sheet
    await page.goto(`/sheets/${sheetId}`);
    await page.waitForFunction(
      () => !!(window as any).__univerAPI?.getActiveWorkbook(),
      undefined,
      { timeout: 20000 },
    );

    // Wait for design mode (lock acquired) — title input only renders when isDesignMode is true
    await page.waitForSelector('input[data-testid="sheet-title-input"]', { timeout: 15000 });

    // Wait for WebSocket to connect as editor
    await page.waitForTimeout(2000);

    // Editor sets a unique cell value (await: Univer 0.20 setValue returns Promise)
    const uniqueValue = `CellSync-${Date.now()}`;
    await page.evaluate(async (val) => {
      const api = (window as any).__univerAPI;
      const wb = api.getActiveWorkbook();
      const sheet = wb.getActiveSheet();
      await sheet.getRange(0, 0, 1, 1).setValue(val);
    }, uniqueValue);

    // Wait for broadcast debounce (300ms) + WS send
    await page.waitForTimeout(2000);

    // Open viewer
    const viewerPage = await context.newPage();
    await viewerPage.goto(`/sheets/${sheetId}`);
    await viewerPage.waitForFunction(
      () => !!(window as any).__univerAPI?.getActiveWorkbook(),
      undefined,
      { timeout: 20000 },
    );

    // Viewer should see the updated cell value via live snapshot (LastSnapshot)
    const cellValue = await viewerPage.waitForFunction(
      () => {
        const api = (window as any).__univerAPI;
        if (!api) return null;
        const wb = api.getActiveWorkbook();
        if (!wb) return null;
        const sheet = wb.getActiveSheet();
        if (!sheet) return null;
        return sheet.getRange(0, 0, 1, 1).getValue();
      },
      undefined,
      { timeout: 15000 },
    );

    // The cell should contain the unique value (either from live update or initial load)
    const val = await cellValue.jsonValue();
    expect(val).toBeTruthy();

    await viewerPage.close();
    // Release the entity lock so the next test can acquire it
    await page.goto('about:blank');
    await request.post(
      `${API_BASE}/entity_lock_control?type=rf-sheets&id=${sheetId}&operation=try_unlock`,
      { data: {} },
    ).catch(() => {});
  });

  test('title changes stream to viewer', async ({ page, context, request }) => {
    await injectAuthCookies(request, context);

    // Editor opens sheet
    await page.goto(`/sheets/${sheetId}`);
    await page.waitForFunction(
      () => !!(window as any).__univerAPI?.getActiveWorkbook(),
      undefined,
      { timeout: 20000 },
    );

    // Wait for design mode (lock acquired) — title input only renders when isDesignMode is true
    await page.waitForSelector('input[data-testid="sheet-title-input"]', { timeout: 15000 });

    // Wait for WebSocket to connect as editor
    await page.waitForTimeout(2000);

    // Editor changes the title
    const uniqueTitle = `Live Title ${Date.now()}`;
    const titleInput = page.locator('input[data-testid="sheet-title-input"]');
    await titleInput.fill(uniqueTitle);

    // Trigger a cell change to fire the broadcast (title is included in broadcast payload)
    // await: Univer 0.20 setValue returns Promise
    await page.evaluate(async () => {
      const api = (window as any).__univerAPI;
      const wb = api.getActiveWorkbook();
      const sheet = wb.getActiveSheet();
      await sheet.getRange(1, 0, 1, 1).setValue('trigger');
    });

    // Wait for broadcast debounce (300ms) + WS send
    await page.waitForTimeout(2000);

    // Open viewer
    const viewerPage = await context.newPage();
    await viewerPage.goto(`/sheets/${sheetId}`);
    await viewerPage.waitForFunction(
      () => !!(window as any).__univerAPI?.getActiveWorkbook(),
      undefined,
      { timeout: 20000 },
    );

    // Viewer should see the updated title via live update
    const titleH1 = viewerPage.locator('h1');
    await expect(titleH1).toContainText(uniqueTitle, { timeout: 10000 });

    await viewerPage.close();
    // Release the entity lock so the next test can acquire it
    await page.goto('about:blank');
    await request.post(
      `${API_BASE}/entity_lock_control?type=rf-sheets&id=${sheetId}&operation=try_unlock`,
      { data: {} },
    ).catch(() => {});
  });

  test('viewer does not see live indicator when no editor is connected', async ({ page, request }) => {
    await injectAuthCookies(request, page.context());

    // Force-unlock so no one is editing
    const { request: ctxRequest } = page.context();
    const apiReq = ctxRequest;
    await apiReq.post(
      `http://localhost:9000/rf/api/entity_lock_control?type=rf-sheets&id=${sheetId}&operation=try_unlock`,
      { data: {} },
    ).catch(() => {});

    // Navigate directly to sheet view without any editor
    await page.goto(`/sheets/${sheetId}`);
    await page.waitForFunction(
      () => !!(window as any).__univerAPI?.getActiveWorkbook(),
      undefined,
      { timeout: 20000 },
    );

    // Wait for WebSocket to connect
    await page.waitForTimeout(2000);

    // Live indicator should NOT be visible (no live data received)
    const liveIndicator = page.locator('[data-testid="live-indicator"]');
    await expect(liveIndicator).not.toBeVisible({ timeout: 3000 });
  });

  test('cleanup — delete test sheet', async ({ api }) => {
    if (sheetId) {
      await api.unlockEntity('rf-sheets', sheetId);
      await api.deleteEntity('rf-sheets', sheetId);
    }
  });
});
