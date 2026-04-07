import { test, expect } from './helpers';

const API_BASE = 'http://localhost:9000/rf/api';

/**
 * E2E tests for API endpoints — verifies the backend API responds correctly.
 * Uses the authenticated API helper for CRUD operations.
 */
test.describe('API Endpoint Verification', () => {
  // Use team-member for CRUD tests (well-known entity type)
  const CRUD_ENTITY = 'team-member';

  // Valid field data for team-member (satisfies all sanity checks)
  const validFields = {
    job_title: 'Tester',
    email: 'test@test.com',
    is_remote: false,
    department: 'engineering',
    years_of_experience: 1,
    performance_score: 5,
    salary: 50000,
    hire_date: '20260101',
    bio: 'Test bio',
    avatar: '',
    office_address: { street: '123 Test St', city: 'Testville', postal_code: '12345' },
    emergency_contacts: [{ contact_name: 'EC Person', relationship: 'friend', phone: '555-0000', email: '' }],
    social_links: [],
    favorite_blog_post: -1,
  };

  test.describe('Schema API', () => {
    test('GET /schema should return all schemas', async ({ api }) => {
      const allSchemas = await api.getAllSchemas();
      expect(allSchemas).toBeDefined();
      expect(typeof allSchemas).toBe('object');
      expect(Object.keys(allSchemas).length).toBeGreaterThan(0);
    });

    test('GET /schema?type={name} should return specific schema', async ({ api }) => {
      const allSchemas = await api.getAllSchemas();
      const entityName = Object.keys(allSchemas)[0];
      const res = await api.request.get(`${API_BASE}/schema?type=${entityName}`);
      expect(res.status()).toBe(200);

      const schema = await res.json();
      expect(schema.entity_name).toBe(entityName);
      expect(schema.fields).toBeDefined();
      expect(Array.isArray(schema.fields)).toBe(true);
      expect(schema.readable_name).toBeDefined();
      expect(schema.features).toBeDefined();
      expect(schema.api_endpoints).toBeDefined();
    });

    test('GET /schema?type=nonexistent should return error', async ({ api }) => {
      const res = await api.request.get(`${API_BASE}/schema?type=nonexistent_entity_xyz`);
      expect(res.status()).not.toBe(200);
    });
  });

  test.describe('CRUD API', () => {
    test.describe.configure({ mode: 'serial' });
    let createdId: number;

    test('PEEK_ALL should return list of entities', async ({ api }) => {
      const entities = await api.peekAll(CRUD_ENTITY);
      expect(Array.isArray(entities)).toBe(true);
    });

    test('PEEK_ALL_PAGINATED should return paginated results', async ({ api }) => {
      const result = await api.peekAllPaginated(CRUD_ENTITY, 5);
      expect(result.items).toBeDefined();
      expect(Array.isArray(result.items)).toBe(true);
    });

    test('CREATE should create new entity', async ({ api }) => {
      const entity = await api.createEntity(CRUD_ENTITY, {
        title: { rendered: `API Test Entity ${Date.now()}` },
        fields: validFields,
      });
      expect(entity.id).toBeDefined();
      expect(entity.id).toBeGreaterThan(0);
      createdId = entity.id;
    });

    test('READ should return entity by ID', async ({ api }) => {
      const entity = await api.readEntity(CRUD_ENTITY, createdId);
      expect(entity.id).toBe(createdId);
      expect(entity.title.rendered).toContain('API Test Entity');
    });

    test('UPDATE should modify entity', async ({ api }) => {
      const updated = await api.updateEntity(CRUD_ENTITY, {
        id: createdId,
        title: { rendered: 'Updated API Test Entity' },
        fields: validFields,
      });
      expect(updated).toBeDefined();

      const entity = await api.readEntity(CRUD_ENTITY, createdId);
      expect(entity.title.rendered).toBe('Updated API Test Entity');
    });

    test('DELETE should remove entity', async ({ api }) => {
      const deleteRes = await api.deleteEntity(CRUD_ENTITY, createdId);
      expect(deleteRes.ok()).toBeTruthy();
    });
  });

  test.describe('Authentication', () => {
    test('login with correct credentials should succeed', async ({ api }) => {
      const res = await api.request.post(`${API_BASE}/login`, {
        data: { email: 'admin@karasoftware.com', password: '123456' },
      });
      expect(res.status()).toBe(200);
      const body = await res.json();
      expect(body.token).toBeDefined();
    });

    test('login with wrong credentials should fail', async ({ api }) => {
      const res = await api.request.post(`${API_BASE}/login`, {
        data: { email: 'wrong@email.com', password: 'wrongpass' },
      });
      expect(res.ok()).toBeFalsy();
    });
  });

  test.describe('Schema validation', () => {
    test('each entity schema has required properties', async ({ api }) => {
      const allSchemas = await api.getAllSchemas();
      for (const [name, schema] of Object.entries(allSchemas)) {
        const s = schema as Record<string, unknown>;
        expect(s.entity_name, `${name} should have entity_name`).toBe(name);
        expect(s.fields, `${name} should have fields`).toBeDefined();
        expect(s.readable_name, `${name} should have readable_name`).toBeDefined();
        expect(s.api_endpoints, `${name} should have api_endpoints`).toBeDefined();
      }
    });

    test('fields have type, name, and label', async ({ api }) => {
      const res = await api.request.get(`${API_BASE}/schema?type=${CRUD_ENTITY}`);
      const schema = await res.json();
      for (const field of schema.fields) {
        expect(field.type, `Field ${field.name} should have type`).toBeDefined();
        expect(field.label, `Field ${field.name} should have label`).toBeDefined();
        expect(field.name, 'Field should have name').toBeDefined();
      }
    });
  });
});
