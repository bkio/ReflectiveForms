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

describe('AI Components', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  describe('AiGlobalSearch', () => {
    it('should render search input', () => {
      const Wrapper = createWrapper();
      render(
        createElement(Wrapper, null,
          createElement(AiGlobalSearch, {
            schemas: { blog: mockSchema },
            onClose: vi.fn(),
          }),
        ),
      );

      expect(screen.getByTestId('ai-global-search')).toBeInTheDocument();
      expect(screen.getByTestId('ai-search-input')).toBeInTheDocument();
    });

    it('should call onClose when backdrop is clicked', () => {
      const onClose = vi.fn();
      const Wrapper = createWrapper();
      render(
        createElement(Wrapper, null,
          createElement(AiGlobalSearch, {
            schemas: { blog: mockSchema },
            onClose,
          }),
        ),
      );

      // The backdrop is the first fixed div inside the component
      const backdrop = screen.getByTestId('ai-global-search').querySelector('.fixed.inset-0.bg-black\\/50');
      if (backdrop) fireEvent.click(backdrop);
      expect(onClose).toHaveBeenCalled();
    });

    it('should show empty state when no results', async () => {
      vi.mocked(client.aiSemanticSearch).mockResolvedValue({ data: [] });
      const Wrapper = createWrapper();
      render(
        createElement(Wrapper, null,
          createElement(AiGlobalSearch, {
            schemas: { blog: mockSchema },
            onClose: vi.fn(),
          }),
        ),
      );

      const input = screen.getByTestId('ai-search-input');
      fireEvent.change(input, { target: { value: 'nonexistent' } });

      await waitFor(() => {
        expect(screen.getByTestId('ai-search-empty')).toBeInTheDocument();
      });
    });
  });

  describe('AiSuggestButton', () => {
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

    it('should render suggest button when inside provider', () => {
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

      expect(screen.getByTestId('ai-suggest-summary')).toBeInTheDocument();
      expect(screen.getByText('Suggest')).toBeInTheDocument();
    });

    it('should render nothing when outside provider', () => {
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

    it('should open AI assistant panel on click', async () => {
      // Mock the agent chat API so triggerMessage doesn't fail
      vi.mocked(client.aiAgentChat).mockResolvedValue({
        data: {
          response: 'Here is a suggestion.',
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

      // The click triggers a message through the assistant, which calls aiAgentChat
      await waitFor(() => {
        expect(client.aiAgentChat).toHaveBeenCalled();
      });
    });
  });

  describe('AiSanityCheckBadge', () => {
    it('should render check button when checks exist', () => {
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

      expect(screen.getByTestId('ai-sanity-check-email')).toBeInTheDocument();
    });

    it('should not render when no checks', () => {
      const Wrapper = createWrapper();
      const { container } = render(
        createElement(Wrapper, null,
          createElement(AiSanityCheckBadge, {
            entityName: 'blog',
            fieldName: 'email',
            fieldValue: 'test',
            checks: [],
          }),
        ),
      );

      expect(container.innerHTML).toBe('');
    });

    it('should show results after check', async () => {
      vi.mocked(client.aiSanityCheck).mockResolvedValue({
        data: [{ field: 'email', passed: false, message: 'Invalid email', severity: 'Error' }],
      });

      const Wrapper = createWrapper();
      render(
        createElement(Wrapper, null,
          createElement(AiSanityCheckBadge, {
            entityName: 'blog',
            fieldName: 'email',
            fieldValue: 'bad-email',
            checks: [{ prompt: 'Check email', severity: 'Error' }],
          }),
        ),
      );

      fireEvent.click(screen.getByTestId('ai-sanity-check-email'));

      await waitFor(() => {
        expect(screen.getByTestId('ai-sanity-result-email-0')).toBeInTheDocument();
        expect(screen.getByText('Invalid email')).toBeInTheDocument();
      });
    });

    it('should show passed state when all checks pass', async () => {
      vi.mocked(client.aiSanityCheck).mockResolvedValue({
        data: [{ field: 'email', passed: true, severity: 'Warning' }],
      });

      const Wrapper = createWrapper();
      render(
        createElement(Wrapper, null,
          createElement(AiSanityCheckBadge, {
            entityName: 'blog',
            fieldName: 'email',
            fieldValue: 'good@email.com',
            checks: [{ prompt: 'Check email', severity: 'Warning' }],
          }),
        ),
      );

      fireEvent.click(screen.getByTestId('ai-sanity-check-email'));

      await waitFor(() => {
        expect(screen.getByTestId('ai-sanity-passed-email')).toBeInTheDocument();
      });
    });
  });

  describe('AiDiffSummary', () => {
    it('should render collapsed by default', () => {
      const Wrapper = createWrapper();
      render(
        createElement(Wrapper, null,
          createElement(AiDiffSummary, {
            entityName: 'blog',
            entityId: 42,
            revisionIndex: 3,
            schema: mockSchema,
          }),
        ),
      );

      expect(screen.getByTestId('ai-diff-summary')).toBeInTheDocument();
      expect(screen.getByTestId('ai-diff-summary-toggle')).toBeInTheDocument();
      expect(screen.queryByTestId('ai-diff-summary-content')).not.toBeInTheDocument();
    });

    it('should fetch and show summary on expand', async () => {
      vi.mocked(client.aiDiffSummary).mockResolvedValue({
        data: { summary: 'Title was updated' },
      });

      const Wrapper = createWrapper();
      render(
        createElement(Wrapper, null,
          createElement(AiDiffSummary, {
            entityName: 'blog',
            entityId: 42,
            revisionIndex: 3,
            schema: mockSchema,
          }),
        ),
      );

      fireEvent.click(screen.getByTestId('ai-diff-summary-toggle'));

      await waitFor(() => {
        expect(screen.getByTestId('ai-diff-summary-content')).toBeInTheDocument();
        expect(screen.getByText('Title was updated')).toBeInTheDocument();
      });
    });
  });

  describe('AiNaturalLanguageFilter', () => {
    it('should render filter input', () => {
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
      expect(screen.getByTestId('ai-nl-filter-input')).toBeInTheDocument();
    });

    it('should call onFilterApplied after submit', async () => {
      const mockResult = {
        interpreted_filters: [{ field: 'fields.status', operator: 'equals', value: 'active' }],
        combination: 'and',
        natural_language_interpretation: 'Active posts',
        results: [{ id: 1, title: 'Test', modified_gmt: '2026-01-01' }],
        used_vector_fallback: false,
      };
      vi.mocked(client.aiNaturalLanguageFilter).mockResolvedValue({
        data: mockResult,
      });

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
        target: { value: 'show active' },
      });
      fireEvent.click(screen.getByTestId('ai-nl-filter-submit'));

      await waitFor(() => {
        expect(onFilterApplied).toHaveBeenCalledWith(mockResult);
      });
    });
  });

  describe('AiRelationSuggestions', () => {
    it('should render suggest button', () => {
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

      expect(screen.getByTestId('ai-relation-suggest-button-author_ref')).toBeInTheDocument();
    });

    it('should show suggestions after click', async () => {
      vi.mocked(client.aiRelationSuggest).mockResolvedValue({
        data: [{ id: 5, title: 'John Smith', score: 0.85 }],
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
        expect(screen.getByTestId('ai-relation-suggest-dropdown-author_ref')).toBeInTheDocument();
        expect(screen.getByText('John Smith')).toBeInTheDocument();
      });
    });

    it('should call onSelect when suggestion clicked', async () => {
      vi.mocked(client.aiRelationSuggest).mockResolvedValue({
        data: [{ id: 5, title: 'John Smith', score: 0.85 }],
      });

      const onSelect = vi.fn();
      const Wrapper = createWrapper();
      render(
        createElement(Wrapper, null,
          createElement(AiRelationSuggestions, {
            entityName: 'blog',
            relationField: 'author_ref',
            currentText: 'John',
            onSelect,
          }),
        ),
      );

      fireEvent.click(screen.getByTestId('ai-relation-suggest-button-author_ref'));

      await waitFor(() => {
        expect(screen.getByTestId('ai-relation-option-5')).toBeInTheDocument();
      });

      fireEvent.click(screen.getByTestId('ai-relation-option-5'));
      expect(onSelect).toHaveBeenCalledWith(5, 'John Smith');
    });
  });
});
