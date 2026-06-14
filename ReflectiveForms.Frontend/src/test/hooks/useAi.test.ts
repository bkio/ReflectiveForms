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

const mockSchema: EntitySchema = {
  entity_name: 'blog',
  readable_name: { singular: 'Blog Post', plural: 'Blog Posts' },
  features: {
    has_author: false,
    has_tags: false,
    has_categories: false,
    has_parent_child: false,
    require_title_uniqueness: false,
    supports_frontend_edit: true,
    show_in_navigation: true,
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

const disabledSchema: EntitySchema = {
  ...mockSchema,
  features: {
    ...mockSchema.features,
    supports_semantic_search: false,
    supports_ai_diff_summary: false,
  },
};

describe('useAi hooks', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    client.setAiDisabled(false);
  });

  afterEach(() => {
    vi.resetAllMocks();
    client.setAiDisabled(false);
  });

  describe('useAiSemanticSearch', () => {
    it('should fetch results when enabled', async () => {
      const mockResults = [{ entity_id: 1, title: 'Hit', entity_name: 'blog', score: 0.9 }];
      vi.mocked(client.aiSemanticSearch).mockResolvedValue({ data: mockResults });

      const { result } = renderHook(
        () => useAiSemanticSearch('test query', 'blog', mockSchema),
        { wrapper: createWrapper() },
      );

      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(result.current.data).toEqual(mockResults);
    });

    it('should not fetch when query is empty', () => {
      const { result } = renderHook(
        () => useAiSemanticSearch('', 'blog', mockSchema),
        { wrapper: createWrapper() },
      );

      expect(result.current.fetchStatus).toBe('idle');
    });

    it('should not fetch when feature is disabled', () => {
      const { result } = renderHook(
        () => useAiSemanticSearch('test', 'blog', disabledSchema),
        { wrapper: createWrapper() },
      );

      expect(result.current.fetchStatus).toBe('idle');
    });

    it('should handle errors', async () => {
      vi.mocked(client.aiSemanticSearch).mockResolvedValue({ error: 'Service error' });

      const { result } = renderHook(
        () => useAiSemanticSearch('test', 'blog', mockSchema),
        { wrapper: createWrapper() },
      );

      await waitFor(() => expect(result.current.isError).toBe(true));
      expect(result.current.error?.message).toBe('Service error');
    });
  });

  describe('useAiDiffSummary', () => {
    it('should fetch summary when enabled', async () => {
      vi.mocked(client.aiDiffSummary).mockResolvedValue({
        data: { summary: 'Title changed' },
      });

      const { result } = renderHook(
        () => useAiDiffSummary('blog', 42, 3, mockSchema),
        { wrapper: createWrapper() },
      );

      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(result.current.data?.summary).toBe('Title changed');
    });

    it('should not fetch when feature is disabled', () => {
      const { result } = renderHook(
        () => useAiDiffSummary('blog', 42, 3, disabledSchema),
        { wrapper: createWrapper() },
      );

      expect(result.current.fetchStatus).toBe('idle');
    });

    it('should not fetch when entityId is undefined', () => {
      const { result } = renderHook(
        () => useAiDiffSummary('blog', undefined, 3, mockSchema),
        { wrapper: createWrapper() },
      );

      expect(result.current.fetchStatus).toBe('idle');
    });
  });

  describe('useAiSanityCheck', () => {
    it('should return check results', async () => {
      const checks = [{ field: 'email', passed: false, message: 'Bad', severity: 'Error' as const }];
      vi.mocked(client.aiSanityCheck).mockResolvedValue({ data: checks });

      const { result } = renderHook(() => useAiSanityCheck(), {
        wrapper: createWrapper(),
      });

      await act(async () => {
        const data = await result.current.mutateAsync({
          entityName: 'users',
          fieldName: 'email',
          fieldValue: 'bad',
        });
        expect(data).toEqual(checks);
      });
    });
  });

  describe('useAiNaturalLanguageFilter', () => {
    it('should return filter result', async () => {
      const filterResult = {
        interpreted_filters: [{ field: 'fields.status', operator: 'equals', value: 'active' }],
        combination: 'and',
        natural_language_interpretation: 'Active items',
        results: [{ id: 1, title: 'Test', modified_gmt: '2026-01-01' }],
        used_vector_fallback: false,
      };
      vi.mocked(client.aiNaturalLanguageFilter).mockResolvedValue({ data: filterResult });

      const { result } = renderHook(() => useAiNaturalLanguageFilter(), {
        wrapper: createWrapper(),
      });

      await act(async () => {
        const data = await result.current.mutateAsync({
          entityName: 'blog',
          query: 'show active',
        });
        expect(data.natural_language_interpretation).toBe('Active items');
        expect(data.results).toHaveLength(1);
      });
    });
  });

  describe('useAiRelationSuggest', () => {
    it('should return suggestions', async () => {
      const suggestions = [{ id: 5, title: 'John', score: 0.85 }];
      vi.mocked(client.aiRelationSuggest).mockResolvedValue({ data: suggestions });

      const { result } = renderHook(() => useAiRelationSuggest(), {
        wrapper: createWrapper(),
      });

      await act(async () => {
        const data = await result.current.mutateAsync({
          entityName: 'blog',
          relationField: 'author_ref',
          currentText: 'John',
        });
        expect(data).toEqual(suggestions);
      });
    });
  });

  describe('isAiDisabled integration', () => {
    it('should disable semantic search query when AI is globally disabled', () => {
      client.setAiDisabled(true);

      const { result } = renderHook(
        () => useAiSemanticSearch('test query', 'blog', mockSchema),
        { wrapper: createWrapper() },
      );

      expect(result.current.fetchStatus).toBe('idle');
    });

    it('should disable diff summary query when AI is globally disabled', () => {
      client.setAiDisabled(true);

      const { result } = renderHook(
        () => useAiDiffSummary('blog', 42, 3, mockSchema),
        { wrapper: createWrapper() },
      );

      expect(result.current.fetchStatus).toBe('idle');
    });
  });
});
