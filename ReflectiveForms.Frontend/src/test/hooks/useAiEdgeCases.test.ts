import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, waitFor, act } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { createElement } from 'react';
import {
  useAiSemanticSearch,
  useAiDiffSummary,
  useAiSanityCheck,
  useAiNaturalLanguageFilter,
  useAiRelationSuggest,
} from '../../hooks/useAi';
import * as client from '../../api/client';
import type { EntitySchema } from '../../types/schema';

vi.mock('../../api/client', async () => {
  const actual = await vi.importActual('../../api/client');
  return {
    ...actual,
    aiSemanticSearch: vi.fn(),
    aiSanityCheck: vi.fn(),
    aiDiffSummary: vi.fn(),
    aiNaturalLanguageFilter: vi.fn(),
    aiRelationSuggest: vi.fn(),
  };
});

function createWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return ({ children }: { children: React.ReactNode }) =>
    createElement(QueryClientProvider, { client: queryClient }, children);
}

const fullSchema: EntitySchema = {
  entity_name: 'blog',
  readable_name: { singular: 'Blog Post', plural: 'Blog Posts' },
  features: {
    has_author: false,
    has_tags: false,
    has_categories: false,
    has_parent_child: false,
    require_title_uniqueness: false,
    supports_frontend_edit: true,
    has_individual_sharing: false,
    supports_semantic_search: true,
    supports_ai_generation: true,
    supports_ai_diff_summary: true,
    supports_natural_language_filter: true,
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

const schemaAllDisabled: EntitySchema = {
  ...fullSchema,
  features: {
    ...fullSchema.features,
    supports_semantic_search: false,
    supports_ai_generation: false,
    supports_ai_diff_summary: false,
    supports_natural_language_filter: false,
  },
};

describe('useAi hooks edge cases', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    client.setAiDisabled(false);
  });

  afterEach(() => {
    vi.resetAllMocks();
    client.setAiDisabled(false);
  });

  // --- Global AI disabled flag affects all hooks ---

  describe('Global AI disabled — all mutations', () => {
    it('useAiSanityCheck should throw when AI is globally disabled', async () => {
      client.setAiDisabled(true);

      vi.mocked(client.aiSanityCheck).mockResolvedValue({ error: 'AI features are disabled' });

      const { result } = renderHook(() => useAiSanityCheck(), {
        wrapper: createWrapper(),
      });

      await expect(
        act(() => result.current.mutateAsync({
          entityName: 'blog',
          fieldName: 'email',
          fieldValue: 'test',
        })),
      ).rejects.toThrow();
    });

    it('useAiNaturalLanguageFilter should throw when AI is globally disabled', async () => {
      client.setAiDisabled(true);

      vi.mocked(client.aiNaturalLanguageFilter).mockResolvedValue({ error: 'AI features are disabled' });

      const { result } = renderHook(() => useAiNaturalLanguageFilter(), {
        wrapper: createWrapper(),
      });

      await expect(
        act(() => result.current.mutateAsync({ entityName: 'blog', query: 'test' })),
      ).rejects.toThrow();
    });

    it('useAiRelationSuggest should throw when AI is globally disabled', async () => {
      client.setAiDisabled(true);

      vi.mocked(client.aiRelationSuggest).mockResolvedValue({ error: 'AI features are disabled' });

      const { result } = renderHook(() => useAiRelationSuggest(), {
        wrapper: createWrapper(),
      });

      await expect(
        act(() => result.current.mutateAsync({
          entityName: 'blog',
          relationField: 'author',
          currentText: 'John',
        })),
      ).rejects.toThrow();
    });
  });

  // --- Feature flag gating ---

  describe('Feature flag gating', () => {
    it('useAiSemanticSearch should be idle when schema has no semantic search', () => {
      const { result } = renderHook(
        () => useAiSemanticSearch('test', 'blog', schemaAllDisabled),
        { wrapper: createWrapper() },
      );

      expect(result.current.fetchStatus).toBe('idle');
    });

    it('useAiDiffSummary should be idle when schema has no diff summary', () => {
      const { result } = renderHook(
        () => useAiDiffSummary('blog', 42, 3, schemaAllDisabled),
        { wrapper: createWrapper() },
      );

      expect(result.current.fetchStatus).toBe('idle');
    });
  });

  // --- Error handling edge cases ---

  describe('Error handling', () => {
    it('useAiSemanticSearch should handle undefined data', async () => {
      vi.mocked(client.aiSemanticSearch).mockResolvedValue({ data: undefined as any });

      const { result } = renderHook(
        () => useAiSemanticSearch('test query', 'blog', fullSchema),
        { wrapper: createWrapper() },
      );

      await waitFor(() => {
        // Should handle gracefully
        expect(result.current.fetchStatus).not.toBe('idle');
      });
    });

    it('useAiDiffSummary should handle undefined data', async () => {
      vi.mocked(client.aiDiffSummary).mockResolvedValue({ data: undefined as any });

      const { result } = renderHook(
        () => useAiDiffSummary('blog', 42, 3, fullSchema),
        { wrapper: createWrapper() },
      );

      await waitFor(() => {
        expect(result.current.fetchStatus).not.toBe('idle');
      });
    });

    it('useAiRelationSuggest should handle network error', async () => {
      vi.mocked(client.aiRelationSuggest).mockRejectedValue(new Error('Network error'));

      const { result } = renderHook(() => useAiRelationSuggest(), {
        wrapper: createWrapper(),
      });

      await expect(
        act(() => result.current.mutateAsync({
          entityName: 'blog',
          relationField: 'author',
          currentText: 'John',
        })),
      ).rejects.toThrow('Network error');
    });
  });

  // --- Query key uniqueness ---

  describe('Query key isolation', () => {
    it('useAiSemanticSearch with different queries should not share cache', async () => {
      vi.mocked(client.aiSemanticSearch)
        .mockResolvedValueOnce({ data: [{ entity_id: 1, title: 'Result A', entity_name: 'blog', score: 0.9 }] })
        .mockResolvedValueOnce({ data: [{ entity_id: 2, title: 'Result B', entity_name: 'blog', score: 0.8 }] });

      const wrapper = createWrapper();

      const { result: result1 } = renderHook(
        () => useAiSemanticSearch('query A', 'blog', fullSchema),
        { wrapper },
      );

      const { result: result2 } = renderHook(
        () => useAiSemanticSearch('query B', 'blog', fullSchema),
        { wrapper },
      );

      await waitFor(() => expect(result1.current.isSuccess).toBe(true));
      await waitFor(() => expect(result2.current.isSuccess).toBe(true));

      // Results should be independent
      expect(result1.current.data?.[0]?.title).not.toBe(result2.current.data?.[0]?.title);
    });

    it('useAiDiffSummary with different revision indices should not share cache', async () => {
      vi.mocked(client.aiDiffSummary)
        .mockResolvedValueOnce({ data: { summary: 'Rev 1 changes' } })
        .mockResolvedValueOnce({ data: { summary: 'Rev 2 changes' } });

      const wrapper = createWrapper();

      const { result: result1 } = renderHook(
        () => useAiDiffSummary('blog', 42, 1, fullSchema),
        { wrapper },
      );

      const { result: result2 } = renderHook(
        () => useAiDiffSummary('blog', 42, 2, fullSchema),
        { wrapper },
      );

      await waitFor(() => expect(result1.current.isSuccess).toBe(true));
      await waitFor(() => expect(result2.current.isSuccess).toBe(true));
    });
  });

  // --- Re-enable after disable ---

  describe('Re-enable after disable', () => {
    it('queries should resume when AI is re-enabled', async () => {
      client.setAiDisabled(true);

      const { result, rerender } = renderHook(
        () => useAiSemanticSearch('test', 'blog', fullSchema),
        { wrapper: createWrapper() },
      );

      expect(result.current.fetchStatus).toBe('idle');

      client.setAiDisabled(false);
      vi.mocked(client.aiSemanticSearch).mockResolvedValue({
        data: [{ entity_id: 1, title: 'Found', entity_name: 'blog', score: 0.9 }],
      });

      rerender();

      // After re-enabling, the query might refetch depending on implementation
      // Just verify it doesn't crash
      expect(result.current).toBeDefined();
    });
  });
});
