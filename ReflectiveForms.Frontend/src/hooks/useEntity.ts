import { useQuery, useMutation, useQueryClient, useInfiniteQuery } from '@tanstack/react-query';
import {
  fetchSchema,
  fetchAllSchemas,
  readEntity,
  peekAllEntities,
  peekAllEntitiesPaginated,
  createEntity,
  updateEntity,
  deleteEntity,
  sanityCheck,
  fetchCapabilities,
  fetchEntityHistory,
} from '../api/client';
import { EntityData, EntitySchema, PaginatedPeekResponse, AllCapabilities, EntityRevisionsResponse } from '../types/schema';

// Schema hooks
export function useSchema(entityName: string) {
  return useQuery({
    queryKey: ['schema', entityName],
    queryFn: async () => {
      const result = await fetchSchema(entityName);
      if (result.error) throw new Error(result.error);
      return result.data as EntitySchema;
    },
    staleTime: 1000 * 60 * 60, // Schemas rarely change, cache for 1 hour
  });
}

export function useAllSchemas() {
  return useQuery({
    queryKey: ['schemas'],
    queryFn: async () => {
      const result = await fetchAllSchemas();
      if (result.error) throw new Error(result.error);
      return result.data as Record<string, EntitySchema>;
    },
    staleTime: 1000 * 60 * 60,
  });
}

// Capabilities hook
export function useCapabilities() {
  return useQuery({
    queryKey: ['capabilities'],
    queryFn: async () => {
      const result = await fetchCapabilities();
      if (result.error) throw new Error(result.error);
      return result.data as AllCapabilities;
    },
    staleTime: 1000 * 60 * 5, // Cache for 5 minutes
    retry: 3,
  });
}

// Entity hooks
export function useEntity(entityName: string, id: number | undefined) {
  const safeId = (id !== undefined && !Number.isNaN(id)) ? id : undefined;
  return useQuery({
    queryKey: ['entity', entityName, safeId],
    queryFn: async () => {
      if (safeId === undefined) return null;
      const result = await readEntity(entityName, safeId);
      if (result.error) throw new Error(result.error);
      return result.data as EntityData;
    },
    enabled: safeId !== undefined,
  });
}

export function useEntityList(entityName: string) {
  return useQuery({
    queryKey: ['entities', entityName],
    queryFn: async () => {
      const result = await peekAllEntities(entityName);
      if (result.error) throw new Error(result.error);
      return result.data;
    },
    enabled: !!entityName,
  });
}

export function usePaginatedEntityList(entityName: string, pageSize: number = 20) {
  return useInfiniteQuery<PaginatedPeekResponse, Error>({
    queryKey: ['entities-paginated', entityName, pageSize],
    queryFn: async ({ pageParam }) => {
      const result = await peekAllEntitiesPaginated(
        entityName,
        pageSize,
        pageParam as string | undefined
      );
      if (result.error) throw new Error(result.error);
      return result.data!;
    },
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (lastPage) => lastPage.next_page_token ?? undefined,
    staleTime: 1000 * 30, // 30 seconds — mutations invalidate explicitly
  });
}

export function useCreateEntity(entityName: string) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (data: Partial<EntityData>) => createEntity(entityName, data),
    onSuccess: (response) => {
      queryClient.invalidateQueries({ queryKey: ['entities', entityName] });
      queryClient.invalidateQueries({ queryKey: ['entities-paginated', entityName] });
      // Pre-populate the entity cache so the edit page renders immediately
      // without a loading flash after navigation.
      if (response.data?.id) {
        queryClient.setQueryData(['entity', entityName, response.data.id], response.data);
      }
    },
  });
}

export function useUpdateEntity(entityName: string) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (data: Partial<EntityData>) => updateEntity(entityName, data),
    onSuccess: (_, variables) => {
      queryClient.invalidateQueries({ queryKey: ['entities', entityName] });
      queryClient.invalidateQueries({ queryKey: ['entities-paginated', entityName] });
      queryClient.invalidateQueries({ queryKey: ['entity', entityName, variables.id] });
    },
  });
}

export function useDeleteEntity(entityName: string) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (id: number) => deleteEntity(entityName, id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['entities', entityName] });
      queryClient.invalidateQueries({ queryKey: ['entities-paginated', entityName] });
    },
  });
}

export function useSanityCheck(entityName: string) {
  return useMutation({
    mutationFn: (data: Partial<EntityData>) => sanityCheck(entityName, data),
  });
}

export function useEntityHistory(entityName: string, id: number | undefined) {
  const safeId = (id !== undefined && !Number.isNaN(id)) ? id : undefined;
  return useQuery({
    queryKey: ['entity-history', entityName, safeId],
    queryFn: async () => {
      if (safeId === undefined) return null;
      const result = await fetchEntityHistory(entityName, safeId);
      if (result.error) throw new Error(result.error);
      return result.data as EntityRevisionsResponse;
    },
    enabled: safeId !== undefined,
  });
}
