import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { createElement } from 'react';
import { useSchema, useEntity, useEntityList, useCreateEntity, useUpdateEntity, useDeleteEntity, useSanityCheck } from '../../hooks/useEntity';
import * as client from '../../api/client';

// Mock the API client
vi.mock('../../api/client');

const mockSchema = {
  entity_name: 'TestEntity',
  readable_name: { singular: 'Test Entity', plural: 'Test Entities' },
  features: {
    has_author: false,
    has_tags: false,
    has_categories: false,
    has_parent_child: false,
    require_title_uniqueness: false,
    supports_frontend_edit: 'ForAllAuthorized' as const,
  },
  fields: [],
  api_endpoints: {
    crud: '/rf/api/crud',
    sanity_check: '/rf/api/sanity_check',
    entity_lock: '/rf/api/entity_lock',
    media: '/rf/api/media',
  },
  schema_version: '1.0.0',
};

const mockEntityData = {
  id: 1,
  slug: 'test-entity-1',
  title: { rendered: 'Test Entity 1' },
  date: '2024-01-01T00:00:00',
  date_gmt: '2024-01-01T00:00:00',
  modified: '2024-01-01T00:00:00',
  modified_gmt: '2024-01-01T00:00:00',
  fields: { description: 'Test description' },
};

const mockPeekEntities = [
  { id: 1, title: 'Test Entity 1' },
  { id: 2, title: 'Test Entity 2' },
];

// Wrapper for React Query
function createWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: {
        retry: false,
      },
    },
  });

  return ({ children }: { children: React.ReactNode }) =>
    createElement(QueryClientProvider, { client: queryClient }, children);
}

describe('useEntity hooks', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  afterEach(() => {
    vi.resetAllMocks();
  });

  describe('useSchema', () => {
    it('should fetch schema successfully', async () => {
      vi.mocked(client.fetchSchema).mockResolvedValue({ data: mockSchema });

      const { result } = renderHook(() => useSchema('TestEntity'), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(result.current.data).toEqual(mockSchema);
      expect(client.fetchSchema).toHaveBeenCalledWith('TestEntity');
    });

    it('should handle schema fetch error', async () => {
      vi.mocked(client.fetchSchema).mockResolvedValue({ error: 'Not found' });

      const { result } = renderHook(() => useSchema('NonExistent'), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isError).toBe(true));

      expect(result.current.error?.message).toBe('Not found');
    });
  });

  describe('useEntity', () => {
    it('should fetch entity data by id', async () => {
      vi.mocked(client.readEntity).mockResolvedValue({ data: mockEntityData });

      const { result } = renderHook(() => useEntity('TestEntity', 1), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(result.current.data).toEqual(mockEntityData);
      expect(client.readEntity).toHaveBeenCalledWith('TestEntity', 1);
    });

    it('should not fetch when id is undefined', async () => {
      const { result } = renderHook(() => useEntity('TestEntity', undefined), {
        wrapper: createWrapper(),
      });

      expect(result.current.fetchStatus).toBe('idle');
      expect(client.readEntity).not.toHaveBeenCalled();
    });
  });

  describe('useEntityList', () => {
    it('should fetch list of entities', async () => {
      vi.mocked(client.peekAllEntities).mockResolvedValue({ data: mockPeekEntities });

      const { result } = renderHook(() => useEntityList('TestEntity'), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(result.current.data).toEqual(mockPeekEntities);
      expect(client.peekAllEntities).toHaveBeenCalledWith('TestEntity');
    });
  });

  describe('useCreateEntity', () => {
    it('should create entity successfully', async () => {
      vi.mocked(client.createEntity).mockResolvedValue({ data: mockEntityData });

      const { result } = renderHook(() => useCreateEntity('TestEntity'), {
        wrapper: createWrapper(),
      });

      const newEntity = {
        title: { rendered: 'New Entity' },
        fields: { description: 'New description' },
      };

      await result.current.mutateAsync(newEntity);

      expect(client.createEntity).toHaveBeenCalledWith('TestEntity', newEntity);
    });
  });

  describe('useUpdateEntity', () => {
    it('should update entity successfully', async () => {
      vi.mocked(client.updateEntity).mockResolvedValue({ data: mockEntityData });

      const { result } = renderHook(() => useUpdateEntity('TestEntity'), {
        wrapper: createWrapper(),
      });

      const updateData = {
        id: 1,
        title: { rendered: 'Updated Title' },
        fields: { description: 'Updated description' },
      };

      await result.current.mutateAsync(updateData);

      expect(client.updateEntity).toHaveBeenCalledWith('TestEntity', updateData);
    });
  });

  describe('useDeleteEntity', () => {
    it('should delete entity successfully', async () => {
      vi.mocked(client.deleteEntity).mockResolvedValue({ data: mockEntityData });

      const { result } = renderHook(() => useDeleteEntity('TestEntity'), {
        wrapper: createWrapper(),
      });

      await result.current.mutateAsync(1);

      expect(client.deleteEntity).toHaveBeenCalledWith('TestEntity', 1);
    });
  });

  describe('useSanityCheck', () => {
    it('should run sanity check successfully', async () => {
      vi.mocked(client.sanityCheck).mockResolvedValue({ data: { message: 'OK' } });

      const { result } = renderHook(() => useSanityCheck('TestEntity'), {
        wrapper: createWrapper(),
      });

      const entityData = {
        title: { rendered: 'Test' },
        fields: { description: 'Test' },
      };

      await result.current.mutateAsync(entityData);

      expect(client.sanityCheck).toHaveBeenCalledWith('TestEntity', entityData);
    });
  });
});
