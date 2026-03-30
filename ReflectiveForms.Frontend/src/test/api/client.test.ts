import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import {
  fetchSchema,
  fetchAllSchemas,
  readEntity,
  peekAllEntities,
  createEntity,
  updateEntity,
  deleteEntity,
  sanityCheck,
  tryLockEntity,
  unlockEntity,
  setApiBaseUrl,
  getApiBaseUrl,
} from '../../api/client';

// Mock fetch globally
const mockFetch = vi.fn();
(globalThis as any).fetch = mockFetch;

describe('API Client', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  afterEach(() => {
    vi.resetAllMocks();
  });

  describe('setApiBaseUrl / getApiBaseUrl', () => {
    it('should update the API base URL', () => {
      setApiBaseUrl('http://custom-api/v1');
      expect(getApiBaseUrl()).toBe('http://custom-api/v1');
    });

    it('should use the updated base URL in fetch calls', async () => {
      setApiBaseUrl('http://custom-api/v1');

      mockFetch.mockResolvedValueOnce({
        ok: true,
        json: () => Promise.resolve({}),
      });

      await fetchSchema('TestEntity');

      expect(mockFetch).toHaveBeenCalledWith(
        'http://custom-api/v1/schema?type=TestEntity',
        expect.any(Object)
      );

      // Reset to default for other tests
      setApiBaseUrl('http://localhost:9000/rf/api');
    });
  });

  describe('fetchSchema', () => {
    it('should fetch schema for entity type', async () => {
      const mockSchema = {
        entity_name: 'TestEntity',
        fields: [],
      };

      mockFetch.mockResolvedValueOnce({
        ok: true,
        json: () => Promise.resolve(mockSchema),
      });

      const result = await fetchSchema('TestEntity');

      expect(result.data).toEqual(mockSchema);
      expect(mockFetch).toHaveBeenCalledWith(
        expect.stringContaining('/schema?type=TestEntity'),
        expect.any(Object)
      );
    });

    it('should handle errors', async () => {
      mockFetch.mockResolvedValueOnce({
        ok: false,
        status: 404,
        json: () => Promise.resolve({ message: 'Not found' }),
      });

      const result = await fetchSchema('NonExistent');

      expect(result.error).toBeDefined();
    });

    it('should handle network errors', async () => {
      mockFetch.mockRejectedValueOnce(new Error('Network error'));

      const result = await fetchSchema('TestEntity');

      expect(result.error).toBe('Network error');
    });
  });

  describe('fetchAllSchemas', () => {
    it('should fetch all schemas', async () => {
      const mockSchemas = {
        Entity1: { entity_name: 'Entity1', fields: [] },
        Entity2: { entity_name: 'Entity2', fields: [] },
      };

      mockFetch.mockResolvedValueOnce({
        ok: true,
        json: () => Promise.resolve(mockSchemas),
      });

      const result = await fetchAllSchemas();

      expect(result.data).toEqual(mockSchemas);
    });
  });

  describe('CRUD operations', () => {
    it('should read entity', async () => {
      const mockEntity = { id: 1, title: { rendered: 'Test' } };

      mockFetch.mockResolvedValueOnce({
        ok: true,
        json: () => Promise.resolve(mockEntity),
      });

      const result = await readEntity('TestEntity', 1);

      expect(result.data).toEqual(mockEntity);
      expect(mockFetch).toHaveBeenCalledWith(
        expect.stringContaining('/crud?operation=READ'),
        expect.objectContaining({
          method: 'POST',
          body: JSON.stringify({ id: 1 }),
        })
      );
    });

    it('should peek all entities', async () => {
      const mockEntities = [
        { id: 1, title: 'Entity 1' },
        { id: 2, title: 'Entity 2' },
      ];

      mockFetch.mockResolvedValueOnce({
        ok: true,
        json: () => Promise.resolve(mockEntities),
      });

      const result = await peekAllEntities('TestEntity');

      expect(result.data).toEqual(mockEntities);
    });

    it('should create entity', async () => {
      const newEntity = { title: { rendered: 'New' }, fields: {} };
      const createdEntity = { id: 1, ...newEntity };

      mockFetch.mockResolvedValueOnce({
        ok: true,
        json: () => Promise.resolve(createdEntity),
      });

      const result = await createEntity('TestEntity', newEntity);

      expect(result.data).toEqual(createdEntity);
      expect(mockFetch).toHaveBeenCalledWith(
        expect.stringContaining('/crud?operation=CREATE'),
        expect.objectContaining({
          method: 'POST',
          body: JSON.stringify(newEntity),
        })
      );
    });

    it('should update entity', async () => {
      const updateData = { id: 1, title: { rendered: 'Updated' }, fields: {} };

      mockFetch.mockResolvedValueOnce({
        ok: true,
        json: () => Promise.resolve(updateData),
      });

      const result = await updateEntity('TestEntity', updateData);

      expect(result.data).toEqual(updateData);
      expect(mockFetch).toHaveBeenCalledWith(
        expect.stringContaining('/crud?operation=UPDATE'),
        expect.objectContaining({
          method: 'POST',
        })
      );
    });

    it('should delete entity', async () => {
      mockFetch.mockResolvedValueOnce({
        ok: true,
        json: () => Promise.resolve({ success: true }),
      });

      const result = await deleteEntity('TestEntity', 1);

      expect(result.data).toBeDefined();
      expect(mockFetch).toHaveBeenCalledWith(
        expect.stringContaining('/crud?operation=DELETE'),
        expect.objectContaining({
          method: 'POST',
          body: JSON.stringify({ id: 1 }),
        })
      );
    });
  });

  describe('sanityCheck', () => {
    it('should run sanity check', async () => {
      const entityData = { title: { rendered: 'Test' }, fields: {} };

      mockFetch.mockResolvedValueOnce({
        ok: true,
        json: () => Promise.resolve({ message: 'OK' }),
      });

      const result = await sanityCheck('TestEntity', entityData);

      expect(result.data).toEqual({ message: 'OK' });
      expect(mockFetch).toHaveBeenCalledWith(
        expect.stringContaining('/sanity_check'),
        expect.objectContaining({
          method: 'POST',
        })
      );
    });
  });

  describe('Entity locking', () => {
    it('should try to lock entity', async () => {
      mockFetch.mockResolvedValueOnce({
        ok: true,
        json: () => Promise.resolve({}),
      });

      const result = await tryLockEntity('TestEntity', 1);

      expect(result.error).toBeUndefined();
      expect(mockFetch).toHaveBeenCalledWith(
        expect.stringContaining('/entity_lock_control'),
        expect.any(Object)
      );
    });

    it('should unlock entity', async () => {
      mockFetch.mockResolvedValueOnce({
        ok: true,
        json: () => Promise.resolve({}),
      });

      const result = await unlockEntity('TestEntity', 1);

      expect(result.error).toBeUndefined();
      expect(mockFetch).toHaveBeenCalledWith(
        expect.stringContaining('/entity_lock_control'),
        expect.any(Object)
      );
    });
  });

  describe('Error handling', () => {
    it('should handle HTTP errors with message', async () => {
      mockFetch.mockResolvedValueOnce({
        ok: false,
        status: 400,
        json: () => Promise.resolve({ message: 'Validation error' }),
      });

      const result = await fetchSchema('TestEntity');

      expect(result.error).toBe('Validation error');
    });

    it('should handle HTTP errors without message', async () => {
      mockFetch.mockResolvedValueOnce({
        ok: false,
        status: 500,
        json: () => Promise.reject(new Error('Parse error')),
      });

      const result = await fetchSchema('TestEntity');

      expect(result.error).toBe('Request failed');
    });

    it('should handle network errors', async () => {
      mockFetch.mockRejectedValueOnce(new Error('Failed to fetch'));

      const result = await fetchSchema('TestEntity');

      expect(result.error).toBe('Failed to fetch');
    });

    it('should include credentials in requests', async () => {
      mockFetch.mockResolvedValueOnce({
        ok: true,
        json: () => Promise.resolve({}),
      });

      await fetchSchema('TestEntity');

      expect(mockFetch).toHaveBeenCalledWith(
        expect.any(String),
        expect.objectContaining({
          credentials: 'include',
        })
      );
    });
  });
});
