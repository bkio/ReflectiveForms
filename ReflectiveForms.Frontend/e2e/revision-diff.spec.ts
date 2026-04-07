import { test, expect } from './helpers';

/** Helper: select a revision option in a searchable-select dropdown. */
async function selectRevision(
  page: import('@playwright/test').Page,
  selectorTestId: string,
  optionValue: string,
) {
  const selector = page.locator(`[data-testid="${selectorTestId}"]`);
  // Open dropdown
  await selector.locator('button[aria-haspopup="listbox"]').click();
  // Wait for the listbox to appear
  const listbox = selector.locator('[role="listbox"]');
  await expect(listbox).toBeVisible({ timeout: 5000 });
  // Click the option
  const option = listbox.locator(`[data-testid="revision-option-${optionValue}"]`);
  await expect(option).toBeVisible({ timeout: 5000 });
  await option.click();
}

test.describe('Revision Diff', () => {
  test.describe.configure({ mode: 'serial' });

  const ENTITY = 'team-member';
  let entityId: number;
  const uniqueSuffix = Date.now();

  // Create an entity and update it multiple times to generate revisions
  test.beforeAll(async ({ api }) => {
    // Create entity
    const created = await api.createEntity(ENTITY, {
      title: { rendered: `RevDiff Test ${uniqueSuffix}` },
      fields: {
        job_title: 'Engineer',
        work_email: 'revdiff@test.com',
        hire_date: '20260101',
        office_address: { street: '100 Main St', city: 'TestCity', postal_code: '10000' },
        emergency_contacts: [{ contact_name: 'Contact1', phone: '555-0001', relationship: 'friend' }],
      },
    });
    entityId = created.id;

    // First update — creates revision 1
    await api.updateEntity(ENTITY, {
      id: entityId,
      title: { rendered: `RevDiff Test ${uniqueSuffix} v2` },
      fields: {
        job_title: 'Senior Engineer',
        work_email: 'revdiff-v2@test.com',
        hire_date: '20260101',
        office_address: { street: '200 Main St', city: 'TestCity', postal_code: '20000' },
        emergency_contacts: [{ contact_name: 'Contact1', phone: '555-0001', relationship: 'friend' }],
      },
    });

    // Second update — creates revision 2
    await api.updateEntity(ENTITY, {
      id: entityId,
      title: { rendered: `RevDiff Test ${uniqueSuffix} v3` },
      fields: {
        job_title: 'Staff Engineer',
        work_email: 'revdiff-v3@test.com',
        hire_date: '20260201',
        office_address: { street: '300 Main St', city: 'NewCity', postal_code: '30000' },
        emergency_contacts: [{ contact_name: 'Contact2', phone: '555-0002', relationship: 'spouse' }],
      },
    });
  });

  test.afterAll(async ({ api }) => {
    if (entityId) {
      await api.deleteEntity(ENTITY, entityId).catch(() => {});
    }
  });

  // ---------------------------------------------------------------
  // Backend API tests
  // ---------------------------------------------------------------

  test('HISTORY API returns revisions for an updated entity', async ({ api }) => {
    const history = await api.getEntityHistory(ENTITY, entityId);
    expect(history.revisions_count).toBe(2);
    expect(history.revisions).toHaveLength(2);

    // Revision 1 should be the original state
    const rev1 = history.revisions.find(r => r.revision_number === 1);
    expect(rev1).toBeTruthy();
    expect(rev1!.modified_by_email).toBeTruthy();
    expect(rev1!.date).toBeTruthy();
    expect(rev1!.object).toBeTruthy();

    // Revision 2 should be the second state (after first update)
    const rev2 = history.revisions.find(r => r.revision_number === 2);
    expect(rev2).toBeTruthy();
    expect(rev2!.object).toBeTruthy();
  });

  test('HISTORY API returns empty revisions for a never-updated entity', async ({ api }) => {
    // Create a fresh entity without any updates
    const fresh = await api.createEntity(ENTITY, {
      title: { rendered: `RevDiff Fresh ${uniqueSuffix}` },
      fields: {
        job_title: 'Intern',
        work_email: 'fresh@test.com',
        hire_date: '20260101',
        office_address: { street: '1 Fresh St', city: 'FreshCity', postal_code: '00001' },
        emergency_contacts: [{ contact_name: 'Fresh Contact', phone: '555-9999', relationship: 'friend' }],
      },
    });

    const history = await api.getEntityHistory(ENTITY, fresh.id);
    expect(history.revisions_count).toBe(0);
    expect(history.revisions).toHaveLength(0);

    await api.deleteEntity(ENTITY, fresh.id);
  });

  test('HISTORY API revision objects contain correct field data', async ({ api }) => {
    const history = await api.getEntityHistory(ENTITY, entityId);

    // Revision 1 = original state before first update
    const rev1Obj = history.revisions.find(r => r.revision_number === 1)!.object as any;
    expect(rev1Obj.title.rendered).toBe(`RevDiff Test ${uniqueSuffix}`);
    expect(rev1Obj.fields.job_title).toBe('Engineer');

    // Revision 2 = state after first update (before second update)
    const rev2Obj = history.revisions.find(r => r.revision_number === 2)!.object as any;
    expect(rev2Obj.title.rendered).toBe(`RevDiff Test ${uniqueSuffix} v2`);
    expect(rev2Obj.fields.job_title).toBe('Senior Engineer');
  });

  // ---------------------------------------------------------------
  // Compare Revisions button visibility
  // ---------------------------------------------------------------

  test('Compare Revisions button is visible on view page when entity has revisions', async ({ ui }) => {
    await ui.gotoViewEntity(ENTITY, entityId);
    const btn = ui.page.locator('[data-testid="compare-revisions-button"]');
    await expect(btn).toBeVisible({ timeout: 10000 });
    await expect(btn).toHaveText(/Compare Revisions/);
  });

  test('Compare Revisions button is visible on edit page when entity has revisions', async ({ ui }) => {
    await ui.gotoEditEntity(ENTITY, entityId);
    const btn = ui.page.locator('[data-testid="compare-revisions-button"]');
    await expect(btn).toBeVisible({ timeout: 10000 });
  });

  test('Compare Revisions button is NOT visible on view page for entity without revisions', async ({ api, ui }) => {
    // Create a fresh entity
    const fresh = await api.createEntity(ENTITY, {
      title: { rendered: `RevDiff NoRev ${uniqueSuffix}` },
      fields: {
        job_title: 'Intern',
        work_email: 'norev@test.com',
        hire_date: '20260101',
        office_address: { street: '1 NoRev St', city: 'NoRevCity', postal_code: '00001' },
        emergency_contacts: [{ contact_name: 'NoRev', phone: '555-0000', relationship: 'friend' }],
      },
    });

    await ui.gotoViewEntity(ENTITY, fresh.id);
    // Wait for page to fully load
    await ui.page.waitForTimeout(2000);
    const btn = ui.page.locator('[data-testid="compare-revisions-button"]');
    await expect(btn).not.toBeVisible();

    await api.deleteEntity(ENTITY, fresh.id);
  });

  test('Compare Revisions button is NOT visible on the create (new) page', async ({ ui }) => {
    await ui.gotoNewEntity(ENTITY);
    // Wait for page to fully load
    await ui.page.waitForTimeout(2000);
    const btn = ui.page.locator('[data-testid="compare-revisions-button"]');
    await expect(btn).not.toBeVisible();
  });

  // ---------------------------------------------------------------
  // Navigation to revision diff page
  // ---------------------------------------------------------------

  test('clicking Compare Revisions button navigates to diff page', async ({ ui }) => {
    await ui.gotoViewEntity(ENTITY, entityId);
    const btn = ui.page.locator('[data-testid="compare-revisions-button"]');
    await expect(btn).toBeVisible({ timeout: 10000 });
    await btn.click();
    await ui.page.waitForURL(/entities-revisions/, { timeout: 10000 });
    await expect(ui.page.locator('h1')).toContainText('Compare Revisions');
  });

  // ---------------------------------------------------------------
  // Revision Diff Page
  // ---------------------------------------------------------------

  test('diff page shows both revision selectors', async ({ ui }) => {
    await ui.gotoRevisionDiff(ENTITY, entityId);
    const leftSelector = ui.page.locator('[data-testid="left-revision-selector"]');
    const rightSelector = ui.page.locator('[data-testid="right-revision-selector"]');
    await expect(leftSelector).toBeVisible({ timeout: 10000 });
    await expect(rightSelector).toBeVisible({ timeout: 10000 });
  });

  test('diff page shows correct revision options', async ({ ui }) => {
    await ui.gotoRevisionDiff(ENTITY, entityId);

    // Open left dropdown to see all options
    const leftSelector = ui.page.locator('[data-testid="left-revision-selector"]');
    await leftSelector.locator('button[aria-haspopup="listbox"]').click();
    const listbox = leftSelector.locator('[role="listbox"]');
    await expect(listbox).toBeVisible({ timeout: 10000 });

    // Should have Latest, Revision 2, Revision 1 options
    await expect(listbox.locator('[data-testid="revision-option-latest"]')).toBeVisible();
    await expect(listbox.locator('[data-testid="revision-option-2"]')).toBeVisible();
    await expect(listbox.locator('[data-testid="revision-option-1"]')).toBeVisible();
  });

  test('diff page revision options show date and email metadata', async ({ ui }) => {
    await ui.gotoRevisionDiff(ENTITY, entityId);

    // Open left dropdown
    const leftSelector = ui.page.locator('[data-testid="left-revision-selector"]');
    await leftSelector.locator('button[aria-haspopup="listbox"]').click();
    const listbox = leftSelector.locator('[role="listbox"]');
    await expect(listbox).toBeVisible({ timeout: 10000 });

    // Revision 1 should show modified_by_email
    const rev1 = listbox.locator('[data-testid="revision-option-1"]');
    await expect(rev1).toBeVisible();
    const rev1Text = await rev1.textContent();
    expect(rev1Text).toBeTruthy();
    // Date portion should exist
    expect(rev1Text!.length).toBeGreaterThan(10);
  });

  test('diff page auto-selects latest and previous revision by default', async ({ ui }) => {
    await ui.gotoRevisionDiff(ENTITY, entityId);

    // Wait for the selectors to load
    await expect(ui.page.locator('[data-testid="left-revision-selector"]')).toBeVisible({ timeout: 10000 });

    // Left trigger should show "Latest"
    const leftTrigger = ui.page.locator('[data-testid="left-revision-selector"] button[aria-haspopup="listbox"]');
    await expect(leftTrigger).toContainText('Latest', { timeout: 5000 });

    // Right trigger should show "Revision 2"
    const rightTrigger = ui.page.locator('[data-testid="right-revision-selector"] button[aria-haspopup="listbox"]');
    await expect(rightTrigger).toContainText('Revision 2', { timeout: 5000 });
  });

  test('diff page shows content for both selected revisions', async ({ ui }) => {
    await ui.gotoRevisionDiff(ENTITY, entityId);

    const leftContent = ui.page.locator('[data-testid="left-revision-content"]');
    const rightContent = ui.page.locator('[data-testid="right-revision-content"]');
    await expect(leftContent).toBeVisible({ timeout: 10000 });
    await expect(rightContent).toBeVisible({ timeout: 10000 });
  });

  test('diff page highlights changed fields', async ({ ui }) => {
    await ui.gotoRevisionDiff(ENTITY, entityId);

    // Wait for content to load
    await expect(ui.page.locator('[data-testid="left-revision-content"]')).toBeVisible({ timeout: 10000 });

    // There should be at least some changed fields highlighted
    const changedFields = ui.page.locator('[data-diff="changed"]');
    const changedCount = await changedFields.count();
    expect(changedCount).toBeGreaterThan(0);
  });

  test('diff page shows unchanged fields without highlight', async ({ ui }) => {
    await ui.gotoRevisionDiff(ENTITY, entityId);

    await expect(ui.page.locator('[data-testid="left-revision-content"]')).toBeVisible({ timeout: 10000 });

    // There should be both changed and unchanged fields
    const unchangedFields = ui.page.locator('[data-diff="unchanged"]');
    const unchangedCount = await unchangedFields.count();
    expect(unchangedCount).toBeGreaterThan(0);
  });

  // ---------------------------------------------------------------
  // Same revision error
  // ---------------------------------------------------------------

  test('selecting same revision on both sides shows error', async ({ ui }) => {
    await ui.gotoRevisionDiff(ENTITY, entityId);

    // Wait for selectors to load
    await expect(ui.page.locator('[data-testid="left-revision-selector"]')).toBeVisible({ timeout: 10000 });

    // Select "latest" on both sides
    await selectRevision(ui.page, 'left-revision-selector', 'latest');
    await selectRevision(ui.page, 'right-revision-selector', 'latest');

    // Error message should appear
    const error = ui.page.locator('[data-testid="same-revision-error"]');
    await expect(error).toBeVisible({ timeout: 5000 });
    await expect(error).toContainText('Cannot compare the same revision');
  });

  test('same revision error hides content panels', async ({ ui }) => {
    await ui.gotoRevisionDiff(ENTITY, entityId);

    // Wait for selectors
    await expect(ui.page.locator('[data-testid="left-revision-selector"]')).toBeVisible({ timeout: 10000 });

    // Select same revision on both sides
    await selectRevision(ui.page, 'left-revision-selector', '1');
    await selectRevision(ui.page, 'right-revision-selector', '1');

    // Error should be visible
    await expect(ui.page.locator('[data-testid="same-revision-error"]')).toBeVisible({ timeout: 5000 });

    // Content panels should NOT be visible
    await expect(ui.page.locator('[data-testid="left-revision-content"]')).not.toBeVisible();
    await expect(ui.page.locator('[data-testid="right-revision-content"]')).not.toBeVisible();
  });

  test('same revision error clears when different revisions selected', async ({ ui }) => {
    await ui.gotoRevisionDiff(ENTITY, entityId);

    // Wait for selectors
    await expect(ui.page.locator('[data-testid="left-revision-selector"]')).toBeVisible({ timeout: 10000 });

    // Select same revision
    await selectRevision(ui.page, 'left-revision-selector', '1');
    await selectRevision(ui.page, 'right-revision-selector', '1');

    // Error should appear
    await expect(ui.page.locator('[data-testid="same-revision-error"]')).toBeVisible({ timeout: 5000 });

    // Now select a different revision on the right side
    await selectRevision(ui.page, 'right-revision-selector', '2');

    // Error should disappear
    await expect(ui.page.locator('[data-testid="same-revision-error"]')).not.toBeVisible({ timeout: 5000 });

    // Content should reappear
    await expect(ui.page.locator('[data-testid="left-revision-content"]')).toBeVisible({ timeout: 5000 });
    await expect(ui.page.locator('[data-testid="right-revision-content"]')).toBeVisible({ timeout: 5000 });
  });

  // ---------------------------------------------------------------
  // Switching between different revisions
  // ---------------------------------------------------------------

  test('switching left revision updates displayed content', async ({ ui }) => {
    await ui.gotoRevisionDiff(ENTITY, entityId);

    // Wait for content
    await expect(ui.page.locator('[data-testid="left-revision-content"]')).toBeVisible({ timeout: 10000 });

    // Select revision 1 on left side
    await selectRevision(ui.page, 'left-revision-selector', '1');

    // Make sure right is on a different revision
    await selectRevision(ui.page, 'right-revision-selector', 'latest');

    // Left content should show the original title
    const leftContent = ui.page.locator('[data-testid="left-revision-content"]');
    await expect(leftContent).toBeVisible({ timeout: 5000 });
    await expect(leftContent).toContainText(`RevDiff Test ${uniqueSuffix}`);
  });

  test('switching right revision updates displayed content', async ({ ui }) => {
    await ui.gotoRevisionDiff(ENTITY, entityId);

    // Wait for content
    await expect(ui.page.locator('[data-testid="right-revision-content"]')).toBeVisible({ timeout: 10000 });

    // Set left to latest
    await selectRevision(ui.page, 'left-revision-selector', 'latest');

    // Select revision 1 on right side
    await selectRevision(ui.page, 'right-revision-selector', '1');

    // Right content should contain the original title
    const rightContent = ui.page.locator('[data-testid="right-revision-content"]');
    await expect(rightContent).toBeVisible({ timeout: 5000 });
    await expect(rightContent).toContainText(`RevDiff Test ${uniqueSuffix}`);
  });

  // ---------------------------------------------------------------
  // Diff page content accuracy
  // ---------------------------------------------------------------

  test('comparing latest vs revision 1 shows correct field differences', async ({ ui }) => {
    await ui.gotoRevisionDiff(ENTITY, entityId);

    await expect(ui.page.locator('[data-testid="left-revision-content"]')).toBeVisible({ timeout: 10000 });

    // Set left=latest, right=revision 1
    await selectRevision(ui.page, 'left-revision-selector', 'latest');
    await selectRevision(ui.page, 'right-revision-selector', '1');

    // Left (latest = v3) should show "Staff Engineer"
    const leftContent = ui.page.locator('[data-testid="left-revision-content"]');
    await expect(leftContent).toContainText('Staff Engineer');

    // Right (revision 1 = original) should show "Engineer" (not "Senior Engineer")
    const rightContent = ui.page.locator('[data-testid="right-revision-content"]');
    await expect(rightContent).toContainText('Engineer');
  });

  test('comparing revision 2 vs revision 1 shows correct differences', async ({ ui }) => {
    await ui.gotoRevisionDiff(ENTITY, entityId);

    await expect(ui.page.locator('[data-testid="left-revision-selector"]')).toBeVisible({ timeout: 10000 });

    // Set left=revision 2, right=revision 1
    await selectRevision(ui.page, 'left-revision-selector', '2');
    await selectRevision(ui.page, 'right-revision-selector', '1');

    // Left (revision 2 = after first update) should show "Senior Engineer"
    const leftContent = ui.page.locator('[data-testid="left-revision-content"]');
    await expect(leftContent).toContainText('Senior Engineer');

    // Right (revision 1 = original) should show the original job title
    const rightContent = ui.page.locator('[data-testid="right-revision-content"]');
    await expect(rightContent.locator('text=Engineer').first()).toBeVisible();
  });

  // ---------------------------------------------------------------
  // Back navigation
  // ---------------------------------------------------------------

  test('diff page has back link to entity view', async ({ ui }) => {
    await ui.gotoRevisionDiff(ENTITY, entityId);
    await expect(ui.page.locator('h1')).toContainText('Compare Revisions', { timeout: 10000 });

    // Click back arrow
    const backLink = ui.page.locator('a[title="Back to entity"]');
    await expect(backLink).toBeVisible({ timeout: 5000 });
    await backLink.click();
    await ui.page.waitForURL(/entities-view/, { timeout: 10000 });
  });

  // ---------------------------------------------------------------
  // Page header shows entity info
  // ---------------------------------------------------------------

  test('diff page header shows entity name and ID', async ({ ui }) => {
    await ui.gotoRevisionDiff(ENTITY, entityId);
    await expect(ui.page.locator('h1')).toContainText('Compare Revisions', { timeout: 10000 });

    // Should show entity readable name and ID
    const subtitle = ui.page.locator('p.text-sm.text-gray-500');
    await expect(subtitle).toContainText(`ID: ${entityId}`);
  });

  // ---------------------------------------------------------------
  // Edge cases
  // ---------------------------------------------------------------

  test('diff page for entity with exactly one revision shows Latest and one revision', async ({ api, ui }) => {
    // Create and update once
    const created = await api.createEntity(ENTITY, {
      title: { rendered: `RevDiff OneRev ${uniqueSuffix}` },
      fields: {
        job_title: 'One',
        work_email: 'one@test.com',
        hire_date: '20260101',
        office_address: { street: '1 One St', city: 'OneCity', postal_code: '11111' },
        emergency_contacts: [{ contact_name: 'OneContact', phone: '555-1111', relationship: 'friend' }],
      },
    });
    await api.updateEntity(ENTITY, {
      id: created.id,
      title: { rendered: `RevDiff OneRev ${uniqueSuffix} Updated` },
      fields: {
        job_title: 'Two',
        work_email: 'two@test.com',
        hire_date: '20260101',
        office_address: { street: '2 Two St', city: 'TwoCity', postal_code: '22222' },
        emergency_contacts: [{ contact_name: 'TwoContact', phone: '555-2222', relationship: 'friend' }],
      },
    });

    await ui.gotoRevisionDiff(ENTITY, created.id);

    // Open left dropdown to inspect options
    const leftSelector = ui.page.locator('[data-testid="left-revision-selector"]');
    await leftSelector.locator('button[aria-haspopup="listbox"]').click();
    const listbox = leftSelector.locator('[role="listbox"]');
    await expect(listbox).toBeVisible({ timeout: 10000 });

    // Should have exactly 2 options: Latest and Revision 1
    await expect(listbox.locator('[data-testid="revision-option-latest"]')).toBeVisible();
    await expect(listbox.locator('[data-testid="revision-option-1"]')).toBeVisible();

    // Revision 2 should not exist
    const rev2Option = listbox.locator('[data-testid="revision-option-2"]');
    expect(await rev2Option.count()).toBe(0);

    await api.deleteEntity(ENTITY, created.id);
  });

  test('Compare Revisions button from edit page navigates correctly', async ({ ui }) => {
    await ui.gotoEditEntity(ENTITY, entityId);
    const btn = ui.page.locator('[data-testid="compare-revisions-button"]');
    await expect(btn).toBeVisible({ timeout: 10000 });
    await btn.click();
    await ui.page.waitForURL(/entities-revisions/, { timeout: 10000 });
    await expect(ui.page.locator('h1')).toContainText('Compare Revisions');
  });
});
