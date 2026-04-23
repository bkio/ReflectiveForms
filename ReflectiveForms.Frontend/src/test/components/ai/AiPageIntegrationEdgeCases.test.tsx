import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router-dom';
import { createElement } from 'react';
import { FormProvider, useForm } from 'react-hook-form';
import { FormField } from '../../../components/fields/FormField';
import { RelationField } from '../../../components/fields/RelationField';
import type { FieldSchema } from '../../../types/schema';
vi.mock('../../../api/client');

vi.mock('../../../components/form/SearchableSelect', () => ({
  SearchableSelect: ({ value, onChange }: { value: number; onChange: (v: number) => void }) =>
    createElement('select', {
      'data-testid': 'mock-searchable-select',
      value,
      onChange: (e: any) => onChange(Number(e.target.value)),
    }),
}));

vi.mock('../../../components/form/DynamicForm', () => ({
  useEntityFormContext: vi.fn(),
}));

import { useEntityFormContext } from '../../../components/form/DynamicForm';
import { AiAssistantProvider } from '../../../lib/AiAssistantContext';

function createWrapper() {
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

function FormWrapper({ children, defaultValues }: { children?: React.ReactNode; defaultValues?: Record<string, unknown> }) {
  const Comp = () => {
    const form = useForm({ defaultValues: defaultValues ?? { fields: {} } });
    return createElement(FormProvider, { ...form, children });
  };
  const Wrapper = createWrapper();
  return createElement(Wrapper, null, createElement(Comp));
}

// --- Field schemas with various AI configs ---

const fieldTextWithSuggestion: FieldSchema = {
  name: 'excerpt',
  type: 'Text',
  label: 'Excerpt',
  required: false,
  has_dynamic_choices_runtime: false,
  has_dynamic_choices_compile_time: false,
  has_logic_sanity_check: false,
  ai_suggestion: { prompt: 'Generate excerpt', source_fields: ['body'] },
};

const fieldTextAreaWithSuggestionAndCheck: FieldSchema = {
  name: 'body',
  type: 'TextArea',
  label: 'Body',
  required: false,
  has_dynamic_choices_runtime: false,
  has_dynamic_choices_compile_time: false,
  has_logic_sanity_check: false,
  ai_suggestion: { prompt: 'Suggest body', source_fields: [] },
  ai_sanity_checks: [
    { prompt: 'Is it professional?', severity: 'Warning' },
    { prompt: 'Contains PII?', severity: 'Error' },
  ],
};

const fieldTextAreaWithSanityCheckOnly: FieldSchema = {
  name: 'body',
  type: 'TextArea',
  label: 'Body',
  required: false,
  has_dynamic_choices_runtime: false,
  has_dynamic_choices_compile_time: false,
  has_logic_sanity_check: false,
  ai_sanity_checks: [{ prompt: 'Check quality', severity: 'Error' }],
};

const fieldNumberNoAi: FieldSchema = {
  name: 'priority',
  type: 'Number',
  label: 'Priority',
  required: false,
  has_dynamic_choices_runtime: false,
  has_dynamic_choices_compile_time: false,
  has_logic_sanity_check: false,
};

const fieldCheckboxNoAi: FieldSchema = {
  name: 'published',
  type: 'Checkbox',
  label: 'Published',
  required: false,
  has_dynamic_choices_runtime: false,
  has_dynamic_choices_compile_time: false,
  has_logic_sanity_check: false,
};

const fieldSelectNoAi: FieldSchema = {
  name: 'status',
  type: 'Select',
  label: 'Status',
  required: false,
  has_dynamic_choices_runtime: false,
  has_dynamic_choices_compile_time: false,
  has_logic_sanity_check: false,
  select_options: {
    choices: [
      { value: 'draft', label: 'Draft' },
      { value: 'published', label: 'Published' },
    ],
    allow_multiple: false,
  },
};

const relationFieldWithAiSuggestion: FieldSchema = {
  name: 'author_id',
  type: 'Relation',
  label: 'Author',
  required: false,
  has_dynamic_choices_runtime: false,
  has_dynamic_choices_compile_time: false,
  has_logic_sanity_check: false,
  relation_options: { relation_entity_name: 'authors', is_relation_entity_not_exists_ok: false, allow_multiple: false },
  ai_relation_suggestion: { top_k: 5 },
};

const relationFieldNoAi: FieldSchema = {
  name: 'category_id',
  type: 'Relation',
  label: 'Category',
  required: false,
  has_dynamic_choices_runtime: false,
  has_dynamic_choices_compile_time: false,
  has_logic_sanity_check: false,
  relation_options: { relation_entity_name: 'categories', is_relation_entity_not_exists_ok: false, allow_multiple: false },
};

describe('Page Integration Edge Cases', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  // --- 7.56: AI suggestion on various field types ---

  describe('AI suggestion on different field types (7.56)', () => {
    it('should show AiSuggestButton on Text field with ai_suggestion', () => {
      vi.mocked(useEntityFormContext).mockReturnValue({ entityName: 'blog', canUpdate: true });

      render(createElement(FormWrapper, null,
        createElement(FormField, { fieldSchema: fieldTextWithSuggestion }),
      ));

      expect(screen.getByTestId('ai-suggest-excerpt')).toBeInTheDocument();
    });

    it('should show AiSuggestButton on TextArea field with ai_suggestion', () => {
      vi.mocked(useEntityFormContext).mockReturnValue({ entityName: 'blog', canUpdate: true });

      render(createElement(FormWrapper, null,
        createElement(FormField, { fieldSchema: fieldTextAreaWithSuggestionAndCheck }),
      ));

      expect(screen.getByTestId('ai-suggest-body')).toBeInTheDocument();
    });

    it('should not show AiSuggestButton on Number field', () => {
      vi.mocked(useEntityFormContext).mockReturnValue({ entityName: 'blog', canUpdate: true });

      render(createElement(FormWrapper, null,
        createElement(FormField, { fieldSchema: fieldNumberNoAi }),
      ));

      expect(screen.queryByTestId('ai-suggest-priority')).not.toBeInTheDocument();
    });

    it('should not show AiSuggestButton on Checkbox field', () => {
      vi.mocked(useEntityFormContext).mockReturnValue({ entityName: 'blog', canUpdate: true });

      render(createElement(FormWrapper, null,
        createElement(FormField, { fieldSchema: fieldCheckboxNoAi }),
      ));

      expect(screen.queryByTestId('ai-suggest-published')).not.toBeInTheDocument();
    });

    it('should not show AiSuggestButton on Select field', () => {
      vi.mocked(useEntityFormContext).mockReturnValue({ entityName: 'blog', canUpdate: true });

      render(createElement(FormWrapper, null,
        createElement(FormField, { fieldSchema: fieldSelectNoAi }),
      ));

      expect(screen.queryByTestId('ai-suggest-status')).not.toBeInTheDocument();
    });
  });

  // --- Multiple AI features on same field ---

  describe('Multiple AI features on same field', () => {
    it('should show both suggest and sanity check on TextArea', () => {
      vi.mocked(useEntityFormContext).mockReturnValue({ entityName: 'blog', canUpdate: true });

      render(createElement(FormWrapper, null,
        createElement(FormField, { fieldSchema: fieldTextAreaWithSuggestionAndCheck }),
      ));

      expect(screen.getByTestId('ai-suggest-body')).toBeInTheDocument();
      expect(screen.getByTestId('ai-sanity-check-body')).toBeInTheDocument();
    });

    it('should show only sanity check when no ai_suggestion', () => {
      vi.mocked(useEntityFormContext).mockReturnValue({ entityName: 'blog', canUpdate: true });

      render(createElement(FormWrapper, null,
        createElement(FormField, { fieldSchema: fieldTextAreaWithSanityCheckOnly }),
      ));

      expect(screen.queryByTestId('ai-suggest-body')).not.toBeInTheDocument();
      expect(screen.getByTestId('ai-sanity-check-body')).toBeInTheDocument();
    });
  });

  // --- canUpdate permission gating ---

  describe('Permission gating', () => {
    it('should hide all AI features when canUpdate is false', () => {
      vi.mocked(useEntityFormContext).mockReturnValue({ entityName: 'blog', canUpdate: false });

      render(createElement(FormWrapper, null,
        createElement(FormField, { fieldSchema: fieldTextAreaWithSuggestionAndCheck }),
      ));

      expect(screen.queryByTestId('ai-suggest-body')).not.toBeInTheDocument();
      expect(screen.queryByTestId('ai-sanity-check-body')).not.toBeInTheDocument();
    });

    it('should hide relation AI when canUpdate is false', () => {
      vi.mocked(useEntityFormContext).mockReturnValue({ entityName: 'blog', canUpdate: false });

      render(createElement(FormWrapper, { defaultValues: { fields: { author_id: -1 } } },
        createElement(RelationField, { schema: relationFieldWithAiSuggestion, path: 'fields.author_id' }),
      ));

      expect(screen.queryByTestId('ai-relation-suggest-button-author_id')).not.toBeInTheDocument();
    });
  });

  // --- Null/missing entity context ---

  describe('Missing entity form context', () => {
    it('should not show AI features when context is null', () => {
      vi.mocked(useEntityFormContext).mockReturnValue(null);

      render(createElement(FormWrapper, null,
        createElement(FormField, { fieldSchema: fieldTextWithSuggestion }),
      ));

      expect(screen.queryByTestId('ai-suggest-excerpt')).not.toBeInTheDocument();
    });

    it('should not show AI sanity check when context is null', () => {
      vi.mocked(useEntityFormContext).mockReturnValue(null);

      render(createElement(FormWrapper, null,
        createElement(FormField, { fieldSchema: fieldTextAreaWithSanityCheckOnly }),
      ));

      expect(screen.queryByTestId('ai-sanity-check-body')).not.toBeInTheDocument();
    });
  });

  // --- Relation fields ---

  describe('Relation field AI integration', () => {
    it('should show AI relation suggestions when configured and user can update', () => {
      vi.mocked(useEntityFormContext).mockReturnValue({ entityName: 'blog', canUpdate: true });

      render(createElement(FormWrapper, { defaultValues: { fields: { author_id: -1 } } },
        createElement(RelationField, { schema: relationFieldWithAiSuggestion, path: 'fields.author_id' }),
      ));

      expect(screen.getByTestId('ai-relation-suggest-button-author_id')).toBeInTheDocument();
    });

    it('should not show AI suggestions on relation without config', () => {
      vi.mocked(useEntityFormContext).mockReturnValue({ entityName: 'blog', canUpdate: true });

      render(createElement(FormWrapper, { defaultValues: { fields: { category_id: -1 } } },
        createElement(RelationField, { schema: relationFieldNoAi, path: 'fields.category_id' }),
      ));

      expect(screen.queryByTestId('ai-relation-suggest-button-category_id')).not.toBeInTheDocument();
    });
  });
});
