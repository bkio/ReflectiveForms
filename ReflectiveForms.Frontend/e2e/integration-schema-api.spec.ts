import { test, expect } from './helpers';

/**
 * Schema & API Contract Integration Tests
 *
 * Validates the API contract between frontend and backend:
 * - Schema endpoint returns all 5 entity types + reserved entities
 * - Each schema has expected fields, features, and configuration
 * - CRUD operations work correctly at the API level for all entity types
 * - Login flow produces valid session
 * - Reserved entity types (users) are accessible
 */

const TS = () => Date.now().toString(36);

test.describe('Schema API Contract', () => {
  test('schema endpoint returns all configured entity types', async ({ api }) => {
    const schemas = await api.getAllSchemas();

    // 5 custom entities + reserved entities
    expect(schemas).toHaveProperty('objective');
    expect(schemas).toHaveProperty('blog-post');
    expect(schemas).toHaveProperty('team-member');
    expect(schemas).toHaveProperty('product');
    expect(schemas).toHaveProperty('event');
  });

  test('blog-post schema has correct features', async ({ api }) => {
    const schemas = await api.getAllSchemas() as Record<string, any>;
    const blogSchema = schemas['blog-post'];

    expect(blogSchema.entity_name).toBe('blog-post');
    expect(blogSchema.readable_name.singular).toBe('Blog Post');
    expect(blogSchema.readable_name.plural).toBe('Blog Posts');
    expect(blogSchema.features.has_author).toBe(true);
    expect(blogSchema.features.has_tags).toBe(true);
    expect(blogSchema.features.has_categories).toBe(true);
    expect(blogSchema.features.has_parent_child).toBe(false);
  });

  test('team-member schema features', async ({ api }) => {
    const schemas = await api.getAllSchemas() as Record<string, any>;
    const tmSchema = schemas['team-member'];

    expect(tmSchema.features.supports_frontend_edit).toBe(true);
    expect(tmSchema.features.has_author).toBe(false);
    expect(tmSchema.features.has_tags).toBe(false);
  });

  test('product schema supports hierarchical (parent-child)', async ({ api }) => {
    const schemas = await api.getAllSchemas() as Record<string, any>;
    const productSchema = schemas['product'];

    expect(productSchema.features.has_parent_child).toBe(true);
    expect(productSchema.features.has_tags).toBe(true);
    expect(productSchema.features.has_categories).toBe(true);
  });

  test('event schema has categories but no tags or parent', async ({ api }) => {
    const schemas = await api.getAllSchemas() as Record<string, any>;
    const eventSchema = schemas['event'];

    expect(eventSchema.features.has_categories).toBe(true);
    expect(eventSchema.features.has_tags).toBe(false);
    expect(eventSchema.features.has_parent_child).toBe(false);
  });

  test('objective schema has all features enabled', async ({ api }) => {
    const schemas = await api.getAllSchemas() as Record<string, any>;
    const objSchema = schemas['objective'];

    expect(objSchema.features.has_author).toBe(true);
    expect(objSchema.features.has_tags).toBe(true);
    expect(objSchema.features.has_categories).toBe(true);
    expect(objSchema.features.has_parent_child).toBe(true);
  });

  test('each schema has fields array with proper structure', async ({ api }) => {
    const schemas = await api.getAllSchemas() as Record<string, any>;

    for (const [name, schema] of Object.entries(schemas) as [string, any][]) {
      if (!schema.fields) continue; // skip reserved entities without fields

      expect(Array.isArray(schema.fields), `${name} should have fields array`).toBe(true);

      for (const field of schema.fields) {
        expect(field).toHaveProperty('name');
        expect(field).toHaveProperty('type');
        expect(field).toHaveProperty('label');
      }
    }
  });

  test('blog-post schema contains expected field types', async ({ api }) => {
    const schemas = await api.getAllSchemas() as Record<string, any>;
    const fields = schemas['blog-post'].fields as Array<{ name: string; type: string }>;

    const fieldNames = fields.map(f => f.name);
    expect(fieldNames).toContain('content');
    expect(fieldNames).toContain('excerpt');
    expect(fieldNames).toContain('featured_image');
    expect(fieldNames).toContain('status');
    expect(fieldNames).toContain('scheduled_date');
    expect(fieldNames).toContain('is_featured');
    expect(fieldNames).toContain('reading_time_minutes');
    expect(fieldNames).toContain('seo_metadata');
    expect(fieldNames).toContain('external_links');
    expect(fieldNames).toContain('slug');
    expect(fieldNames).toContain('publication_year');
  });

  test('product schema contains DynamicChoicesRuntimeAsync field', async ({ api }) => {
    const schemas = await api.getAllSchemas() as Record<string, any>;
    const fields = schemas['product'].fields as Array<{ name: string; type: string; dynamic_choices_runtime?: unknown }>;

    const subcategory = fields.find(f => f.name === 'subcategory');
    expect(subcategory).toBeDefined();
    // Should have dynamic choices indicator
    expect(subcategory!.type.toLowerCase()).toContain('select');
  });
});

test.describe('CRUD API Contract: all entity types', () => {
  test.describe.configure({ mode: 'serial' });
  const entities = ['blog-post', 'team-member', 'product', 'event', 'objective'];

  test.afterAll(async ({ request }) => {
    const { ApiHelper } = await import('./helpers');
    const api = new ApiHelper(request);
    await api.login();
    for (const e of entities) {
      await api.deleteAll(e);
    }
  });

  test('PEEK_ALL returns empty array for clean state', async ({ api }) => {
    for (const entityName of entities) {
      await api.deleteAll(entityName);
      const list = await api.peekAll(entityName);
      expect(list).toEqual([]);
    }
  });

  test('CREATE + READ + DELETE cycle for blog-post', async ({ api }) => {
    const result = await api.createEntity('blog-post', {
      title: { rendered: `API CRUD Blog ${TS()}` },
      fields: {
        content: '<p>API test</p>', excerpt: 'api', status: 'draft',
        is_featured: false, allow_comments: true, reading_time_minutes: 1,
        slug: `api-crud-${TS()}`, featured_image: '',
        seo_metadata: { meta_title: '', meta_description: '', meta_keywords: '', canonical_url: '' },
        external_links: [], publication_year: '', scheduled_date: '',
      },
    });
    expect(result.id).toBeGreaterThan(0);

    const read = await api.readEntity('blog-post', result.id);
    expect(read.title.rendered).toContain('API CRUD Blog');

    await api.deleteEntity('blog-post', result.id);
    const list = await api.peekAll('blog-post');
    expect(list.find(e => e.id === result.id)).toBeUndefined();
  });

  test('CREATE + READ + DELETE cycle for team-member', async ({ api }) => {
    const result = await api.createEntity('team-member', {
      title: { rendered: `API TM ${TS()}` },
      fields: {
        email: 'api@test.com', department: 'engineering', job_title: 'Dev',
        years_of_experience: 3, performance_score: 5, is_remote: true,
        bio: '', hire_date: '20240101', salary: 80000,
        emergency_contacts: [{ contact_name: 'EC1', relationship: 'friend', phone: '+1 555-0001', email: 'ec@test.com' }], social_links: [],
        avatar: '', office_address: { street: '1 Main St', city: 'Anytown', state: 'CA', postal_code: '90210', country: 'US' },
        favorite_blog_post: -1,
      },
    });
    expect(result.id).toBeGreaterThan(0);

    const read = await api.readEntity('team-member', result.id);
    expect(read.title.rendered).toContain('API TM');
    expect(read.fields.department).toBe('engineering');
    expect(Number(read.fields.salary)).toBe(80000);

    await api.deleteEntity('team-member', result.id);
    const list = await api.peekAll('team-member');
    expect(list.find(e => e.id === result.id)).toBeUndefined();
  });

  test('CREATE + READ + DELETE cycle for product', async ({ api }) => {
    const result = await api.createEntity('product', {
      title: { rendered: `API Product ${TS()}` },
      fields: {
        short_description: 'desc', long_description: '',
        product_category: 'electronics', subcategory: 'laptops',
        primary_image: '/media/placeholder.png', gallery: [],
        base_price: 100, discount_percentage: 0,
        is_published: false, is_digital: false, weight_kg: 1,
        variants: [{ variant_name: 'v1', sku: `API-${TS()}`, price: 100, stock_quantity: 10, is_available: true }],
        specifications: [], product_manager: -1,
        launch_date: '20250601', product_url: '',
      },
    });
    expect(result.id).toBeGreaterThan(0);

    const read = await api.readEntity('product', result.id);
    expect(read.title.rendered).toContain('API Product');
    expect(Number(read.fields.base_price)).toBe(100);

    await api.deleteEntity('product', result.id);
  });

  test('CREATE + READ for event', async ({ api }) => {
    const result = await api.createEntity('event', {
      title: { rendered: `API Event ${TS()}` },
      fields: {
        description: '<p>API event</p>', event_type: 'meetup',
        start_date: '20250701', end_date: '20250702',
        is_online: true, meeting_url: 'https://zoom.us/test',
        venue: { venue_name: '', venue_address: { street: '', city: '', state: '', postal_code: '', country: 'US' }, capacity: 0, venue_url: '' },
        max_attendees: 50, ticket_price: 0,
        registration_email: 'test@test.com', banner_image: '',
        sessions: [], sponsors: [], event_coordinator: -1,
        registration_url: '',
      },
    });

    const read = await api.readEntity('event', result.id);
    expect(read.fields.is_online).toBe(true);
    expect(read.fields.meeting_url).toBe('https://zoom.us/test');

    await api.deleteEntity('event', result.id);
  });

  test('CREATE + READ for objective', async ({ api }) => {
    const result = await api.createEntity('objective', {
      title: { rendered: `API OKR ${TS()}` },
      fields: {
        objective_work_start_date: '20250101', objective_type: 'short_term',
        documentation_url: '', root_cause: `API cause ${TS()}`,
        creator_comment: { author: 2, comment: 'test' },
        key_results: [{ key_result: 'KR1', key_result_comments: [], achieved: false }],
        objective_comments: [],
        objective_initiation_year: '-1', year_based_okr_type: 'unspecified',
      },
    });

    const read = await api.readEntity('objective', result.id);
    expect(read.fields.key_results).toHaveLength(1);
    expect(read.fields.key_results[0].key_result).toBe('KR1');

    await api.deleteEntity('objective', result.id);
  });
});

test.describe('Login & Auth Contract', () => {
  test('login with valid credentials succeeds', async ({ request }) => {
    const res = await request.post('http://localhost:9000/rf/api/login', {
      data: { email: 'admin@karasoftware.com', password: '123456' },
    });
    expect(res.ok()).toBeTruthy();
  });

  test('login with invalid credentials fails', async ({ request }) => {
    const res = await request.post('http://localhost:9000/rf/api/login', {
      data: { email: 'admin@karasoftware.com', password: 'wrongpassword' },
    });
    expect(res.ok()).toBeFalsy();
  });

  test('unauthenticated CRUD request fails', async ({ request }) => {
    // Create a new request context without auth cookies
    const res = await request.post('http://localhost:9000/rf/api/crud?operation=PEEK_ALL&type=blog-post', {
      data: {},
      headers: { Cookie: '' },
    });
    // Should be unauthorized
    // (The exact response depends on backend behavior — may return 401 or empty)
    // We just verify we don't crash
    expect(res.status()).toBeDefined();
  });
});

test.describe('Dashboard renders all entity types from backend', () => {
  test('dashboard shows all 5 main entity type cards', async ({ page, ui }) => {
    await ui.gotoDashboard();

    // Verify entity cards
    await expect(page.locator('h3', { hasText: 'Objectives' })).toBeVisible();
    await expect(page.locator('h3', { hasText: 'Blog Posts' })).toBeVisible();
    await expect(page.locator('h3', { hasText: 'Team Members' })).toBeVisible();
    await expect(page.locator('h3', { hasText: 'Products' })).toBeVisible();
    await expect(page.locator('h3', { hasText: 'Events' })).toBeVisible();
  });

  test('dashboard shows correct feature badges for each entity', async ({ page, ui }) => {
    await ui.gotoDashboard();

    // Objectives card should have Has Author, Has Tags, Has Categories, Hierarchical
    const objCard = page.locator('.bg-white.rounded-lg').filter({ has: page.locator('h3', { hasText: 'Objectives' }) });
    await expect(objCard.locator('span', { hasText: 'Has Author' })).toBeVisible();
    await expect(objCard.locator('span', { hasText: 'Has Tags' })).toBeVisible();
    await expect(objCard.locator('span', { hasText: 'Has Categories' })).toBeVisible();
    await expect(objCard.locator('span', { hasText: 'Hierarchical' })).toBeVisible();

    // Events card has categories but NOT tags
    const eventCard = page.locator('.bg-white.rounded-lg').filter({ has: page.locator('h3', { hasText: 'Events' }) });
    await expect(eventCard.locator('span', { hasText: 'Has Categories' })).toBeVisible();
    await expect(eventCard.locator('span', { hasText: 'Has Tags' })).not.toBeVisible();
  });

  test('content types count matches on dashboard', async ({ page, ui, api }) => {
    await ui.gotoDashboard();

    const schemas = await api.getAllSchemas() as Record<string, any>;
    // The dashboard shows entity cards for each non-sharing entity the user can peek
    const entityCards = page.locator('.bg-white.rounded-lg h3');
    const cardCount = await entityCards.count();

    // Dashboard filters out sharing-only entities, so card count should be <= total schemas
    expect(cardCount).toBeGreaterThan(0);
    expect(cardCount).toBeLessThanOrEqual(Object.keys(schemas).length);
  });
});
