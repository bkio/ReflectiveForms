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
} from '../api/client';
import { EntityData, EntitySchema, PaginatedPeekResponse } from '../types/schema';

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

// Entity hooks
export function useEntity(entityName: string, id: number | undefined) {
  return useQuery({
    queryKey: ['entity', entityName, id],
    queryFn: async () => {
      if (id === undefined) return null;
      const result = await readEntity(entityName, id);
      if (result.error) throw new Error(result.error);
      return result.data as EntityData;
    },
    enabled: id !== undefined,
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
  });
}

export function useCreateEntity(entityName: string) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (data: Partial<EntityData>) => createEntity(entityName, data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['entities', entityName] });
    },
  });
}

export function useUpdateEntity(entityName: string) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (data: Partial<EntityData>) => updateEntity(entityName, data),
    onSuccess: (_, variables) => {
      queryClient.invalidateQueries({ queryKey: ['entities', entityName] });
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
    },
  });
}

export function useSanityCheck(entityName: string) {
  return useMutation({
    mutationFn: (data: Partial<EntityData>) => sanityCheck(entityName, data),
  });
}
