import { useQuery, useMutation } from '@tanstack/react-query';
import {
  aiSemanticSearch,
  aiSanityCheck,
  aiDiffSummary,
  aiNaturalLanguageFilter,
  aiRelationSuggest,
  isAiDisabled,
} from '../api/client';
import type { EntitySchema } from '../types/schema';

/**
 * Check whether an AI feature is available for a given schema.
 * Returns false if the schema is null/undefined or the feature flag is off.
 */
function isAiFeatureEnabled(
  schema: EntitySchema | null | undefined,
  feature: keyof Pick<
    EntitySchema['features'],
    'supports_semantic_search' | 'supports_ai_generation' | 'supports_ai_diff_summary' | 'supports_natural_language_filter'
  >,
): boolean {
  if (isAiDisabled()) return false;
  return !!schema?.features[feature];
}

// --- Queries ---

export function useAiSemanticSearch(
  query: string,
  entityName?: string,
  schema?: EntitySchema | null,
) {
  const enabled = !!query.trim() && isAiFeatureEnabled(schema, 'supports_semantic_search');
  return useQuery({
    queryKey: ['ai', 'semantic_search', query, entityName],
    queryFn: async () => {
      const result = await aiSemanticSearch(query, entityName);
      if (result.error) throw new Error(result.error);
      return result.data!;
    },
    enabled,
    staleTime: 1000 * 30,
  });
}

export function useAiDiffSummary(
  entityName: string,
  entityId: number | undefined,
  revisionIndex: number | undefined,
  schema?: EntitySchema | null,
) {
  const enabled =
    !!entityName &&
    entityId !== undefined &&
    revisionIndex !== undefined &&
    isAiFeatureEnabled(schema, 'supports_ai_diff_summary');
  return useQuery({
    queryKey: ['ai', 'diff_summary', entityName, entityId, revisionIndex],
    queryFn: async () => {
      const result = await aiDiffSummary(entityName, entityId!, revisionIndex!);
      if (result.error) throw new Error(result.error);
      return result.data!;
    },
    enabled,
    staleTime: 1000 * 60 * 5, // Diff summaries are stable
  });
}

// --- Mutations ---

export function useAiSanityCheck() {
  return useMutation({
    mutationFn: async ({
      entityName,
      fieldName,
      fieldValue,
    }: {
      entityName: string;
      fieldName: string;
      fieldValue: unknown;
    }) => {
      const result = await aiSanityCheck(entityName, fieldName, fieldValue);
      if (result.error) throw new Error(result.error);
      return result.data!;
    },
  });
}

export function useAiNaturalLanguageFilter() {
  return useMutation({
    mutationFn: async ({ entityName, query }: { entityName: string; query: string }) => {
      const result = await aiNaturalLanguageFilter(entityName, query);
      if (result.error) throw new Error(result.error);
      return result.data!;
    },
  });
}

export function useAiRelationSuggest() {
  return useMutation({
    mutationFn: async ({
      entityName,
      relationField,
      currentText,
    }: {
      entityName: string;
      relationField: string;
      currentText: string;
    }) => {
      const result = await aiRelationSuggest(entityName, relationField, currentText);
      if (result.error) throw new Error(result.error);
      return result.data!;
    },
  });
}
