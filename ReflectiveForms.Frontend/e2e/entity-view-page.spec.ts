import { test, expect } from './helpers';

/**
 * E2E tests for the read-only entity view page.
 */
test.describe('Entity View Page', () => {
  test.describe.configure({ mode: 'serial' });

  const ENTITY = 'team-member';
  let entityId: number;

  // Valid field data for team-member (satisfies all sanity checks)
  const validFields = {
    job_title: 'Senior Engineer',
    email: 'jane@company.com',
    is_remote: true,
    department: 'engineering',
    bio: 'A passionate engineer working on distributed systems.',
    years_of_experience: 5,
    performance_score: 8,
    salary: 120000,
    hire_date: '20220615',
    avatar: '',
    office_address: { street: '456 Tech Ave', city: 'San Francisco', postal_code: '94105' },
    emergency_contacts: [{ contact_name: 'Bob Smith', relationship: 'friend', phone: '555-1234', email: '' }],
    social_links: [],
    favorite_blog_post: -1,
  };

  test('setup — create test entity', async ({ api }) => {
    const entity = await api.createEntity(ENTITY, {
      title: { rendered: `View Test - Jane Smith ${Date.now()}` },
      fields: validFields,
    });
    entityId = entity.id;
    expect(entityId).toBeGreaterThan(0);
  });

  test('navigates to view page and displays title', async ({ ui }) => {
    await ui.page.goto(`/rf/app/entities-view/${ENTITY}?id=${entityId}`);
    await ui.page.waitForSelector('h1', { timeout: 15000 });

    await expect(ui.page.locator('h1')).toContainText('View Test - Jane Smith');
  });

  test('displays entity ID', async ({ ui }) => {
    await ui.page.goto(`/rf/app/entities-view/${ENTITY}?id=${entityId}`);
    await ui.page.waitForSelector('h1', { timeout: 15000 });

    await expect(ui.page.locator('text=ID:')).toBeVisible();
  });

  test('displays text field values', async ({ ui }) => {
    await ui.page.goto(`/rf/app/entities-view/${ENTITY}?id=${entityId}`);
    await ui.page.waitForSelector('h1', { timeout: 15000 });

    await expect(ui.page.locator('text=Senior Engineer')).toBeVisible();
  });

  test('displays email as mailto link', async ({ ui }) => {
    await ui.page.goto(`/rf/app/entities-view/${ENTITY}?id=${entityId}`);
    await ui.page.waitForSelector('h1', { timeout: 15000 });

    const emailLink = ui.page.locator('a[href="mailto:jane@company.com"]');
    await expect(emailLink).toBeVisible();
  });

  test('displays checkbox as Yes/No badge', async ({ ui }) => {
    await ui.page.goto(`/rf/app/entities-view/${ENTITY}?id=${entityId}`);
    await ui.page.waitForSelector('h1', { timeout: 15000 });

    // is_remote = true → "Yes"
    const remoteBadge = ui.page.locator('.field-view')
      .filter({ has: ui.page.locator('text=Remote Worker') })
      .locator('text=Yes');
    await expect(remoteBadge).toBeVisible();
  });

  test('displays select field with human-readable label', async ({ ui }) => {
    await ui.page.goto(`/rf/app/entities-view/${ENTITY}?id=${entityId}`);
    await ui.page.waitForSelector('h1', { timeout: 15000 });

    // department = "engineering" → should display label
    const deptField = ui.page.locator('.field-view')
      .filter({ has: ui.page.locator('text=Department') });
    await expect(deptField).toBeVisible();
  });

  test('displays number value', async ({ ui }) => {
    await ui.page.goto(`/rf/app/entities-view/${ENTITY}?id=${entityId}`);
    await ui.page.waitForSelector('h1', { timeout: 15000 });

    // years_of_experience = 5, check in the specific field wrapper
    const yoeField = ui.page.locator('.field-view')
      .filter({ has: ui.page.locator('text=Years of Experience') });
    await expect(yoeField.locator('.font-mono')).toContainText('5');
  });

  test('displays text area content', async ({ ui }) => {
    await ui.page.goto(`/rf/app/entities-view/${ENTITY}?id=${entityId}`);
    await ui.page.waitForSelector('h1', { timeout: 15000 });

    await expect(ui.page.locator('text=passionate engineer')).toBeVisible();
  });

  test('has Edit button linking to edit page', async ({ ui }) => {
    await ui.page.goto(`/rf/app/entities-view/${ENTITY}?id=${entityId}`);
    await ui.page.waitForSelector('h1', { timeout: 15000 });

    const editLink = ui.page.locator('a[title="Edit"]');
    await expect(editLink).toBeVisible();
    const href = await editLink.getAttribute('href');
    expect(href).toContain(`/entities-admin/${ENTITY}?id=${entityId}`);
  });

  test('has Back to list link', async ({ ui }) => {
    await ui.page.goto(`/rf/app/entities-view/${ENTITY}?id=${entityId}`);
    await ui.page.waitForSelector('h1', { timeout: 15000 });

    const backLink = ui.page.locator('a[title="Back to list"]');
    await expect(backLink).toBeVisible();
    const href = await backLink.getAttribute('href');
    expect(href).toContain(`/entities/${ENTITY}`);
  });

  test('list page has View button linking to view page', async ({ ui }) => {
    await ui.gotoEntityList(ENTITY);

    const viewLink = ui.page.locator('a[title="View"]').first();
    await expect(viewLink).toBeVisible();
    const href = await viewLink.getAttribute('href');
    expect(href).toContain(`/entities-view/${ENTITY}?id=`);
  });

  test('shows "Not set" for empty fields', async ({ api, ui }) => {
    // Create an entity with minimal data
    const sparse = await api.createEntity(ENTITY, {
      title: { rendered: 'Sparse Entity' },
      fields: { ...validFields, job_title: 'Sparse', email: '', bio: '' },
    });

    await ui.page.goto(`/rf/app/entities-view/${ENTITY}?id=${sparse.id}`);
    await ui.page.waitForSelector('h1', { timeout: 15000 });

    // Should have "Not set" for empty fields
    const notSets = await ui.page.locator('text=Not set').count();
    expect(notSets).toBeGreaterThan(0);

    // Clean up
    await api.deleteEntity(ENTITY, sparse.id);
  });

  test('nested group fields do NOT have double card wrappers', async ({ api, ui }) => {
    // Create a non-remote entity so office_address group is visible
    const onSite = await api.createEntity(ENTITY, {
      title: { rendered: `OnSite View Test ${Date.now()}` },
      fields: { ...validFields, is_remote: false },
    });

    await ui.page.goto(`/rf/app/entities-view/${ENTITY}?id=${onSite.id}`);
    await ui.page.waitForSelector('h1', { timeout: 15000 });

    // office_address is a Group field — its children (street, city, postal_code)
    // should NOT have bg-white rounded-lg shadow-sm (no card-in-card)
    const streetField = ui.page.locator('.field-view.field-type-text')
      .filter({ hasText: '456 Tech Ave' });
    // The field-view itself should NOT contain a card wrapper
    const hasCard = await streetField.locator('.bg-white.rounded-lg.shadow-sm').count();
    expect(hasCard).toBe(0);

    await api.deleteEntity(ENTITY, onSite.id);
  });

  test('repeater items show structured headers', async ({ ui }) => {
    await ui.page.goto(`/rf/app/entities-view/${ENTITY}?id=${entityId}`);
    await ui.page.waitForSelector('h1', { timeout: 15000 });

    // emergency_contacts is a Repeater — items should have "Emergency Contacts #1"
    await expect(ui.page.locator('text=Emergency Contacts #1')).toBeVisible();
  });

  test('group fields use grid layout', async ({ api, ui }) => {
    // Create a non-remote entity so office_address group is visible (display condition: is_remote == false)
    const onSite = await api.createEntity(ENTITY, {
      title: { rendered: `Grid View Test ${Date.now()}` },
      fields: { ...validFields, is_remote: false },
    });

    await ui.page.goto(`/rf/app/entities-view/${ENTITY}?id=${onSite.id}`);
    await ui.page.waitForSelector('h1', { timeout: 15000 });

    // office_address group (render_style=Grid3) should have a grid container
    const gridContainer = ui.page.locator('.grid').filter({ hasText: '456 Tech Ave' });
    const count = await gridContainer.count();
    expect(count).toBeGreaterThan(0);

    await api.deleteEntity(ENTITY, onSite.id);
  });

  test('cleanup — delete test entity', async ({ api }) => {
    if (entityId) {
      await api.deleteEntity(ENTITY, entityId);
    }
  });
});
