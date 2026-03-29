import { test, expect } from './helpers';

/**
 * Cross-Entity Relation & Multi-Entity Integration Tests
 *
 * Tests complex inter-entity relationships:
 * - Product → Team Member (product manager relation)
 * - Event → Team Member (event coordinator relation)
 * - Team Member → Blog Post (favorite blog post relation)
 * - Cascading deletes / orphan handling
 * - Multi-entity workflow: create dependent entities, then reference them
 * - Verify relations survive update cycles
 * - Bulk operations across entity types
 */

const TS = () => Date.now().toString(36);

test.describe('Relations: Product Manager workflow', () => {
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

  test('create team member via API for relation target', async ({ api }) => {
    await api.deleteAll('team-member');
    await api.deleteAll('product');

    const result = await api.createEntity('team-member', {
      title: { rendered: `PM Bob ${TS()}` },
      fields: {
        email: 'bob@company.com', department: 'product',
        job_title: 'Senior PM', years_of_experience: 8,
        performance_score: 9, is_remote: true,
        hire_date: '20210115', salary: 140000,
        emergency_contacts: [{ contact_name: 'EC1', relationship: 'friend', phone: '+1 555-0001', email: 'ec@test.com' }], social_links: [],
        avatar: '', bio: '',
        office_address: { street: '100 Test Ave', city: 'Portland', state: 'OR', postal_code: '97201', country: 'US' },
        favorite_blog_post: -1,
      },
    });
    teamMemberId = result.id;
  });

  test('create product in UI and set Product Manager relation', async ({ page, ui, api }) => {
    await ui.gotoNewEntity('product');

    await ui.fillTitle(`Relation Product ${TS()}`);
    await ui.fillMedia('Primary Product Image');
    await ui.fillTextArea('Short Description', 'Testing PM relation.');
    await ui.selectOption('Product Category', 'electronics');
    await ui.fillNumber('Base Price (USD)', '199');
    await ui.setCheckbox('Published', true);

    // Add required variant (pre-populated from min_items=1)
    await ui.fillTextField('Variant Name', 'Base');
    await ui.fillTextField('SKU', `REL-PM-${TS()}`);
    await ui.fillNumber('Price (USD)', '199');
    await ui.fillNumber('Stock Quantity', '25');

    // Set Product Manager relation (SearchableSelect)
    await ui.selectSearchableOption('Product Manager', /bob/i);

    await ui.clickSaveNow();
    await ui.waitForSave();

    const entities = await api.peekAll('product');
    const created = entities.find(e => (e.title ?? e.name ?? '').includes('Relation Product'));
    expect(created).toBeDefined();
    productId = created!.id;
  });

  test('relation is correctly saved in backend', async ({ api }) => {
    const product = await api.readEntity('product', productId);
    expect(product.fields.product_manager).toBe(teamMemberId);
  });

  test('relation survives product update', async ({ page, ui, api }) => {
    await ui.gotoEditEntity('product', productId);

    // Change a non-relation field
    await ui.fillNumber('Base Price (USD)', '249');
    await ui.clickSaveNow();
    await ui.waitForSave();

    // Verify relation still intact
    const product = await api.readEntity('product', productId);
    expect(product.fields.product_manager).toBe(teamMemberId);
    expect(Number(product.fields.base_price)).toBe(249);
  });

  test('deleting relation target does not break product (isRelationEntityNotExistsOk)', async ({ api, page, ui }) => {
    // Delete the team member
    await api.deleteEntity('team-member', teamMemberId);

    // Product should still be accessible via API
    const product = await api.readEntity('product', productId);
    expect(product.id).toBe(productId);
    expect(product.fields.product_manager).toBe(teamMemberId);

    // Product should still render in UI
    await ui.gotoEditEntity('product', productId);
    await expect(page.locator('h1')).toContainText('Edit Product');
  });
});

test.describe('Relations: Event Coordinator workflow', () => {
  let teamMemberId: number;
  let eventId: number;

  test.describe.configure({ mode: 'serial' });

  test.afterAll(async ({ request }) => {
    const { ApiHelper } = await import('./helpers');
    const api = new ApiHelper(request);
    await api.login();
    await api.deleteAll('event');
    await api.deleteAll('team-member');
  });

  test('create team member for event coordinator', async ({ api }) => {
    await api.deleteAll('team-member');
    await api.deleteAll('event');

    const result = await api.createEntity('team-member', {
      title: { rendered: `Coordinator Carol ${TS()}` },
      fields: {
        email: 'carol@company.com', department: 'operations',
        job_title: 'Event Manager', years_of_experience: 5,
        performance_score: 8, is_remote: false,
        office_address: { street: '200 Main', city: 'Austin', state: 'TX', postal_code: '73301', country: 'US' },
        hire_date: '20220601', salary: 95000,
        emergency_contacts: [{ contact_name: 'EC1', relationship: 'friend', phone: '+1 555-0001', email: 'ec@test.com' }], social_links: [],
        avatar: '', bio: '', favorite_blog_post: -1,
      },
    });
    teamMemberId = result.id;
  });

  test('create event with coordinator relation via UI', async ({ page, ui, api }) => {
    await ui.gotoNewEntity('event');

    await ui.fillTitle(`Relation Event ${TS()}`);
    await ui.fillWysiwyg('Event Description', '<p>Coordinator relation test.</p>');
    await ui.selectOption('Event Type', 'conference');
    await ui.fillDate('Start Date', '2025-08-01');
    await ui.fillDate('End Date', '2025-08-03');
    await ui.setCheckbox('Online Event', true);
    await ui.fillTextField('Meeting URL', 'https://zoom.us/event-test');
    await ui.fillTextField('Registration Contact Email', 'events@test.com');

    // Set coordinator (SearchableSelect)
    await ui.selectSearchableOption('Event Coordinator', /carol/i);

    await ui.clickSaveNow();
    await ui.waitForSave();

    const entities = await api.peekAll('event');
    const created = entities.find(e => (e.title ?? e.name ?? '').includes('Relation Event'));
    expect(created).toBeDefined();
    eventId = created!.id;
  });

  test('coordinator relation persisted correctly', async ({ api }) => {
    const event = await api.readEntity('event', eventId);
    expect(event.fields.event_coordinator).toBe(teamMemberId);
  });
});

test.describe('Relations: Team Member favorite blog post', () => {
  let blogId: number;
  let teamMemberId: number;

  test.describe.configure({ mode: 'serial' });

  test.afterAll(async ({ request }) => {
    const { ApiHelper } = await import('./helpers');
    const api = new ApiHelper(request);
    await api.login();
    await api.deleteAll('team-member');
    await api.deleteAll('blog-post');
  });

  test('create a blog post to be the favorite', async ({ api }) => {
    await api.deleteAll('blog-post');
    await api.deleteAll('team-member');

    const result = await api.createEntity('blog-post', {
      title: { rendered: `Fav Blog ${TS()}` },
      fields: {
        content: '<p>test</p>', excerpt: 'test', status: 'published',
        is_featured: true, allow_comments: true, reading_time_minutes: 3,
        slug: `fav-blog-${TS()}`, featured_image: '',
        seo_metadata: { meta_title: '', meta_description: '', meta_keywords: '', canonical_url: '' },
        external_links: [], publication_year: '', scheduled_date: '',
      },
    });
    blogId = result.id;
  });

  test('create team member with favorite blog post relation', async ({ page, ui, api }) => {
    await ui.gotoNewEntity('team-member');

    await ui.fillTitle(`Reader Dave ${TS()}`);
    await ui.fillTextField('Work Email', 'dave@company.com');
    await ui.fillTextField('Job Title', 'Blogger');
    await ui.fillDate('Hire Date', '2023-01-10');

    // Set remote worker to skip office address validation
    await ui.setCheckbox('Remote Worker', true);

    // Set favorite blog post relation (SearchableSelect)
    await ui.selectSearchableOption('Favorite Blog Post', /fav blog/i);

    // Emergency contact (pre-populated from min_items=1)
    await ui.fillTextField('Contact Name', 'Emergency Person');
    await ui.fillTextField('Phone Number', '+1 555-0000');

    await ui.clickSaveNow();
    await ui.waitForSave();

    const entities = await api.peekAll('team-member');
    const created = entities.find(e => (e.title ?? e.name ?? '').includes('Reader Dave'));
    expect(created).toBeDefined();
    teamMemberId = created!.id;
  });

  test('favorite blog post relation is saved', async ({ api }) => {
    const member = await api.readEntity('team-member', teamMemberId);
    expect(member.fields.favorite_blog_post).toBe(blogId);
  });

  test('deleting blog post, team member still loads', async ({ api, page, ui }) => {
    await api.deleteEntity('blog-post', blogId);

    const member = await api.readEntity('team-member', teamMemberId);
    expect(member.id).toBe(teamMemberId);
    expect(member.fields.favorite_blog_post).toBe(blogId); // ID still stored

    // UI still works
    await ui.gotoEditEntity('team-member', teamMemberId);
    await expect(page.locator('h1')).toContainText('Edit Team Member');
  });
});

test.describe('Multi-Entity Bulk Operations', () => {
  test.describe.configure({ mode: 'serial' });

  test.afterAll(async ({ request }) => {
    const { ApiHelper } = await import('./helpers');
    const api = new ApiHelper(request);
    await api.login();
    for (const name of ['blog-post', 'team-member', 'product', 'event', 'objective']) {
      await api.deleteAll(name);
    }
  });

  test('create entities across all types and verify counts', async ({ api }) => {
    // Clean slate
    for (const name of ['blog-post', 'team-member', 'product', 'event', 'objective']) {
      await api.deleteAll(name);
    }

    // Create 2 blog posts
    for (let i = 0; i < 2; i++) {
      await api.createEntity('blog-post', {
        title: { rendered: `Bulk Blog ${i} ${TS()}` },
        fields: {
          content: '<p>test</p>', excerpt: 'test', status: 'draft', is_featured: false,
          allow_comments: true, reading_time_minutes: 1, slug: `bulk-${i}-${TS()}`,
          featured_image: '',
          seo_metadata: { meta_title: '', meta_description: '', meta_keywords: '', canonical_url: '' },
          external_links: [], publication_year: '', scheduled_date: '',
        },
      });
    }

    // Create 2 team members
    for (let i = 0; i < 2; i++) {
      await api.createEntity('team-member', {
        title: { rendered: `Bulk TM ${i} ${TS()}` },
        fields: {
          email: `bulk${i}@test.com`, department: 'engineering', job_title: 'Dev',
          years_of_experience: i, performance_score: 5, is_remote: true,
          hire_date: '20240101', salary: 80000,
          emergency_contacts: [{ contact_name: 'EC1', relationship: 'friend', phone: '+1 555-0001', email: 'ec@test.com' }], social_links: [],
          avatar: '', bio: '',
          office_address: { street: '50 Bulk St', city: 'Seattle', state: 'WA', postal_code: '98101', country: 'US' },
          favorite_blog_post: -1,
        },
      });
    }

    // Create 1 product
    await api.createEntity('product', {
      title: { rendered: `Bulk Product ${TS()}` },
      fields: {
        short_description: 'bulk', long_description: '',
        product_category: 'electronics', subcategory: '',
        primary_image: '/media/placeholder.png', gallery: [],
        base_price: 10, discount_percentage: 0,
        is_published: true, is_digital: true, weight_kg: 0,
        variants: [{ variant_name: 'v1', sku: `BULK-${TS()}`, price: 10, stock_quantity: 1, is_available: true }],
        specifications: [], product_manager: -1,
        launch_date: '20250601', product_url: '',
      },
    });

    // Verify counts
    expect(await api.peekAll('blog-post')).toHaveLength(2);
    expect(await api.peekAll('team-member')).toHaveLength(2);
    expect(await api.peekAll('product')).toHaveLength(1);
  });

  test('list pages show correct counts for all entity types', async ({ ui }) => {
    await ui.gotoEntityList('blog-post');
    expect(await ui.entityRowCount()).toBe(2);

    await ui.gotoEntityList('team-member');
    expect(await ui.entityRowCount()).toBe(2);

    await ui.gotoEntityList('product');
    expect(await ui.entityRowCount()).toBe(1);
  });

  test('bulk delete via API, verify empty lists', async ({ api, ui }) => {
    await api.deleteAll('blog-post');
    await api.deleteAll('team-member');
    await api.deleteAll('product');

    await ui.gotoEntityList('blog-post');
    expect(await ui.entityRowCount()).toBe(0);

    await ui.gotoEntityList('team-member');
    expect(await ui.entityRowCount()).toBe(0);

    await ui.gotoEntityList('product');
    expect(await ui.entityRowCount()).toBe(0);
  });
});
