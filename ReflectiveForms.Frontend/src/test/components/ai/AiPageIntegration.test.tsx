import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router-dom';
import { createElement } from 'react';
import { FormProvider, useForm } from 'react-hook-form';
import { FormField } from '../../../components/fields/FormField';
import { RelationField } from '../../../components/fields/RelationField';
import type { FieldSchema } from '../../../types/schema';
// Mock the api client
vi.mock('../../../api/client');

// Mock SearchableSelect to simplify relation field tests
vi.mock('../../../components/form/SearchableSelect', () => ({
  SearchableSelect: ({ value, onChange }: { value: number; onChange: (v: number) => void }) =>
    createElement('select', {
      'data-testid': 'mock-searchable-select',
      value,
      onChange: (e: any) => onChange(Number(e.target.value)),
    }),
}));

// Mock the DynamicForm context
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

// Wrapper that provides FormProvider for field components
function FormWrapper({ children, defaultValues }: { children?: React.ReactNode; defaultValues?: Record<string, unknown> }) {
  const Comp = () => {
    const form = useForm({ defaultValues: defaultValues ?? { fields: {} } });
    return createElement(FormProvider, { ...form, children });
  };
  const Wrapper = createWrapper();
  return createElement(Wrapper, null, createElement(Comp));
}

const fieldWithAiSuggestion: FieldSchema = {
  name: 'summary',
  type: 'Text',
  label: 'Summary',
  required: false,
  has_dynamic_choices_runtime: false,
  has_dynamic_choices_compile_time: false,
  has_logic_sanity_check: false,
  ai_suggestion: { prompt: 'Suggest a summary', source_fields: ['title'] },
};

const fieldWithSanityCheck: FieldSchema = {
  name: 'email',
  type: 'Text',
  label: 'Email',
  required: false,
  has_dynamic_choices_runtime: false,
  has_dynamic_choices_compile_time: false,
  has_logic_sanity_check: false,
  ai_sanity_checks: [{ prompt: 'Check email format', severity: 'Error' }],
};

const fieldWithBoth: FieldSchema = {
  name: 'description',
  type: 'TextArea',
  label: 'Description',
  required: false,
  has_dynamic_choices_runtime: false,
  has_dynamic_choices_compile_time: false,
  has_logic_sanity_check: false,
  ai_suggestion: { prompt: 'Suggest description', source_fields: ['title'] },
  ai_sanity_checks: [{ prompt: 'Check length', severity: 'Warning' }],
};

const plainField: FieldSchema = {
  name: 'title',
  type: 'Text',
  label: 'Title',
  required: true,
  has_dynamic_choices_runtime: false,
  has_dynamic_choices_compile_time: false,
  has_logic_sanity_check: false,
};

const relationFieldWithAi: FieldSchema = {
  name: 'author_ref',
  type: 'Relation',
  label: 'Author',
  required: false,
  has_dynamic_choices_runtime: false,
  has_dynamic_choices_compile_time: false,
  has_logic_sanity_check: false,
  relation_options: { relation_entity_name: 'users', is_relation_entity_not_exists_ok: false, allow_multiple: false },
  ai_relation_suggestion: { top_k: 5 },
};

const relationFieldPlain: FieldSchema = {
  name: 'category_ref',
  type: 'Relation',
  label: 'Category',
  required: false,
  has_dynamic_choices_runtime: false,
  has_dynamic_choices_compile_time: false,
  has_logic_sanity_check: false,
  relation_options: { relation_entity_name: 'categories', is_relation_entity_not_exists_ok: false, allow_multiple: false },
};

describe('FormField AI integration', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('should show AiSuggestButton when field has ai_suggestion and user can update', () => {
    vi.mocked(useEntityFormContext).mockReturnValue({ entityName: 'blog', canUpdate: true });

    render(createElement(FormWrapper, null,
      createElement(FormField, { fieldSchema: fieldWithAiSuggestion }),
    ));

    expect(screen.getByTestId('ai-suggest-summary')).toBeInTheDocument();
  });

  it('should not show AiSuggestButton when user cannot update', () => {
    vi.mocked(useEntityFormContext).mockReturnValue({ entityName: 'blog', canUpdate: false });

    render(createElement(FormWrapper, null,
      createElement(FormField, { fieldSchema: fieldWithAiSuggestion }),
    ));

    expect(screen.queryByTestId('ai-suggest-summary')).not.toBeInTheDocument();
  });

  it('should not show AiSuggestButton when field has no ai_suggestion', () => {
    vi.mocked(useEntityFormContext).mockReturnValue({ entityName: 'blog', canUpdate: true });

    render(createElement(FormWrapper, null,
      createElement(FormField, { fieldSchema: plainField }),
    ));

    expect(screen.queryByTestId('ai-suggest-title')).not.toBeInTheDocument();
  });

  it('should show AiSanityCheckBadge when field has ai_sanity_checks and user can update', () => {
    vi.mocked(useEntityFormContext).mockReturnValue({ entityName: 'blog', canUpdate: true });

    render(createElement(FormWrapper, null,
      createElement(FormField, { fieldSchema: fieldWithSanityCheck }),
    ));

    expect(screen.getByTestId('ai-sanity-check-email')).toBeInTheDocument();
  });

  it('should not show AiSanityCheckBadge when user cannot update', () => {
    vi.mocked(useEntityFormContext).mockReturnValue({ entityName: 'blog', canUpdate: false });

    render(createElement(FormWrapper, null,
      createElement(FormField, { fieldSchema: fieldWithSanityCheck }),
    ));

    expect(screen.queryByTestId('ai-sanity-check-email')).not.toBeInTheDocument();
  });

  it('should show both AI components when field has both', () => {
    vi.mocked(useEntityFormContext).mockReturnValue({ entityName: 'blog', canUpdate: true });

    render(createElement(FormWrapper, null,
      createElement(FormField, { fieldSchema: fieldWithBoth }),
    ));

    expect(screen.getByTestId('ai-suggest-description')).toBeInTheDocument();
    expect(screen.getByTestId('ai-sanity-check-description')).toBeInTheDocument();
  });

  it('should not show AI components when no entity form context', () => {
    vi.mocked(useEntityFormContext).mockReturnValue(null);

    render(createElement(FormWrapper, null,
      createElement(FormField, { fieldSchema: fieldWithAiSuggestion }),
    ));

    expect(screen.queryByTestId('ai-suggest-summary')).not.toBeInTheDocument();
  });
});

describe('RelationField AI integration', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('should show AiRelationSuggestions when field has ai_relation_suggestion and user can update', () => {
    vi.mocked(useEntityFormContext).mockReturnValue({ entityName: 'blog', canUpdate: true });

    render(createElement(FormWrapper, { defaultValues: { fields: { author_ref: -1 } } },
      createElement(RelationField, { schema: relationFieldWithAi, path: 'fields.author_ref' }),
    ));

    expect(screen.getByTestId('ai-relation-suggest-button-author_ref')).toBeInTheDocument();
  });

  it('should not show AiRelationSuggestions when user cannot update', () => {
    vi.mocked(useEntityFormContext).mockReturnValue({ entityName: 'blog', canUpdate: false });

    render(createElement(FormWrapper, { defaultValues: { fields: { author_ref: -1 } } },
      createElement(RelationField, { schema: relationFieldWithAi, path: 'fields.author_ref' }),
    ));

    expect(screen.queryByTestId('ai-relation-suggest-button-author_ref')).not.toBeInTheDocument();
  });

  it('should not show AiRelationSuggestions when field has no ai_relation_suggestion', () => {
    vi.mocked(useEntityFormContext).mockReturnValue({ entityName: 'blog', canUpdate: true });

    render(createElement(FormWrapper, { defaultValues: { fields: { category_ref: -1 } } },
      createElement(RelationField, { schema: relationFieldPlain, path: 'fields.category_ref' }),
    ));

    expect(screen.queryByTestId('ai-relation-suggest-button-category_ref')).not.toBeInTheDocument();
  });
});
