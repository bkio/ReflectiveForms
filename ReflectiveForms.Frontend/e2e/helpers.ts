import { test as base, expect, Page, APIRequestContext } from '@playwright/test';

const API_BASE = 'http://localhost:9000/rf/api';
const APP_PREFIX = '/rf/app';

// -----------------------------------------------------------------
// API helper – direct HTTP calls for setup/teardown & assertions
// -----------------------------------------------------------------
export class ApiHelper {
  constructor(public request: APIRequestContext) {}

  async login(email = 'admin@karasoftware.com', password = '123456') {
    const res = await this.request.post(`${API_BASE}/login`, {
      data: { email, password },
    });
    expect(res.ok(), `Login failed: ${res.status()}`).toBeTruthy();
    return res;
  }

  async getAllSchemas() {
    const res = await this.request.get(`${API_BASE}/schema`);
    expect(res.ok()).toBeTruthy();
    return (await res.json()) as Record<string, unknown>;
  }

  async peekAll(entityName: string) {
    const res = await this.request.post(
      `${API_BASE}/crud?operation=PEEK_ALL&type=${entityName}`,
      { data: {} },
    );
    expect(res.ok()).toBeTruthy();
    return (await res.json()) as Array<{ id: number; title?: string; name?: string }>;
  }

  async peekAllPaginated(entityName: string, pageSize: number, pageToken?: string) {
    let url = `${API_BASE}/crud?operation=PEEK_ALL_PAGINATED&type=${entityName}&page_size=${pageSize}`;
    if (pageToken) {
      url += `&page_token=${encodeURIComponent(pageToken)}`;
    }
    const res = await this.request.post(url, { data: {} });
    expect(res.ok()).toBeTruthy();
    return (await res.json()) as {
      items: Array<{ id: number; title?: string; name?: string }>;
      next_page_token: string | null;
      total_count: number | null;
    };
  }

  async readEntity(entityName: string, id: number) {
    const res = await this.request.post(
      `${API_BASE}/crud?operation=READ&type=${entityName}`,
      { data: { id } },
    );
    expect(res.ok()).toBeTruthy();
    return await res.json();
  }

  async createEntity(entityName: string, data: Record<string, unknown>) {
    // Auto-add standard entity metadata if not already present
    const withDefaults: Record<string, unknown> = {
      tags: [], categories: [], author: 2, parent: -1,
      ...data,
    };
    const res = await this.request.post(
      `${API_BASE}/crud?operation=CREATE&type=${entityName}`,
      { data: withDefaults },
    );
    if (!res.ok()) {
      const errBody = await res.text();
      expect(res.ok(), `CREATE ${entityName} failed (${res.status()}): ${errBody}`).toBeTruthy();
    }
    return await res.json();
  }

  async updateEntity(entityName: string, data: Record<string, unknown>) {
    const withDefaults: Record<string, unknown> = {
      tags: [], categories: [], author: 2, parent: -1,
      ...data,
    };
    const res = await this.request.post(
      `${API_BASE}/crud?operation=UPDATE&type=${entityName}`,
      { data: withDefaults },
    );
    if (!res.ok()) {
      const errBody = await res.text();
      expect(res.ok(), `UPDATE ${entityName} failed (${res.status()}): ${errBody}`).toBeTruthy();
    }
    return await res.json();
  }

  async deleteEntity(entityName: string, id: number) {
    const res = await this.request.post(
      `${API_BASE}/crud?operation=DELETE&type=${entityName}`,
      { data: { id } },
    );
    return res;
  }

  /** Delete every entity of a given type (cleanup helper). */
  async deleteAll(entityName: string) {
    const list = await this.peekAll(entityName);
    for (const e of list) {
      await this.deleteEntity(entityName, e.id);
    }
  }
}

// -----------------------------------------------------------------
// Page helpers – UI interaction utilities
// -----------------------------------------------------------------
export class UiHelper {
  constructor(public page: Page) {}

  /** Navigate to the dashboard. */
  async gotoDashboard() {
    await this.page.goto(`${APP_PREFIX}/`);
    await this.page.waitForSelector('h1');
  }

  /** Navigate to the entity list page for a given entity type. */
  async gotoEntityList(entityName: string) {
    await this.page.goto(`${APP_PREFIX}/entities/${entityName}`);
    await this.page.waitForSelector('table', { timeout: 15000 });
  }

  /** Navigate to the new-entity form for a given entity type. */
  async gotoNewEntity(entityName: string) {
    await this.page.goto(`${APP_PREFIX}/entities-admin/${entityName}?id=new`);
    await this.page.waitForSelector('form', { timeout: 15000 });
  }

  /** Navigate to the edit-entity form for a given entity type and id. */
  async gotoEditEntity(entityName: string, id: number) {
    await this.page.goto(`${APP_PREFIX}/entities-admin/${entityName}?id=${id}`);
    await this.page.waitForSelector('form', { timeout: 15000 });
    // Wait for entity data to populate the title field (avoids race with async data loading)
    await this.page.waitForFunction(
      () => {
        const input = document.querySelector('input[name="title.rendered"]') as HTMLInputElement | null;
        return input && input.value.length > 0;
      },
      undefined,
      { timeout: 15000 },
    );
  }

  // --- title ---
  async fillTitle(title: string) {
    const input = this.page.locator('input[name="title.rendered"]');
    await input.fill(title);
    return input;
  }

  async getTitle(): Promise<string> {
    return this.page.locator('input[name="title.rendered"]').inputValue();
  }

  // --- generic field helpers ---
  async fillTextField(label: string, value: string) {
    const field = this.fieldWrapperByLabel(label);
    const input = field.locator('input[type="text"], input[type="email"], input[type="url"]');
    await input.fill(value);
    return input;
  }

  async fillTextArea(label: string, value: string) {
    const field = this.fieldWrapperByLabel(label);
    const ta = field.locator('textarea');
    await ta.fill(value);
    return ta;
  }

  async fillNumber(label: string, value: string) {
    const field = this.fieldWrapperByLabel(label);
    const input = field.locator('input[type="number"]');
    await input.fill(value);
    return input;
  }

  async fillDate(label: string, value: string) {
    const field = this.fieldWrapperByLabel(label);
    const input = field.locator('input[type="date"]');
    await input.fill(value);
    return input;
  }

  async selectOption(label: string, value: string) {
    const field = this.fieldWrapperByLabel(label);
    const trigger = field.locator('button[aria-haspopup="listbox"]');
    // Click the chevron area (right side) to avoid hitting the clear (X) button
    await trigger.click({ position: { x: 10, y: 10 } });

    const listbox = field.locator('[role="listbox"]');
    // If the dropdown didn't open (e.g. click hit the clear button), retry
    const isVisible = await listbox.isVisible({ timeout: 1500 }).catch(() => false);
    if (!isVisible) {
      await trigger.click({ position: { x: 10, y: 10 } });
    }
    await expect(listbox).toBeVisible({ timeout: 10000 });

    const option = field.locator(`[role="option"][data-value="${value}"]`);
    await expect(option).toBeVisible({ timeout: 5000 });
    await option.click();
  }

  /**
   * Select an option in a SearchableSelect component (used for Relation/Author fields).
   * @param label - The field label text
   * @param optionPattern - Regex or string to match the option text. If not provided, selects the first option.
   */
  async selectSearchableOption(label: string, optionPattern?: string | RegExp, nth: number = 0) {
    const field = this.fieldWrapperByLabel(label, nth);
    const trigger = field.locator('button[aria-haspopup="listbox"]');
    await trigger.click({ position: { x: 10, y: 10 } });

    const listbox = field.locator('[role="listbox"]');
    const isVisible = await listbox.isVisible().catch(() => false);
    if (!isVisible) {
      await trigger.click({ position: { x: 10, y: 10 } });
    }
    await expect(listbox).toBeVisible({ timeout: 10000 });

    // Wait for at least one option to appear (API might still be loading)
    await expect(field.locator('[role="option"]').first()).toBeVisible({ timeout: 10000 });

    if (optionPattern) {
      const pattern = typeof optionPattern === 'string' ? new RegExp(optionPattern, 'i') : optionPattern;

      // Retry polling for the matching option (paginated data may take time to load)
      const matchingOption = field.locator('[role="option"]').filter({ hasText: pattern });
      await expect(matchingOption.first()).toBeVisible({ timeout: 10000 });
      await matchingOption.first().click();
    } else {
      // Select the first non-placeholder option (index 1, since 0 is "unselect")
      const firstOption = field.locator('[role="option"]').nth(1);
      await expect(firstOption).toBeVisible({ timeout: 10000 });
      await firstOption.click();
    }
  }

  async setCheckbox(label: string, checked: boolean) {
    const field = this.fieldWrapperByLabel(label);
    const cb = field.locator('input[type="checkbox"]');
    if (checked) {
      await cb.check();
    } else {
      await cb.uncheck();
    }
    return cb;
  }

  async setRange(label: string, value: string) {
    const field = this.fieldWrapperByLabel(label);
    const input = field.locator('input[type="range"]');
    await input.fill(value);
    return input;
  }

  async fillWysiwyg(label: string, htmlContent: string) {
    const field = this.fieldWrapperByLabel(label);
    // Switch to HTML source mode if available
    const sourceBtn = field.locator('button', { hasText: /html|source|code/i }).first();
    if (await sourceBtn.isVisible({ timeout: 2000 }).catch(() => false)) {
      await sourceBtn.click();
      const textarea = field.locator('textarea');
      await textarea.fill(htmlContent);
    } else {
      // Fall back to contentEditable
      const editor = field.locator('[contenteditable="true"]');
      await editor.click();
      await editor.fill(htmlContent);
    }
  }

  // --- media upload helpers ---
  async fillMedia(label: string) {
    const field = this.fieldWrapperByLabel(label);
    const fileInput = field.locator('input[type="file"]');
    const inputId = await fileInput.getAttribute('id');
    // id format is "file-input-{path}", e.g. "file-input-fields.primary_image"
    const path = inputId?.replace('file-input-', '') ?? '';

    // Set the value to a media path that the backend accepts directly
    // (bypasses SkiaSharp image processing which may not work on all platforms)
    await this.page.evaluate((fieldPath: string) => {
      const setValue = (window as any).__rfFormSetValue;
      if (setValue) {
        setValue(fieldPath, '/media/placeholder.png', { shouldDirty: true });
      }
    }, path);
  }

  // --- repeater helpers ---
  async addRepeaterItem(label: string) {
    const field = this.fieldWrapperByLabel(label);
    const addBtn = field.locator('button').filter({ hasText: /add/i }).last();
    await addBtn.click();
  }

  repeaterItems(label: string) {
    const field = this.fieldWrapperByLabel(label);
    // Use the direct child combinator (>) from .space-y-4 to avoid matching
    // nested repeater items (e.g. questions inside sections).
    return field.locator('> div > .space-y-4 > .border.border-gray-200.rounded-lg.overflow-hidden');
  }

  // --- save ---
  async clickSaveNow() {
    const btn = this.page.locator('button[type="submit"]', { hasText: /save now/i });
    await btn.click();
  }

  /** Wait until the "Saved!" indicator appears or throw on error indicator/toast. */
  async waitForSave(timeoutMs = 30000) {
    // Wait for autosave-saved indicator or error indicator/toast
    const result = await this.page.waitForFunction(
      () => {
        // Check new AutoSaveIndicator component
        const saved = document.querySelector('[data-testid="autosave-saved"]');
        if (saved) return { saved: true };

        const error = document.querySelector('[data-testid="autosave-error"]');
        if (error) return { error: error.textContent };

        // Fallback: check sonner toasts for error
        const toasts = document.querySelectorAll('[data-sonner-toast]');
        for (const t of toasts) {
          if (t.getAttribute('data-type') === 'error') {
            return { error: t.textContent };
          }
        }
        return null;
      },
      undefined,
      { timeout: timeoutMs },
    );
    const val = await result.jsonValue();
    if (val && 'error' in val) {
      throw new Error(`Save failed: ${val.error}`);
    }
  }

  // --- conditional visibility ---
  async fieldIsVisible(label: string): Promise<boolean> {
    return this.fieldWrapperByLabel(label).isVisible({ timeout: 3000 }).catch(() => false);
  }

  // --- list page ---
  entityRows() {
    // Exclude the empty-state "No ... found" row which uses td[colspan]
    return this.page.locator('tbody tr').filter({ hasNot: this.page.locator('td[colspan]') });
  }

  async entityRowCount(): Promise<number> {
    return this.entityRows().count();
  }

  async clickDeleteOnRow(index: number) {
    const row = this.entityRows().nth(index);
    await row.locator('button[title="Delete"]').click();
  }

  async clickEditOnRow(index: number) {
    const row = this.entityRows().nth(index);
    await row.locator('a[title="Edit"]').click();
    await this.page.waitForSelector('form', { timeout: 15000 });
  }

  // --- private ---
  private fieldWrapperByLabel(label: string, nth: number = 0) {
    // Use exact text matching to avoid substring collisions
    // (e.g. "URL" must not match "Canonical URL" or "URL Slug").
    const escaped = label.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
    return this.page.locator('label', { hasText: new RegExp(`^\\s*${escaped}\\s*\\*?\\s*$`) }).nth(nth)
      .locator('xpath=ancestor::div[contains(@class,"field-wrapper")][1]');
  }
}

// -----------------------------------------------------------------
// Extended test fixture
// -----------------------------------------------------------------
type Fixtures = {
  api: ApiHelper;
  ui: UiHelper;
};

export const test = base.extend<Fixtures>({
  api: async ({ request }, use) => {
    const api = new ApiHelper(request);
    // Authenticate so cookies are available for subsequent requests
    await api.login();
    await use(api);
  },
  ui: async ({ page, request }, use) => {
    // Login via the standalone request context (which the api fixture already uses)
    // and extract the auth cookie to inject into the browser context.
    const loginRes = await request.post(`${API_BASE}/login`, {
      data: { email: 'admin@karasoftware.com', password: '123456' },
    });
    // Extract Set-Cookie from the response and add to the browser context
    const setCookieHeaders = loginRes.headersArray().filter(h => h.name.toLowerCase() === 'set-cookie');
    for (const header of setCookieHeaders) {
      const parts = header.value.split(';').map(p => p.trim());
      const [nameValue] = parts;
      const [name, ...rest] = nameValue.split('=');
      const value = rest.join('=');
      await page.context().addCookies([{
        name,
        value,
        domain: 'localhost',
        path: '/',
        httpOnly: true,
        sameSite: 'Strict',
      }]);
    }
    await use(new UiHelper(page));
  },
});

export { expect };
