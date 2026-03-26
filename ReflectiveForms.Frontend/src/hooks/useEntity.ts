import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import {
  fetchSchema,
  fetchAllSchemas,
  readEntity,
  peekAllEntities,
  createEntity,
  updateEntity,
  deleteEntity,
  sanityCheck,
} from '../api/client';
import { EntityData, EntitySchema } from '../types/schema';

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
