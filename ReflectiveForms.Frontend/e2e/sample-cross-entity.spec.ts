import { test, expect } from './helpers';

/**
 * Cross-Entity Relationship & Integration E2E Tests
 *
 * Verifies that entities can reference each other via Relation fields,
 * tests multi-entity workflows, and validates that deleting a related
 * entity doesn't break the referencing entity.
 *
 * Scenario: Create a team member → create a product referencing that
 * team member as product manager → verify relation data → update the
 * relation → delete team member → verify product still loads.
 */

const TS = () => Date.now().toString(36);

test.describe('Cross-Entity Relations', () => {
  let teamMemberId: number;
  let productId: number;

  test.describe.configure({ mode: 'serial' });

  test.afterAll(async ({ request }) => {
    const { ApiHelper } = await import('./helpers');
    const api = new ApiHelper(request);
    await api.login();
    await api.deleteAll('product');
    await api.deleteAll('team-member');
  });

  test('setup: create a team member via API', async ({ api }) => {
    await api.deleteAll('team-member');
    await api.deleteAll('product');

    const result = await api.createEntity('team-member', {
      title: { rendered: `PM Alice ${TS()}` },
      fields: {
        email: 'alice@company.com',
        department: 'product',
        job_title: 'Product Manager',
        years_of_experience: 10,
        performance_score: 9,
        is_remote: true,
        hire_date: '20220101',
        salary: 150000,
        emergency_contacts: [
          { contact_name: 'Bob', relationship: 'friend', phone: '+1 555-0000', email: 'bob@test.com' },
        ],
        social_links: [],
      },
    });
    teamMemberId = result.id;
    expect(teamMemberId).toBeGreaterThan(0);
  });

  test('create product with Relation to team member via UI', async ({ page, ui, api }) => {
    await ui.gotoNewEntity('product');

    await ui.fillTitle(`Relation Test Product ${TS()}`);
    await ui.fillMedia('Primary Product Image');
    await ui.fillTextArea('Short Description', 'Testing relation field.');
    await ui.selectOption('Product Category', 'electronics');
    await ui.fillNumber('Base Price (USD)', '99');
    await ui.setCheckbox('Published', true);

    // Add required variant (pre-populated from min_items=1)
    await ui.fillTextField('Variant Name', 'Default');
    await ui.fillTextField('SKU', `REL-${TS()}`);
    await ui.fillNumber('Price (USD)', '99');
    await ui.fillNumber('Stock Quantity', '10');

    // Product Manager Relation — select the team member (SearchableSelect)
    await ui.selectSearchableOption('Product Manager', /alice/i);

    await ui.clickSaveNow();
    await ui.waitForSave();

    const entities = await api.peekAll('product');
    const product = entities.find(e => (e.title ?? e.name ?? '').includes('Relation Test'));
    expect(product).toBeDefined();
    productId = product!.id;
  });

  test('verify relation was saved', async ({ api }) => {
    const entity = await api.readEntity('product', productId);
    // The product manager field should be set to the team member ID
    expect(entity.fields.product_manager).toBe(teamMemberId);
  });

  test('product still loads in UI after creation', async ({ page, ui }) => {
    await ui.gotoEditEntity('product', productId);
    await expect(page.locator('h1')).toContainText('Edit Product');
    const title = await ui.getTitle();
    expect(title).toContain('Relation Test');
  });

  test('verify both entities appear in their list pages', async ({ page, ui }) => {
    // Team member list
    await ui.gotoEntityList('team-member');
    await expect(page.locator('a', { hasText: /PM Alice/ })).toBeVisible();

    // Product list
    await ui.gotoEntityList('product');
    await expect(page.locator('a', { hasText: /Relation Test/ })).toBeVisible();
  });

  test('delete team member, product should still load', async ({ api, page, ui }) => {
    // Delete the team member via API
    await api.deleteEntity('team-member', teamMemberId);

    // Verify team member is gone
    const teamMembers = await api.peekAll('team-member');
    expect(teamMembers.find(e => e.id === teamMemberId)).toBeUndefined();

    // Product should still be readable (isRelationEntityNotExistsOk = true)
    const product = await api.readEntity('product', productId);
    expect(product.id).toBe(productId);
    expect(product.fields.product_manager).toBe(teamMemberId); // ID still stored

    // Product should still load in UI
    await ui.gotoEditEntity('product', productId);
    await expect(page.locator('h1')).toContainText('Edit Product');
  });

  test('cleanup: delete product', async ({ api }) => {
    await api.deleteEntity('product', productId);
    const products = await api.peekAll('product');
    expect(products.find(e => e.id === productId)).toBeUndefined();
  });
});

test.describe('Multi-Entity List Integrity', () => {
  test.describe.configure({ mode: 'serial' });

  test('create 3 entities, verify list count, delete one, recount', async ({ api, ui }) => {
    await api.deleteAll('blog-post');

    // Create 3 blog posts
    const ids: number[] = [];
    for (let i = 1; i <= 3; i++) {
      const result = await api.createEntity('blog-post', {
        title: { rendered: `Integrity Post ${i} ${TS()}` },
        fields: {
          status: 'draft',
          is_featured: false,
          allow_comments: true,
          reading_time_minutes: i * 3,
          slug: `integrity-${i}-${TS()}`,
          content: '<p>test content</p>',
          excerpt: '',
          featured_image: '',
          seo_metadata: { meta_title: '', meta_description: '', meta_keywords: '', canonical_url: '' },
          external_links: [],
          publication_year: '',
          scheduled_date: '',
        },
      });
      ids.push(result.id);
    }

    // Verify list count via API
    let allPosts = await api.peekAll('blog-post');
    expect(allPosts.length).toBe(3);

    // Verify list page shows 3 rows
    await ui.gotoEntityList('blog-post');
    expect(await ui.entityRowCount()).toBe(3);

    // Delete middle post
    await api.deleteEntity('blog-post', ids[1]);

    // Verify 2 remain
    allPosts = await api.peekAll('blog-post');
    expect(allPosts.length).toBe(2);
    expect(allPosts.find(e => e.id === ids[1])).toBeUndefined();

    // List page after refresh
    await ui.gotoEntityList('blog-post');
    expect(await ui.entityRowCount()).toBe(2);

    // Cleanup
    await api.deleteAll('blog-post');
  });
});
