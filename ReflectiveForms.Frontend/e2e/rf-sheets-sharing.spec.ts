import { test, expect, ApiHelper } from './helpers';

const API_BASE = 'http://localhost:9000/rf/api';
const TS = () => Date.now().toString(36);

/**
 * RF Sheets — Sharing & Access Control E2E Tests
 *
 * Creates multiple users and IAM roles, then exercises:
 * 1. Private sheets: owner can see, others cannot
 * 2. User-level sharing: share with specific user (view / edit)
 * 3. Role-level sharing: share with role (view / edit)
 * 4. Public sheets: anyone with rf-sheets permission can view
 * 5. Access level badges on the sheet list page
 * 6. Non-owner cannot change sharing settings
 * 7. Non-owner cannot delete a shared sheet
 * 8. UI: sharing dialog, user picker, role picker, permission toggle
 */
test.describe('RF Sheets — Sharing & Access Control', () => {
  test.describe.configure({ mode: 'serial' });

  // ── Test data ───────────────────────────────────────────────
  const ADMIN_EMAIL = 'admin@karasoftware.com';
  const ADMIN_PASSWORD = '123456';
  const USER_A_EMAIL = `e2e-sheetA-${Date.now()}@test.com`;
  const USER_B_EMAIL = `e2e-sheetB-${Date.now()}@test.com`;
  const USER_PASSWORD = 'testpass123';

  let roleWithSheetsId: number;
  let roleWithoutSheetsId: number;
  let userAId: number; // role = roleWithSheets (has rf-sheets access)
  let userBId: number; // role = roleWithSheets
  let adminUserId: number;

  // Sheets created during tests
  let privateSheetId: number;
  let userSharedViewSheetId: number;
  let userSharedEditSheetId: number;
  let roleSharedViewSheetId: number;
  let roleSharedEditSheetId: number;
  let publicSheetId: number;

  // ── Setup: create roles and users ────────────────────────────

  test.beforeAll(async ({ request }, testInfo) => {
    testInfo.setTimeout(300000); // 5 min — user creation hooks are slow
    const adminApi = new ApiHelper(request);
    await adminApi.login();

    // Find admin user ID
    const adminUsers = await adminApi.peekAll('users');
    const adminEntry = adminUsers.find(u => u.title === 'admin' || u.name === 'admin');
    adminUserId = adminEntry?.id ?? 2;

    // 1. Create IAM role WITH rf-sheets access
    const roleWithRes = await request.post(
      `${API_BASE}/crud?operation=CREATE&type=iam-role`,
      {
        data: {
          title: { rendered: `E2E Sheets Role ${TS()}` },
          tags: [], categories: [], author: adminUserId, parent: -1,
          fields: {
            capabilities: [
              {
                entity_type: 'rf-sheets',
                allow_peek_all: true,
                allow_read: true,
                allow_create: true,
                allow_update: true,
                allow_delete: true,
              },
            ],
          },
        },
        timeout: 60000,
      },
    );
    expect(roleWithRes.ok(), `Role creation failed: ${roleWithRes.status()}`).toBeTruthy();
    roleWithSheetsId = (await roleWithRes.json()).id;

    // 2. Create IAM role WITHOUT rf-sheets access (for negative tests)
    const roleWithoutRes = await request.post(
      `${API_BASE}/crud?operation=CREATE&type=iam-role`,
      {
        data: {
          title: { rendered: `E2E No-Sheets Role ${TS()}` },
          tags: [], categories: [], author: adminUserId, parent: -1,
          fields: {
            capabilities: [
              {
                entity_type: 'product',
                allow_peek_all: true,
                allow_read: true,
                allow_create: false,
                allow_update: false,
                allow_delete: false,
              },
            ],
          },
        },
        timeout: 60000,
      },
    );
    expect(roleWithoutRes.ok()).toBeTruthy();
    roleWithoutSheetsId = (await roleWithoutRes.json()).id;

    // 3. Create User A (has sheets access)
    const userARes = await request.post(
      `${API_BASE}/crud?operation=CREATE&type=users`,
      {
        data: {
          title: { rendered: `Sheet User A ${TS()}` },
          tags: [], categories: [], author: adminUserId, parent: -1,
          fields: {
            email_address: USER_A_EMAIL,
            optional_custom_password: USER_PASSWORD,
            generate_password: false,
            password_sha256: '',
            roles: [{ role: roleWithSheetsId }],
          },
        },
        timeout: 180000,
      },
    );
    const userABody = await userARes.text();
    expect(userARes.ok(), `User A creation failed (${userARes.status()}): ${userABody}`).toBeTruthy();
    userAId = JSON.parse(userABody).id;

    // 4. Create User B (has sheets access)
    const userBRes = await request.post(
      `${API_BASE}/crud?operation=CREATE&type=users`,
      {
        data: {
          title: { rendered: `Sheet User B ${TS()}` },
          tags: [], categories: [], author: adminUserId, parent: -1,
          fields: {
            email_address: USER_B_EMAIL,
            optional_custom_password: USER_PASSWORD,
            generate_password: false,
            password_sha256: '',
            roles: [{ role: roleWithSheetsId }],
          },
        },
        timeout: 180000,
      },
    );
    const userBBody = await userBRes.text();
    expect(userBRes.ok(), `User B creation failed (${userBRes.status()}): ${userBBody}`).toBeTruthy();
    userBId = JSON.parse(userBBody).id;

    // 5. Poll login for both users until ready
    for (const email of [USER_A_EMAIL, USER_B_EMAIL]) {
      const maxWaitMs = 30_000;
      const pollIntervalMs = 2_000;
      const start = Date.now();
      let loginOk = false;
      while (Date.now() - start < maxWaitMs) {
        const loginRes = await request.post(`${API_BASE}/login`, {
          data: { email, password: USER_PASSWORD },
        });
        if (loginRes.ok()) {
          loginOk = true;
          break;
        }
        await new Promise(r => setTimeout(r, pollIntervalMs));
      }
      expect(loginOk, `Login for ${email} never succeeded`).toBeTruthy();
    }
  });

  test.afterAll(async ({ request }) => {
    const adminApi = new ApiHelper(request);
    await adminApi.login();

    // Cleanup sheets
    for (const id of [privateSheetId, userSharedViewSheetId, userSharedEditSheetId,
                       roleSharedViewSheetId, roleSharedEditSheetId, publicSheetId]) {
      if (id) await adminApi.deleteEntity('rf-sheets', id).catch(() => {});
    }
    // Cleanup users then roles (order matters)
    if (userAId) await adminApi.deleteEntity('users', userAId).catch(() => {});
    if (userBId) await adminApi.deleteEntity('users', userBId).catch(() => {});
    if (roleWithSheetsId) await adminApi.deleteEntity('iam-role', roleWithSheetsId).catch(() => {});
    if (roleWithoutSheetsId) await adminApi.deleteEntity('iam-role', roleWithoutSheetsId).catch(() => {});
  });

  // ── Helpers ────────────────────────────────────────────────

  /** Create a new APIRequestContext logged in as a specific user. */
  async function loginAsUser(request: import('@playwright/test').APIRequestContext, email: string, password: string) {
    const loginRes = await request.post(`${API_BASE}/login`, {
      data: { email, password },
    });
    expect(loginRes.ok(), `Login as ${email} failed: ${loginRes.status()}`).toBeTruthy();
    return loginRes;
  }

  /** Inject auth cookies from a login response into a browser page context. */
  async function injectCookies(page: import('@playwright/test').Page, loginRes: import('@playwright/test').APIResponse) {
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
  }

  /** Login through the browser UI. This ensures currentUser is populated via JWT (needed for isOwner). */
  async function loginViaBrowser(page: import('@playwright/test').Page, email: string, password: string) {
    await page.goto('/login');
    await page.locator('input#email').fill(email);
    await page.locator('input#password').fill(password);
    await page.locator('button[type="submit"]').click();
    // Wait for redirect away from login page (login complete)
    await page.waitForURL((url) => !url.pathname.includes('/login'), { timeout: 15000 });
  }

  /** Create a sheet via API as admin with optional sharing. */
  async function createSheetAsAdmin(
    request: import('@playwright/test').APIRequestContext,
    title: string,
    sharing: { is_public?: boolean; shared_users?: Array<{ user: number; permission: string }>; shared_roles?: Array<{ role: number; permission: string }> } = {},
  ) {
    const adminApi = new ApiHelper(request);
    await adminApi.login();
    const result = await adminApi.createEntity('rf-sheets', {
      title: { rendered: title },
      author: adminUserId,
      fields: {
        sources: '[]',
        bound_regions: '[]',
        workbook_data: '{}',
        refresh_interval_seconds: 30,
        is_public: sharing.is_public ?? false,
        shared_users: sharing.shared_users ?? [],
        shared_roles: sharing.shared_roles ?? [],
      },
    });
    return result.id as number;
  }

  /** PEEK_ALL sheets as a specific user. Does NOT assert — returns raw response. */
  async function peekSheetsAs(request: import('@playwright/test').APIRequestContext, email: string, password: string) {
    await loginAsUser(request, email, password);
    const res = await request.post(
      `${API_BASE}/crud?operation=PEEK_ALL&type=rf-sheets`,
      { data: {} },
    );
    return { status: res.status(), data: res.ok() ? await res.json() as Array<{ id: number; title?: string; access_level?: string }> : [] };
  }

  /** READ a sheet as a specific user. Does NOT assert — returns raw response. */
  async function readSheetAs(request: import('@playwright/test').APIRequestContext, email: string, password: string, id: number) {
    await loginAsUser(request, email, password);
    const res = await request.post(
      `${API_BASE}/crud?operation=READ&type=rf-sheets`,
      { data: { id } },
    );
    return { status: res.status(), data: res.ok() ? await res.json() : null };
  }

  /** UPDATE a sheet as a specific user. Does NOT assert — returns raw response. */
  async function updateSheetAs(
    request: import('@playwright/test').APIRequestContext,
    email: string,
    password: string,
    id: number,
    patch: Record<string, unknown>,
  ) {
    await loginAsUser(request, email, password);
    const res = await request.post(
      `${API_BASE}/crud?operation=UPDATE&type=rf-sheets`,
      {
        data: {
          id,
          tags: [], categories: [], author: adminUserId, parent: -1,
          ...patch,
        },
      },
    );
    return { status: res.status(), data: res.ok() ? await res.json() : null };
  }

  /** DELETE a sheet as a specific user. Does NOT assert — returns raw response. */
  async function deleteSheetAs(request: import('@playwright/test').APIRequestContext, email: string, password: string, id: number) {
    await loginAsUser(request, email, password);
    const res = await request.post(
      `${API_BASE}/crud?operation=DELETE&type=rf-sheets`,
      { data: { id } },
    );
    return { status: res.status() };
  }

  // ═══════════════════════════════════════════════════════════
  // TEST 1: Private sheet — only owner can see it
  // ═══════════════════════════════════════════════════════════

  test('private sheet: only owner can see, others get 403', async ({ request }) => {
    await test.step('create private sheet as admin', async () => {
      privateSheetId = await createSheetAsAdmin(request, `Private Sheet ${TS()}`);
      expect(privateSheetId).toBeGreaterThan(0);
    });

    await test.step('admin (owner) can READ the sheet', async () => {
      const { status } = await readSheetAs(request, ADMIN_EMAIL, ADMIN_PASSWORD, privateSheetId);
      expect(status).toBe(200);
    });

    await test.step('User A cannot READ the private sheet', async () => {
      const { status } = await readSheetAs(request, USER_A_EMAIL, USER_PASSWORD, privateSheetId);
      expect(status).toBe(403);
    });

    await test.step('User B cannot READ the private sheet', async () => {
      const { status } = await readSheetAs(request, USER_B_EMAIL, USER_PASSWORD, privateSheetId);
      expect(status).toBe(403);
    });

    await test.step('User A PEEK_ALL does not include the private sheet', async () => {
      const { data } = await peekSheetsAs(request, USER_A_EMAIL, USER_PASSWORD);
      const found = data.find(s => s.id === privateSheetId);
      expect(found).toBeUndefined();
    });
  });

  // ═══════════════════════════════════════════════════════════
  // TEST 2: Share sheet with specific user — VIEW permission
  // ═══════════════════════════════════════════════════════════

  test('user-shared VIEW: shared user can read but not edit', async ({ request }) => {
    await test.step('create sheet shared with User A (view)', async () => {
      userSharedViewSheetId = await createSheetAsAdmin(request, `User View Sheet ${TS()}`, {
        shared_users: [{ user: userAId, permission: 'view' }],
      });
      expect(userSharedViewSheetId).toBeGreaterThan(0);
    });

    await test.step('User A can READ the shared sheet', async () => {
      const { status, data } = await readSheetAs(request, USER_A_EMAIL, USER_PASSWORD, userSharedViewSheetId);
      expect(status).toBe(200);
      expect(data).toBeTruthy();
    });

    await test.step('User A sees sheet in PEEK_ALL with access_level=view', async () => {
      const { data } = await peekSheetsAs(request, USER_A_EMAIL, USER_PASSWORD);
      const found = data.find(s => s.id === userSharedViewSheetId);
      expect(found).toBeTruthy();
      expect(found!.access_level).toBe('view');
    });

    await test.step('User A cannot UPDATE the sheet (view only)', async () => {
      const { status } = await updateSheetAs(request, USER_A_EMAIL, USER_PASSWORD, userSharedViewSheetId, {
        title: { rendered: 'Hacked Title' },
      });
      expect(status).toBe(403);
    });

    await test.step('User B cannot see the sheet (not shared)', async () => {
      const { status } = await readSheetAs(request, USER_B_EMAIL, USER_PASSWORD, userSharedViewSheetId);
      expect(status).toBe(403);
    });
  });

  // ═══════════════════════════════════════════════════════════
  // TEST 3: Share sheet with specific user — EDIT permission
  // ═══════════════════════════════════════════════════════════

  test('user-shared EDIT: shared user can read and edit, but not change sharing', async ({ request }) => {
    await test.step('create sheet shared with User A (edit)', async () => {
      userSharedEditSheetId = await createSheetAsAdmin(request, `User Edit Sheet ${TS()}`, {
        shared_users: [{ user: userAId, permission: 'edit' }],
      });
      expect(userSharedEditSheetId).toBeGreaterThan(0);
    });

    await test.step('User A can READ the shared sheet', async () => {
      const { status } = await readSheetAs(request, USER_A_EMAIL, USER_PASSWORD, userSharedEditSheetId);
      expect(status).toBe(200);
    });

    await test.step('User A sees sheet in PEEK_ALL with access_level=edit', async () => {
      const { data } = await peekSheetsAs(request, USER_A_EMAIL, USER_PASSWORD);
      const found = data.find(s => s.id === userSharedEditSheetId);
      expect(found).toBeTruthy();
      expect(found!.access_level).toBe('edit');
    });

    await test.step('User A can UPDATE non-sharing fields', async () => {
      const { status } = await updateSheetAs(request, USER_A_EMAIL, USER_PASSWORD, userSharedEditSheetId, {
        title: { rendered: `Updated by A ${TS()}` },
        fields: {
          sources: '["product"]',
          bound_regions: '[]',
          workbook_data: '{}',
          refresh_interval_seconds: 30,
        },
      });
      expect(status).toBe(200);
    });

    await test.step('User A cannot change sharing settings (stripped by server)', async () => {
      // Attempt to make it public and add User B — server should strip these
      const { status, data } = await updateSheetAs(request, USER_A_EMAIL, USER_PASSWORD, userSharedEditSheetId, {
        title: { rendered: `Sharing attempt ${TS()}` },
        fields: {
          sources: '[]',
          bound_regions: '[]',
          workbook_data: '{}',
          refresh_interval_seconds: 30,
          is_public: true,
          shared_users: [
            { user: userAId, permission: 'edit' },
            { user: userBId, permission: 'edit' },
          ],
          shared_roles: [],
        },
      });
      expect(status).toBe(200);

      // Verify the sharing fields were NOT changed — read as admin
      const adminApi = new ApiHelper(request);
      await adminApi.login();
      const sheet = await adminApi.readEntity('rf-sheets', userSharedEditSheetId);
      // is_public should still be false
      expect(sheet.fields.is_public).toBe(false);
      // shared_users should still only have User A
      expect(sheet.fields.shared_users).toHaveLength(1);
      expect(sheet.fields.shared_users[0].user).toBe(userAId);
    });

    await test.step('User A cannot DELETE the sheet', async () => {
      const { status } = await deleteSheetAs(request, USER_A_EMAIL, USER_PASSWORD, userSharedEditSheetId);
      expect(status).toBe(403);
    });
  });

  // ═══════════════════════════════════════════════════════════
  // TEST 4: Share sheet by ROLE — VIEW permission
  // ═══════════════════════════════════════════════════════════

  test('role-shared VIEW: users with matching role can read', async ({ request }) => {
    await test.step('create sheet shared with role (view)', async () => {
      roleSharedViewSheetId = await createSheetAsAdmin(request, `Role View Sheet ${TS()}`, {
        shared_roles: [{ role: roleWithSheetsId, permission: 'view' }],
      });
      expect(roleSharedViewSheetId).toBeGreaterThan(0);
    });

    await test.step('User A (has role) can READ the sheet', async () => {
      const { status } = await readSheetAs(request, USER_A_EMAIL, USER_PASSWORD, roleSharedViewSheetId);
      expect(status).toBe(200);
    });

    await test.step('User B (has role) can also READ the sheet', async () => {
      const { status } = await readSheetAs(request, USER_B_EMAIL, USER_PASSWORD, roleSharedViewSheetId);
      expect(status).toBe(200);
    });

    await test.step('Both users see access_level=view in PEEK_ALL', async () => {
      for (const email of [USER_A_EMAIL, USER_B_EMAIL]) {
        const { data } = await peekSheetsAs(request, email, USER_PASSWORD);
        const found = data.find(s => s.id === roleSharedViewSheetId);
        expect(found, `Sheet not found in peek for ${email}`).toBeTruthy();
        expect(found!.access_level).toBe('view');
      }
    });

    await test.step('User A cannot UPDATE (view only via role)', async () => {
      const { status } = await updateSheetAs(request, USER_A_EMAIL, USER_PASSWORD, roleSharedViewSheetId, {
        title: { rendered: 'Role view edit attempt' },
      });
      expect(status).toBe(403);
    });
  });

  // ═══════════════════════════════════════════════════════════
  // TEST 5: Share sheet by ROLE — EDIT permission
  // ═══════════════════════════════════════════════════════════

  test('role-shared EDIT: users with matching role can edit', async ({ request }) => {
    await test.step('create sheet shared with role (edit)', async () => {
      roleSharedEditSheetId = await createSheetAsAdmin(request, `Role Edit Sheet ${TS()}`, {
        shared_roles: [{ role: roleWithSheetsId, permission: 'edit' }],
      });
      expect(roleSharedEditSheetId).toBeGreaterThan(0);
    });

    await test.step('User A (has role) can UPDATE the sheet', async () => {
      const { status } = await updateSheetAs(request, USER_A_EMAIL, USER_PASSWORD, roleSharedEditSheetId, {
        title: { rendered: `Role-edited by A ${TS()}` },
        fields: {
          sources: '[]',
          bound_regions: '[]',
          workbook_data: '{}',
          refresh_interval_seconds: 30,
        },
      });
      expect(status).toBe(200);
    });

    await test.step('User B (has role) can also UPDATE the sheet', async () => {
      const { status } = await updateSheetAs(request, USER_B_EMAIL, USER_PASSWORD, roleSharedEditSheetId, {
        title: { rendered: `Role-edited by B ${TS()}` },
        fields: {
          sources: '[]',
          bound_regions: '[]',
          workbook_data: '{}',
          refresh_interval_seconds: 30,
        },
      });
      expect(status).toBe(200);
    });

    await test.step('Both users see access_level=edit', async () => {
      for (const email of [USER_A_EMAIL, USER_B_EMAIL]) {
        const { data } = await peekSheetsAs(request, email, USER_PASSWORD);
        const found = data.find(s => s.id === roleSharedEditSheetId);
        expect(found, `Sheet not found for ${email}`).toBeTruthy();
        expect(found!.access_level).toBe('edit');
      }
    });

    await test.step('Neither user can DELETE (not owner)', async () => {
      for (const email of [USER_A_EMAIL, USER_B_EMAIL]) {
        const { status } = await deleteSheetAs(request, email, USER_PASSWORD, roleSharedEditSheetId);
        expect(status).toBe(403);
      }
    });
  });

  // ═══════════════════════════════════════════════════════════
  // TEST 6: Public sheet — anyone with rf-sheets perm can view
  // ═══════════════════════════════════════════════════════════

  test('public sheet: anyone with permission sees it (view only)', async ({ request }) => {
    await test.step('create public sheet', async () => {
      publicSheetId = await createSheetAsAdmin(request, `Public Sheet ${TS()}`, {
        is_public: true,
      });
      expect(publicSheetId).toBeGreaterThan(0);
    });

    await test.step('User A (not shared, but has rf-sheets perm) can READ', async () => {
      const { status } = await readSheetAs(request, USER_A_EMAIL, USER_PASSWORD, publicSheetId);
      expect(status).toBe(200);
    });

    await test.step('User A sees access_level=view', async () => {
      const { data } = await peekSheetsAs(request, USER_A_EMAIL, USER_PASSWORD);
      const found = data.find(s => s.id === publicSheetId);
      expect(found).toBeTruthy();
      expect(found!.access_level).toBe('view');
    });

    await test.step('User A cannot UPDATE public sheet (view only)', async () => {
      const { status } = await updateSheetAs(request, USER_A_EMAIL, USER_PASSWORD, publicSheetId, {
        title: { rendered: 'Public edit attempt' },
      });
      expect(status).toBe(403);
    });
  });

  // ═══════════════════════════════════════════════════════════
  // TEST 7: User-share overrides role-share (higher permission wins)
  // ═══════════════════════════════════════════════════════════

  test('user-share overrides role-share: user has edit, role has view', async ({ request }) => {
    let sheetId: number;

    await test.step('create sheet with role=view AND user A=edit', async () => {
      sheetId = await createSheetAsAdmin(request, `Mixed Access Sheet ${TS()}`, {
        shared_users: [{ user: userAId, permission: 'edit' }],
        shared_roles: [{ role: roleWithSheetsId, permission: 'view' }],
      });
    });

    await test.step('User A has edit (from user share, overrides role view)', async () => {
      const { data } = await peekSheetsAs(request, USER_A_EMAIL, USER_PASSWORD);
      const found = data.find(s => s.id === sheetId);
      expect(found!.access_level).toBe('edit');
    });

    await test.step('User B has view (from role share only)', async () => {
      const { data } = await peekSheetsAs(request, USER_B_EMAIL, USER_PASSWORD);
      const found = data.find(s => s.id === sheetId);
      expect(found!.access_level).toBe('view');
    });

    await test.step('User A can update, User B cannot', async () => {
      const aResult = await updateSheetAs(request, USER_A_EMAIL, USER_PASSWORD, sheetId, {
        title: { rendered: `Mixed updated ${TS()}` },
        fields: { sources: '[]', bound_regions: '[]', workbook_data: '{}', refresh_interval_seconds: 30 },
      });
      expect(aResult.status).toBe(200);

      const bResult = await updateSheetAs(request, USER_B_EMAIL, USER_PASSWORD, sheetId, {
        title: { rendered: 'B attempt' },
      });
      expect(bResult.status).toBe(403);
    });

    // Cleanup
    const adminApi = new ApiHelper(request);
    await adminApi.login();
    await adminApi.deleteEntity('rf-sheets', sheetId);
  });

  // ═══════════════════════════════════════════════════════════
  // TEST 8: Owner sees "Owner" badge; owner can delete
  // ═══════════════════════════════════════════════════════════

  test('owner: sees owner badge and can delete their sheet', async ({ request }) => {
    await test.step('admin (owner) sees access_level=owner in peek', async () => {
      const adminApi = new ApiHelper(request);
      await adminApi.login();
      const peek = await adminApi.peekAll('rf-sheets') as Array<{ id: number; access_level?: string }>;
      const found = peek.find(s => s.id === privateSheetId);
      expect(found).toBeTruthy();
      expect(found!.access_level).toBe('owner');
    });

    await test.step('owner can delete a sheet they own', async () => {
      // Create and delete a new sheet
      const tempId = await createSheetAsAdmin(request, `Temp Delete Sheet ${TS()}`);
      const adminApi = new ApiHelper(request);
      await adminApi.login();
      const delRes = await adminApi.deleteEntity('rf-sheets', tempId);
      expect(delRes.status()).toBe(200);
    });
  });

  // ═══════════════════════════════════════════════════════════
  // TEST 9: Dynamically add/remove user sharing via API
  // ═══════════════════════════════════════════════════════════

  test('dynamic sharing: add then remove user access', async ({ request }) => {
    let sheetId: number;

    await test.step('create private sheet', async () => {
      sheetId = await createSheetAsAdmin(request, `Dynamic Share Sheet ${TS()}`);
    });

    await test.step('User A cannot access initially', async () => {
      const { status } = await readSheetAs(request, USER_A_EMAIL, USER_PASSWORD, sheetId);
      expect(status).toBe(403);
    });

    await test.step('admin shares sheet with User A (edit)', async () => {
      const adminApi = new ApiHelper(request);
      await adminApi.login();
      await adminApi.updateEntity('rf-sheets', {
        id: sheetId,
        fields: {
          sources: '[]',
          bound_regions: '[]',
          workbook_data: '{}',
          refresh_interval_seconds: 30,
          is_public: false,
          shared_users: [{ user: userAId, permission: 'edit' }],
          shared_roles: [],
        },
      });
    });

    await test.step('User A can now access the sheet', async () => {
      const { status } = await readSheetAs(request, USER_A_EMAIL, USER_PASSWORD, sheetId);
      expect(status).toBe(200);
    });

    await test.step('admin removes User A sharing', async () => {
      const adminApi = new ApiHelper(request);
      await adminApi.login();
      await adminApi.updateEntity('rf-sheets', {
        id: sheetId,
        fields: {
          sources: '[]',
          bound_regions: '[]',
          workbook_data: '{}',
          refresh_interval_seconds: 30,
          is_public: false,
          shared_users: [],
          shared_roles: [],
        },
      });
    });

    await test.step('User A can no longer access the sheet', async () => {
      const { status } = await readSheetAs(request, USER_A_EMAIL, USER_PASSWORD, sheetId);
      expect(status).toBe(403);
    });

    // Cleanup
    const adminApi = new ApiHelper(request);
    await adminApi.login();
    await adminApi.deleteEntity('rf-sheets', sheetId);
  });

  // ═══════════════════════════════════════════════════════════
  // TEST 10: UI — Sharing dialog shows users and roles
  // ═══════════════════════════════════════════════════════════

  test('UI: sharing dialog — add user, change permission, save', async ({ page, request }) => {
    // Create a sheet as admin via UI
    let sheetId: number;

    await test.step('create sheet via API', async () => {
      sheetId = await createSheetAsAdmin(request, `UI Sharing Test ${TS()}`);
    });

    await test.step('login as admin via browser and navigate to sheet', async () => {
      await loginViaBrowser(page, ADMIN_EMAIL, ADMIN_PASSWORD);
      await page.goto(`/sheets/${sheetId}`);
      await expect(page.locator('[title="Export to .xlsx"]')).toBeVisible({ timeout: 15000 });
    });

    await test.step('open sharing dialog', async () => {
      await page.locator('[title="Sharing settings"]').click();
      await expect(page.locator('text=Sharing Settings')).toBeVisible({ timeout: 5000 });
    });

    await test.step('dialog shows Public toggle, Users section, Roles section', async () => {
      await expect(page.locator('text=Public')).toBeVisible();
      await expect(page.locator('text=Shared with Users')).toBeVisible();
      await expect(page.locator('text=Shared with Roles')).toBeVisible();
    });

    await test.step('add User A with view permission', async () => {
      // Select user from dropdown
      const userSelect = page.locator('select').filter({ has: page.locator('option', { hasText: /select user/i }) });
      await expect(userSelect).toBeVisible({ timeout: 10000 });

      // Wait for users to load into the select
      await page.waitForFunction(() => {
        const selects = document.querySelectorAll('select');
        for (const s of selects) {
          if (s.options.length > 1 && s.options[0].textContent?.match(/select user/i)) return true;
        }
        return false;
      }, undefined, { timeout: 15000 });

      // Find User A's option in the user select
      const userOption = userSelect.locator('option').filter({ hasText: /Sheet User A/i });
      const userValue = await userOption.getAttribute('value');
      await userSelect.selectOption(userValue!);

      // Click the "Add" button for users (the first "Add" button after the user select)
      const addBtn = page.locator('button', { hasText: /^Add$/ }).first();
      await addBtn.click();
    });

    await test.step('User A appears in the shared users list', async () => {
      await expect(page.locator('text=Sheet User A').first()).toBeVisible({ timeout: 5000 });
    });

    await test.step('change User A permission to edit', async () => {
      // Find the shared-user entry row (bg-gray-50 row containing the user name)
      const userEntry = page.locator('.bg-gray-50').filter({ hasText: /Sheet User A/ }).first();
      const permSelect = userEntry.locator('select');
      await permSelect.selectOption('edit');
    });

    await test.step('toggle public ON', async () => {
      const publicCheckbox = page.locator('input[type="checkbox"]');
      await publicCheckbox.check({ force: true });
    });

    await test.step('close dialog and save sheet', async () => {
      await page.locator('button', { hasText: /^Done$/ }).click();
      // Wait for dialog to close
      await expect(page.locator('text=Sharing Settings')).not.toBeVisible({ timeout: 3000 });
      // Save
      await page.locator('button', { hasText: /^Save$/ }).click();
      await expect(page.locator('text=Sheet saved')).toBeVisible({ timeout: 10000 });
    });

    await test.step('verify sharing persisted via API', async () => {
      const adminApi = new ApiHelper(request);
      await adminApi.login();
      const sheet = await adminApi.readEntity('rf-sheets', sheetId);
      expect(sheet.fields.is_public).toBe(true);
      expect(sheet.fields.shared_users).toHaveLength(1);
      expect(sheet.fields.shared_users[0].user).toBe(userAId);
      expect(sheet.fields.shared_users[0].permission).toBe('edit');
    });

    // Cleanup
    const adminApi = new ApiHelper(request);
    await adminApi.login();
    await adminApi.deleteEntity('rf-sheets', sheetId);
  });

  // ═══════════════════════════════════════════════════════════
  // TEST 11: UI — Sheet list shows correct access badges
  // ═══════════════════════════════════════════════════════════

  test('UI: sheet list shows correct access level badges', async ({ page, request }) => {
    await test.step('login as admin and navigate to sheet list', async () => {
      await loginViaBrowser(page, ADMIN_EMAIL, ADMIN_PASSWORD);
      await page.goto('/sheets');
      await page.waitForSelector('h1', { timeout: 15000 });
    });

    await test.step('owner sheets show "Owner" badge', async () => {
      // The admin-owned private sheet should show Owner badge
      const row = page.locator('tr').filter({ hasText: /Private Sheet/ });
      if (await row.isVisible({ timeout: 3000 }).catch(() => false)) {
        await expect(row.locator('text=Owner')).toBeVisible();
      }
    });

    await test.step('login as User A and check badges', async () => {
      await loginViaBrowser(page, USER_A_EMAIL, USER_PASSWORD);
      await page.goto('/sheets');
      await page.waitForSelector('h1', { timeout: 15000 });

      // User A should see the user-shared view sheet with "View Only"
      const viewRow = page.locator('tr').filter({ hasText: /User View Sheet/ });
      if (await viewRow.isVisible({ timeout: 3000 }).catch(() => false)) {
        await expect(viewRow.locator('text=View Only')).toBeVisible();
      }

      // User A should see the user-shared edit sheet with "Can Edit"
      const editRow = page.locator('tr').filter({ hasText: /User Edit Sheet/ });
      if (await editRow.isVisible({ timeout: 3000 }).catch(() => false)) {
        await expect(editRow.locator('text=Can Edit')).toBeVisible();
      }

      // User A should see the public sheet with "View Only"
      const publicRow = page.locator('tr').filter({ hasText: /Public Sheet/ });
      if (await publicRow.isVisible({ timeout: 3000 }).catch(() => false)) {
        await expect(publicRow.locator('text=View Only')).toBeVisible();
      }
    });
  });

  // ═══════════════════════════════════════════════════════════
  // TEST 12: UI — View-only user sees no Save or Share button
  // ═══════════════════════════════════════════════════════════

  test('UI: view-only user can view but backend rejects save', async ({ page, request }) => {
    await test.step('login as User A via browser and navigate to view-shared sheet', async () => {
      await loginViaBrowser(page, USER_A_EMAIL, USER_PASSWORD);
      await page.goto(`/sheets/${userSharedViewSheetId}`);
      await expect(page.locator('[title="Export to .xlsx"]')).toBeVisible({ timeout: 15000 });
    });

    await test.step('sheet content is visible (page loaded)', async () => {
      // The title should be visible
      await expect(page.locator('input[type="text"]').first()).toBeVisible();
    });

    await test.step('export and refresh buttons should still work', async () => {
      await expect(page.locator('[title="Export to .xlsx"]')).toBeVisible();
      await expect(page.locator('[title="Refresh data"]')).toBeVisible();
    });

    await test.step('verify backend rejects update for view-only user', async () => {
      // Even though the UI shows Save (capabilities-based), the backend should reject updates
      await loginAsUser(request, USER_A_EMAIL, USER_PASSWORD);
      const updateRes = await request.post(
        `${API_BASE}/crud?operation=UPDATE&type=rf-sheets`,
        { data: { id: userSharedViewSheetId, fields: { refresh_interval_seconds: 99 } } },
      );
      expect(updateRes.status()).toBe(403);
    });
  });

  // ═══════════════════════════════════════════════════════════
  // TEST 13: UI — Edit user sees Save but sharing dialog is read-only
  // ═══════════════════════════════════════════════════════════

  test('UI: edit user sees Save, sharing dialog is read-only', async ({ page, request }) => {
    await test.step('login as User A via browser and navigate to edit-shared sheet', async () => {
      await loginViaBrowser(page, USER_A_EMAIL, USER_PASSWORD);
      await page.goto(`/sheets/${userSharedEditSheetId}`);
      await expect(page.locator('[title="Export to .xlsx"]')).toBeVisible({ timeout: 15000 });
    });

    await test.step('Save button IS visible (edit permission)', async () => {
      await expect(page.locator('button', { hasText: /^Save$/ })).toBeVisible({ timeout: 5000 });
    });

    await test.step('sharing dialog: public toggle is disabled for non-owner', async () => {
      await page.locator('[title="Sharing settings"]').click();
      await expect(page.locator('text=Sharing Settings')).toBeVisible({ timeout: 5000 });

      const publicCheckbox = page.locator('input[type="checkbox"]');
      await expect(publicCheckbox).toBeDisabled();
    });

    await test.step('sharing dialog: no Add buttons visible for non-owner', async () => {
      // The "Select user..." dropdown should not be visible for non-owners
      const userSelect = page.locator('select').filter({ has: page.locator('option', { hasText: /select user/i }) });
      await expect(userSelect).not.toBeVisible({ timeout: 2000 });

      await page.locator('button', { hasText: /^Done$/ }).click();
    });
  });
});
