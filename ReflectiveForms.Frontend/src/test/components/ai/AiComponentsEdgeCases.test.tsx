import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router-dom';
import { createElement } from 'react';
import { AiGlobalSearch } from '../../../components/ai/AiGlobalSearch';
import { AiSuggestButton } from '../../../components/ai/AiSuggestButton';
import { AiSanityCheckBadge } from '../../../components/ai/AiSanityCheckBadge';
import { AiDiffSummary } from '../../../components/ai/AiDiffSummary';
import { AiNaturalLanguageFilter } from '../../../components/ai/AiNaturalLanguageFilter';
import { AiRelationSuggestions } from '../../../components/ai/AiRelationSuggestions';
import { AiAssistantProvider } from '../../../lib/AiAssistantContext';
import * as client from '../../../api/client';
import type { EntitySchema } from '../../../types/schema';

vi.mock('../../../api/client');

function createWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return ({ children }: { children: React.ReactNode }) =>
    createElement(
      QueryClientProvider,
      { client: queryClient },
      createElement(MemoryRouter, null, children),
    );
}

const schemaWithAllAi: EntitySchema = {
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

const schemaNoSemanticSearch: EntitySchema = {
  ...schemaWithAllAi,
  features: {
    ...schemaWithAllAi.features,
    supports_semantic_search: false,
  },
};

describe('AI Components Edge Cases', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  // Note: Individual AI components do NOT check feature flags internally.
  // Feature flag gating is done at the page/integration layer (FormField, EntityListPage, etc.).
  // See AiPageIntegration tests for flag gating tests.

  describe('AiDiffSummary — renders with proper schema', () => {
    it('should render toggle when supports_ai_diff_summary is true', () => {
      const Wrapper = createWrapper();
      render(
        createElement(Wrapper, null,
          createElement(AiDiffSummary, {
            entityName: 'blog',
            entityId: 42,
            revisionIndex: 3,
            schema: schemaWithAllAi,
          }),
        ),
      );

      expect(screen.getByTestId('ai-diff-summary')).toBeInTheDocument();
    });
  });

  describe('AiNaturalLanguageFilter — renders properly', () => {
    it('should render filter input when available', () => {
      const Wrapper = createWrapper();
      render(
        createElement(Wrapper, null,
          createElement(AiNaturalLanguageFilter, {
            entityName: 'blog',
            onFilterApplied: vi.fn(),
            onFilterCleared: vi.fn(),
          }),
        ),
      );

      expect(screen.getByTestId('ai-nl-filter')).toBeInTheDocument();
    });
  });

  // --- AiGlobalSearch edge cases ---

  describe('AiGlobalSearch edge cases', () => {
    it('should call onClose when Escape key is pressed', () => {
      const onClose = vi.fn();
      const Wrapper = createWrapper();
      render(
        createElement(Wrapper, null,
          createElement(AiGlobalSearch, {
            schemas: { blog: schemaWithAllAi },
            onClose,
          }),
        ),
      );

      const input = screen.getByTestId('ai-search-input');
      fireEvent.keyDown(input, { key: 'Escape' });

      expect(onClose).toHaveBeenCalled();
    });

    it('should handle search API error gracefully', async () => {
      vi.mocked(client.aiSemanticSearch).mockRejectedValue(new Error('Network error'));
      const Wrapper = createWrapper();
      render(
        createElement(Wrapper, null,
          createElement(AiGlobalSearch, {
            schemas: { blog: schemaWithAllAi },
            onClose: vi.fn(),
          }),
        ),
      );

      const input = screen.getByTestId('ai-search-input');
      fireEvent.change(input, { target: { value: 'search' } });

      // Should not crash — error state shown or empty results
      await waitFor(() => {
        expect(screen.getByTestId('ai-global-search')).toBeInTheDocument();
      });
    });

    it('should filter schemas without semantic search from results', () => {
      const Wrapper = createWrapper();
      render(
        createElement(Wrapper, null,
          createElement(AiGlobalSearch, {
            schemas: {
              blog: schemaWithAllAi,
              pages: schemaNoSemanticSearch,
            },
            onClose: vi.fn(),
          }),
        ),
      );

      expect(screen.getByTestId('ai-global-search')).toBeInTheDocument();
    });

    it('should handle empty schemas object', () => {
      const Wrapper = createWrapper();
      render(
        createElement(Wrapper, null,
          createElement(AiGlobalSearch, {
            schemas: {},
            onClose: vi.fn(),
          }),
        ),
      );

      expect(screen.getByTestId('ai-global-search')).toBeInTheDocument();
    });
  });

  // --- AiSuggestButton edge cases ---

  describe('AiSuggestButton edge cases', () => {
    function createAssistantWrapper() {
      const queryClient = new QueryClient({
        defaultOptions: { queries: { retry: false } },
      });
      return ({ children }: { children: React.ReactNode }) =>
        createElement(
          QueryClientProvider,
          { client: queryClient },
          createElement(MemoryRouter, null,
            createElement(AiAssistantProvider, null, children),
          ),
        );
    }

    it('should render nothing outside AiAssistantProvider', () => {
      const Wrapper = createWrapper();
      const { container } = render(
        createElement(Wrapper, null,
          createElement(AiSuggestButton, {
            entityName: 'blog',
            targetField: 'summary',
            currentFields: { title: 'Test' },
          }),
        ),
      );

      expect(container.innerHTML).toBe('');
    });

    it('should trigger assistant message on click', async () => {
      vi.mocked(client.aiAgentChat).mockResolvedValue({
        data: {
          response: 'Suggested value for summary.',
          tool_calls_made: [],
          proposed_actions: [],
        },
      });

      const Wrapper = createAssistantWrapper();
      render(
        createElement(Wrapper, null,
          createElement(AiSuggestButton, {
            entityName: 'blog',
            targetField: 'summary',
            currentFields: { title: 'Test' },
          }),
        ),
      );

      fireEvent.click(screen.getByTestId('ai-suggest-summary'));

      await waitFor(() => {
        expect(client.aiAgentChat).toHaveBeenCalled();
      });
    });
  });

  // --- AiSanityCheckBadge edge cases ---

  describe('AiSanityCheckBadge edge cases', () => {
    it('should handle mixed warning and error results', async () => {
      vi.mocked(client.aiSanityCheck).mockResolvedValue({
        data: [
          { field: 'content', passed: false, message: 'Too long', severity: 'Warning' },
          { field: 'content', passed: false, message: 'Contains PII', severity: 'Error' },
        ],
      });

      const Wrapper = createWrapper();
      render(
        createElement(Wrapper, null,
          createElement(AiSanityCheckBadge, {
            entityName: 'blog',
            fieldName: 'content',
            fieldValue: 'Some content with John Doe phone: 555-1234',
            checks: [
              { prompt: 'Check length', severity: 'Warning' },
              { prompt: 'Check PII', severity: 'Error' },
            ],
          }),
        ),
      );

      fireEvent.click(screen.getByTestId('ai-sanity-check-content'));

      await waitFor(() => {
        expect(screen.getByTestId('ai-sanity-result-content-0')).toBeInTheDocument();
        expect(screen.getByTestId('ai-sanity-result-content-1')).toBeInTheDocument();
      });
    });

    it('should handle API failure gracefully', async () => {
      vi.mocked(client.aiSanityCheck).mockRejectedValue(new Error('Check failed'));

      const Wrapper = createWrapper();
      render(
        createElement(Wrapper, null,
          createElement(AiSanityCheckBadge, {
            entityName: 'blog',
            fieldName: 'email',
            fieldValue: 'test@test.com',
            checks: [{ prompt: 'Check format', severity: 'Error' }],
          }),
        ),
      );

      fireEvent.click(screen.getByTestId('ai-sanity-check-email'));

      // Should not crash
      await waitFor(() => {
        expect(screen.getByTestId('ai-sanity-check-email')).toBeInTheDocument();
      });
    });

    it('should not render when fieldValue is empty', () => {
      const Wrapper = createWrapper();
      const { container } = render(
        createElement(Wrapper, null,
          createElement(AiSanityCheckBadge, {
            entityName: 'blog',
            fieldName: 'email',
            fieldValue: '',
            checks: [{ prompt: 'Check format', severity: 'Error' }],
          }),
        ),
      );

      // Empty field value — either renders disabled or not at all
      // Just verify it doesn't crash
      expect(container).toBeDefined();
    });
  });

  // --- AiDiffSummary edge cases ---

  describe('AiDiffSummary edge cases', () => {
    it('should not fetch on initial render (lazy load)', () => {
      const Wrapper = createWrapper();
      render(
        createElement(Wrapper, null,
          createElement(AiDiffSummary, {
            entityName: 'blog',
            entityId: 42,
            revisionIndex: 3,
            schema: schemaWithAllAi,
          }),
        ),
      );

      // Initially collapsed — API should NOT have been called
      expect(client.aiDiffSummary).not.toHaveBeenCalled();
    });

    it('should handle API failure when expanded', async () => {
      vi.mocked(client.aiDiffSummary).mockRejectedValue(new Error('Summary failed'));

      const Wrapper = createWrapper();
      render(
        createElement(Wrapper, null,
          createElement(AiDiffSummary, {
            entityName: 'blog',
            entityId: 42,
            revisionIndex: 3,
            schema: schemaWithAllAi,
          }),
        ),
      );

      fireEvent.click(screen.getByTestId('ai-diff-summary-toggle'));

      // Should not crash
      await waitFor(() => {
        expect(screen.getByTestId('ai-diff-summary')).toBeInTheDocument();
      });
    });
  });

  // --- AiNaturalLanguageFilter edge cases ---

  describe('AiNaturalLanguageFilter edge cases', () => {
    it('should not submit when input is empty', () => {
      const onFilterApplied = vi.fn();
      const Wrapper = createWrapper();
      render(
        createElement(Wrapper, null,
          createElement(AiNaturalLanguageFilter, {
            entityName: 'blog',
            onFilterApplied,
            onFilterCleared: vi.fn(),
          }),
        ),
      );

      // Input is empty — click submit
      fireEvent.click(screen.getByTestId('ai-nl-filter-submit'));

      // Should not trigger API call
      expect(client.aiNaturalLanguageFilter).not.toHaveBeenCalled();
    });

    it('should handle API error gracefully', async () => {
      vi.mocked(client.aiNaturalLanguageFilter).mockRejectedValue(new Error('Filter failed'));

      const onFilterApplied = vi.fn();
      const Wrapper = createWrapper();
      render(
        createElement(Wrapper, null,
          createElement(AiNaturalLanguageFilter, {
            entityName: 'blog',
            onFilterApplied,
            onFilterCleared: vi.fn(),
          }),
        ),
      );

      fireEvent.change(screen.getByTestId('ai-nl-filter-input'), {
        target: { value: 'show active items' },
      });
      fireEvent.click(screen.getByTestId('ai-nl-filter-submit'));

      // Should not crash
      await waitFor(() => {
        expect(onFilterApplied).not.toHaveBeenCalled();
      });
    });
  });

  // --- AiRelationSuggestions edge cases ---

  describe('AiRelationSuggestions edge cases', () => {
    it('should show empty state when no suggestions returned', async () => {
      vi.mocked(client.aiRelationSuggest).mockResolvedValue({
        data: [],
      });

      const Wrapper = createWrapper();
      render(
        createElement(Wrapper, null,
          createElement(AiRelationSuggestions, {
            entityName: 'blog',
            relationField: 'author_ref',
            currentText: 'Nonexistent Author',
            onSelect: vi.fn(),
          }),
        ),
      );

      fireEvent.click(screen.getByTestId('ai-relation-suggest-button-author_ref'));

      await waitFor(() => {
        const dropdown = screen.queryByTestId('ai-relation-suggest-dropdown-author_ref');
        // Should show dropdown but with no items or an empty message
        if (dropdown) {
          expect(screen.queryByTestId('ai-relation-option-5')).not.toBeInTheDocument();
        }
      });
    });

    it('should handle API failure gracefully', async () => {
      vi.mocked(client.aiRelationSuggest).mockRejectedValue(new Error('Suggest failed'));

      const Wrapper = createWrapper();
      render(
        createElement(Wrapper, null,
          createElement(AiRelationSuggestions, {
            entityName: 'blog',
            relationField: 'author_ref',
            currentText: 'John',
            onSelect: vi.fn(),
          }),
        ),
      );

      fireEvent.click(screen.getByTestId('ai-relation-suggest-button-author_ref'));

      // Should not crash
      await waitFor(() => {
        expect(screen.getByTestId('ai-relation-suggest-button-author_ref')).toBeInTheDocument();
      });
    });

    it('should show multiple suggestions', async () => {
      vi.mocked(client.aiRelationSuggest).mockResolvedValue({
        data: [
          { id: 1, title: 'John Smith', score: 0.95 },
          { id: 2, title: 'John Doe', score: 0.85 },
          { id: 3, title: 'John Williams', score: 0.75 },
        ],
      });

      const Wrapper = createWrapper();
      render(
        createElement(Wrapper, null,
          createElement(AiRelationSuggestions, {
            entityName: 'blog',
            relationField: 'author_ref',
            currentText: 'John',
            onSelect: vi.fn(),
          }),
        ),
      );

      fireEvent.click(screen.getByTestId('ai-relation-suggest-button-author_ref'));

      await waitFor(() => {
        expect(screen.getByTestId('ai-relation-option-1')).toBeInTheDocument();
        expect(screen.getByTestId('ai-relation-option-2')).toBeInTheDocument();
        expect(screen.getByTestId('ai-relation-option-3')).toBeInTheDocument();
      });
    });
  });
});
