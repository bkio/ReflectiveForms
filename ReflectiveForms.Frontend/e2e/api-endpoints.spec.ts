import { test, expect } from '@playwright/test';

/**
 * E2E tests for API endpoints
 * These tests verify the backend API is responding correctly
 */

const API_BASE = 'http://localhost:9000/rf/api';

test.describe('API Endpoint Verification', () => {
  test.describe('Schema API', () => {
    test('GET /schema should return all schemas', async ({ request }) => {
      const response = await request.get(`${API_BASE}/schema`);

      expect(response.status()).toBe(200);

      const data = await response.json();
      expect(data).toBeDefined();
      expect(typeof data).toBe('object');
    });

    test('GET /schema?type={name} should return specific schema', async ({ request }) => {
      // First get all schemas to find a valid entity name
      const allSchemasResponse = await request.get(`${API_BASE}/schema`);
      const allSchemas = await allSchemasResponse.json();

      const entityNames = Object.keys(allSchemas);
      if (entityNames.length > 0) {
        const entityName = entityNames[0];

        const response = await request.get(`${API_BASE}/schema?type=${entityName}`);

        expect(response.status()).toBe(200);

        const schema = await response.json();
        expect(schema.entity_name).toBe(entityName);
        expect(schema.fields).toBeDefined();
        expect(Array.isArray(schema.fields)).toBe(true);
      }
    });
  });

  test.describe('CRUD API', () => {
    test('PEEK_ALL should return list of entities', async ({ request }) => {
      // Get a valid entity type first
      const schemasResponse = await request.get(`${API_BASE}/schema`);
      const schemas = await schemasResponse.json();
      const entityName = Object.keys(schemas)[0];

      if (entityName) {
        const response = await request.post(
          `${API_BASE}/crud?operation=PEEK_ALL&type=${entityName}`,
          { data: {} }
        );

        expect(response.status()).toBe(200);

        const entities = await response.json();
        expect(Array.isArray(entities)).toBe(true);
      }
    });

    test('CREATE should create new entity', async ({ request }) => {
      const schemasResponse = await request.get(`${API_BASE}/schema`);
      const schemas = await schemasResponse.json();
      const entityName = Object.keys(schemas)[0];

      if (entityName) {
        const response = await request.post(
          `${API_BASE}/crud?operation=CREATE&type=${entityName}`,
          {
            data: {
              title: { rendered: `E2E Test Entity ${Date.now()}` },
              fields: {},
            },
          }
        );

        expect(response.status()).toBe(200);

        const entity = await response.json();
        expect(entity.id).toBeDefined();
        expect(entity.title.rendered).toContain('E2E Test Entity');

        // Clean up - delete the entity
        await request.post(
          `${API_BASE}/crud?operation=DELETE&type=${entityName}`,
          { data: { id: entity.id } }
        );
      }
    });

    test('READ should return entity by ID', async ({ request }) => {
      const schemasResponse = await request.get(`${API_BASE}/schema`);
      const schemas = await schemasResponse.json();
      const entityName = Object.keys(schemas)[0];

      if (entityName) {
        // First create an entity
        const createResponse = await request.post(
          `${API_BASE}/crud?operation=CREATE&type=${entityName}`,
          {
            data: {
              title: { rendered: `Read Test Entity ${Date.now()}` },
              fields: {},
            },
          }
        );
        const created = await createResponse.json();

        // Then read it
        const response = await request.post(
          `${API_BASE}/crud?operation=READ&type=${entityName}`,
          { data: { id: created.id } }
        );

        expect(response.status()).toBe(200);

        const entity = await response.json();
        expect(entity.id).toBe(created.id);

        // Clean up
        await request.post(
          `${API_BASE}/crud?operation=DELETE&type=${entityName}`,
          { data: { id: created.id } }
        );
      }
    });

    test('UPDATE should modify entity', async ({ request }) => {
      const schemasResponse = await request.get(`${API_BASE}/schema`);
      const schemas = await schemasResponse.json();
      const entityName = Object.keys(schemas)[0];

      if (entityName) {
        // Create an entity
        const createResponse = await request.post(
          `${API_BASE}/crud?operation=CREATE&type=${entityName}`,
          {
            data: {
              title: { rendered: 'Original Title' },
              fields: {},
            },
          }
        );
        const created = await createResponse.json();

        // Update it
        const response = await request.post(
          `${API_BASE}/crud?operation=UPDATE&type=${entityName}`,
          {
            data: {
              id: created.id,
              title: { rendered: 'Updated Title' },
              fields: {},
            },
          }
        );

        expect(response.status()).toBe(200);

        // Read it back to verify
        const readResponse = await request.post(
          `${API_BASE}/crud?operation=READ&type=${entityName}`,
          { data: { id: created.id } }
        );
        const updated = await readResponse.json();
        expect(updated.title.rendered).toBe('Updated Title');

        // Clean up
        await request.post(
          `${API_BASE}/crud?operation=DELETE&type=${entityName}`,
          { data: { id: created.id } }
        );
      }
    });

    test('DELETE should remove entity', async ({ request }) => {
      const schemasResponse = await request.get(`${API_BASE}/schema`);
      const schemas = await schemasResponse.json();
      const entityName = Object.keys(schemas)[0];

      if (entityName) {
        // Create an entity
        const createResponse = await request.post(
          `${API_BASE}/crud?operation=CREATE&type=${entityName}`,
          {
            data: {
              title: { rendered: 'To Delete' },
              fields: {},
            },
          }
        );
        const created = await createResponse.json();

        // Delete it
        const response = await request.post(
          `${API_BASE}/crud?operation=DELETE&type=${entityName}`,
          { data: { id: created.id } }
        );

        expect(response.status()).toBe(200);

        // Try to read it - should fail or return null
        const readResponse = await request.post(
          `${API_BASE}/crud?operation=READ&type=${entityName}`,
          { data: { id: created.id } }
        );

        // Either 404 or returns null/error
        if (readResponse.status() === 200) {
          const result = await readResponse.json();
          expect(result).toBeNull();
        } else {
          expect([400, 404]).toContain(readResponse.status());
        }
      }
    });
  });

  test.describe('Assets API', () => {
    const jsFiles = [
      'rf-core.js',
      'rf-form-state.js',
      'rf-lock-control.js',
      'rf-repeater.js',
      'rf-relation.js',
      'rf-media.js',
      'rf-entity-list.js',
      'rf-ui-components.js',
    ];

    for (const file of jsFiles) {
      test(`GET /assets/js/${file} should return JS file`, async ({ request }) => {
        const response = await request.get(`${API_BASE}/assets/js/${file}`);

        expect(response.status()).toBe(200);

        const contentType = response.headers()['content-type'];
        expect(contentType).toContain('javascript');

        const content = await response.text();
        expect(content.length).toBeGreaterThan(0);
      });
    }
  });

  test.describe('CORS', () => {
    test('should allow requests from React dev server origin', async ({ request }) => {
      const response = await request.get(`${API_BASE}/schema`, {
        headers: {
          Origin: 'http://localhost:5173',
        },
      });

      expect(response.status()).toBe(200);

      const corsHeader = response.headers()['access-control-allow-origin'];
      expect(corsHeader).toBe('http://localhost:5173');
    });

    test('should handle preflight OPTIONS request', async ({ request }) => {
      const response = await request.fetch(`${API_BASE}/schema`, {
        method: 'OPTIONS',
        headers: {
          Origin: 'http://localhost:5173',
          'Access-Control-Request-Method': 'GET',
        },
      });

      // Options should return 200 or 204
      expect([200, 204]).toContain(response.status());

      const allowMethods = response.headers()['access-control-allow-methods'];
      expect(allowMethods).toBeDefined();
    });
  });
});
