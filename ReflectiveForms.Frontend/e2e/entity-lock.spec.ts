import { test, expect } from './helpers';

const API_BASE = 'http://localhost:9000/rf/api';

/**
 * E2E tests for Entity Locking functionality.
 * Lock conflict tests use Playwright route interception to simulate a second user,
 * as creating a real second user is too slow due to post-create hooks.
 */
test.describe('Entity Locking', () => {
  test.describe.configure({ mode: 'serial' });

  const ENTITY = 'team-member';
  let entityId: number;

  test('setup — create test entity for locking', async ({ api }) => {
    const entity = await api.createEntity(ENTITY, {
      title: { rendered: `Lock Test ${Date.now()}` },
      fields: {
        job_title: 'Lock Tester',
        email: 'lock@test.com',
        performance_score: 5,
        salary: 50000,
        hire_date: '20260101',
        years_of_experience: 1,
        avatar: '',
        bio: '',
        is_remote: false,
        department: 'engineering',
        office_address: { street: '1 Lock St', city: 'Locktown', postal_code: '00001' },
        emergency_contacts: [{ contact_name: 'EC', relationship: 'friend', phone: '555-0000', email: '' }],
        social_links: [],
        favorite_blog_post: -1,
      },
    });
    entityId = entity.id;
    expect(entityId).toBeGreaterThan(0);
  });

  test('should acquire lock when editing entity', async ({ ui }) => {
    await ui.gotoEditEntity(ENTITY, entityId);

    // Form should be visible and editable (no lock warning)
    await expect(ui.page.locator('form')).toBeVisible();

    // Lock warning banner should NOT be visible
    const lockBanner = ui.page.locator('text=This entity is locked');
    await expect(lockBanner).not.toBeVisible({ timeout: 3000 });

    // Title input should be enabled
    const titleInput = ui.page.locator('input[name="title.rendered"]');
    await expect(titleInput).toBeEnabled();
  });

  test('should show lock warning when lock acquisition fails', async ({ ui, page }) => {
    // Intercept the lock API call and return a 409 Conflict to simulate another user holding the lock
    await page.route('**/entity_lock_control*try_lock*', route =>
      route.fulfill({
        status: 409,
        contentType: 'application/json',
        body: JSON.stringify({ detail: 'Lock-owning user is owned by another user.' }),
      }),
    );

    await ui.gotoEditEntity(ENTITY, entityId);

    // Lock warning banner should be visible
    const lockBanner = page.locator('text=This entity is locked');
    await expect(lockBanner).toBeVisible({ timeout: 10000 });

    // Form should be disabled via fieldset
    const fieldset = page.locator('fieldset[disabled]');
    await expect(fieldset).toBeVisible();

    // Remove the route override
    await page.unroute('**/entity_lock_control*try_lock*');
  });

  test('should release lock when leaving page', async ({ ui, page }) => {
    // Navigate to edit page (acquires lock)
    await ui.gotoEditEntity(ENTITY, entityId);
    await expect(page.locator('form')).toBeVisible();

    // Navigate away (should release lock)
    await ui.gotoDashboard();

    // Wait for lock release
    await page.waitForTimeout(2000);

    // Navigate back — should not see lock warning
    await ui.gotoEditEntity(ENTITY, entityId);
    const lockBanner = page.locator('text=This entity is locked');
    await expect(lockBanner).not.toBeVisible({ timeout: 5000 });
  });

  test('should disable form fields when lock fails', async ({ ui, page }) => {
    // Intercept lock API to simulate conflict
    await page.route('**/entity_lock_control*try_lock*', route =>
      route.fulfill({
        status: 409,
        contentType: 'application/json',
        body: JSON.stringify({ detail: 'Lock-owning user is owned by another user.' }),
      }),
    );

    await ui.gotoEditEntity(ENTITY, entityId);

    // Lock warning should be visible
    await expect(page.locator('text=This entity is locked')).toBeVisible({ timeout: 10000 });

    // Fieldset should be disabled
    const disabledFieldset = page.locator('fieldset[disabled]');
    await expect(disabledFieldset).toBeVisible();

    // Title input should be disabled
    const titleInput = page.locator('input[name="title.rendered"]');
    await expect(titleInput).toBeDisabled();

    // Remove the route override
    await page.unroute('**/entity_lock_control*try_lock*');
  });

  test('cleanup — delete test entity', async ({ api }) => {
    if (entityId) {
      // Unlock then delete
      await api.request.post(
        `${API_BASE}/entity_lock_control?type=${ENTITY}&id=${entityId}&operation=try_unlock`,
        { data: {} },
      ).catch(() => {});
      await api.deleteEntity(ENTITY, entityId).catch(() => {});
    }
  });
});
