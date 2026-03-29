import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { DynamicForm } from '../../components/form/DynamicForm';
import { EntitySchema } from '../../types/schema';
import * as client from '../../api/client';

// Mock the API client
vi.mock('../../api/client');

// Mock sonner toast
vi.mock('sonner', () => ({
  toast: {
    info: vi.fn(),
    success: vi.fn(),
    error: vi.fn(),
  },
}));

const createMockSchema = (fields: EntitySchema['fields'] = []): EntitySchema => ({
  entity_name: 'TestEntity',
  readable_name: {
    singular: 'Test Entity',
    plural: 'Test Entities',
  },
  features: {
    has_author: false,
    has_tags: false,
    has_categories: false,
    has_parent_child: false,
    require_title_uniqueness: false,
    supports_frontend_edit: 'ForAllAuthorized',
  },
  fields,
  api_endpoints: {
    crud: '/rf/api/crud',
    sanity_check: '/rf/api/sanity_check',
    entity_lock: '/rf/api/entity_lock',
    media: '/rf/api/media',
  },
  schema_version: '1.0.0',
});

function renderWithProviders(ui: React.ReactElement) {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });

  return render(
    <QueryClientProvider client={queryClient}>{ui}</QueryClientProvider>
  );
}

describe('DynamicForm', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('should render title field', () => {
    const schema = createMockSchema();

    renderWithProviders(<DynamicForm schema={schema} />);

    expect(screen.getByText(/title/i)).toBeInTheDocument();
  });

  it('should render text field from schema', () => {
    const schema = createMockSchema([
      {
        name: 'description',
        type: 'Text',
        label: 'Description',
        required: false,
        has_dynamic_choices_runtime: false,
        has_dynamic_choices_compile_time: false,
        has_logic_sanity_check: false,
        text_options: {
          placeholder: 'Enter description',
          is_multiline: false,
        },
      },
    ]);

    renderWithProviders(<DynamicForm schema={schema} />);

    expect(screen.getByText('Description')).toBeInTheDocument();
  });

  it('should render textarea field from schema', () => {
    const schema = createMockSchema([
      {
        name: 'content',
        type: 'TextArea',
        label: 'Content',
        required: false,
        has_dynamic_choices_runtime: false,
        has_dynamic_choices_compile_time: false,
        has_logic_sanity_check: false,
        text_options: {
          is_multiline: true,
        },
      },
    ]);

    renderWithProviders(<DynamicForm schema={schema} />);

    expect(screen.getByText('Content')).toBeInTheDocument();
  });

  it('should render select field with options', () => {
    const schema = createMockSchema([
      {
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
      },
    ]);

    renderWithProviders(<DynamicForm schema={schema} />);

    expect(screen.getByText('Status')).toBeInTheDocument();
    // SearchableChoicesSelect renders a button trigger, not a native <select>
    expect(screen.getByRole('button', { name: /draft/i })).toBeInTheDocument();
  });

  it('should render checkbox field', () => {
    const schema = createMockSchema([
      {
        name: 'is_featured',
        type: 'Checkbox',
        label: 'Featured',
        required: false,
        has_dynamic_choices_runtime: false,
        has_dynamic_choices_compile_time: false,
        has_logic_sanity_check: false,
      },
    ]);

    renderWithProviders(<DynamicForm schema={schema} />);

    expect(screen.getAllByText('Featured').length).toBeGreaterThan(0);
    expect(screen.getByRole('checkbox')).toBeInTheDocument();
  });

  it('should render number field', () => {
    const schema = createMockSchema([
      {
        name: 'quantity',
        type: 'Number',
        label: 'Quantity',
        required: false,
        has_dynamic_choices_runtime: false,
        has_dynamic_choices_compile_time: false,
        has_logic_sanity_check: false,
        number_options: {
          min: 0,
          max: 100,
          step: 1,
          is_range: false,
        },
      },
    ]);

    renderWithProviders(<DynamicForm schema={schema} />);

    expect(screen.getByText('Quantity')).toBeInTheDocument();
    expect(screen.getByRole('spinbutton')).toBeInTheDocument();
  });

  it('should render group field with nested fields', () => {
    const schema = createMockSchema([
      {
        name: 'metadata',
        type: 'Group',
        label: 'Metadata',
        required: false,
        has_dynamic_choices_runtime: false,
        has_dynamic_choices_compile_time: false,
        has_logic_sanity_check: false,
        group_options: {
          child_schema: [
            {
              name: 'author',
              type: 'Text',
              label: 'Author',
              required: false,
              has_dynamic_choices_runtime: false,
              has_dynamic_choices_compile_time: false,
              has_logic_sanity_check: false,
            },
          ],
          render_style: 'Full',
        },
      },
    ]);

    renderWithProviders(<DynamicForm schema={schema} />);

    expect(screen.getByText('Metadata')).toBeInTheDocument();
  });

  it('should show lock warning when entity is locked', async () => {
    vi.mocked(client.tryLockEntity).mockResolvedValue({
      error: 'Entity is locked by another user',
    });

    const schema = createMockSchema();

    renderWithProviders(
      <DynamicForm schema={schema} entityId={1} />
    );

    await waitFor(() => {
      expect(screen.getByText(/this entity is locked/i)).toBeInTheDocument();
    });
  });

  it('should populate form with initial data', () => {
    const schema = createMockSchema([
      {
        name: 'description',
        type: 'Text',
        label: 'Description',
        required: false,
        has_dynamic_choices_runtime: false,
        has_dynamic_choices_compile_time: false,
        has_logic_sanity_check: false,
      },
    ]);

    const initialData = {
      id: 1,
      title: { rendered: 'Existing Entity' },
      fields: { description: 'Existing description' },
    };

    renderWithProviders(
      <DynamicForm schema={schema} initialData={initialData} entityId={1} />
    );

    expect(screen.getByDisplayValue('Existing Entity')).toBeInTheDocument();
  });

  it('should handle conditional field visibility', () => {
    const schema = createMockSchema([
      {
        name: 'show_extra',
        type: 'Checkbox',
        label: 'Show Extra Field',
        required: false,
        has_dynamic_choices_runtime: false,
        has_dynamic_choices_compile_time: false,
        has_logic_sanity_check: false,
      },
      {
        name: 'extra_field',
        type: 'Text',
        label: 'Extra Field',
        required: false,
        display_condition: 'show_extra == true',
        has_dynamic_choices_runtime: false,
        has_dynamic_choices_compile_time: false,
        has_logic_sanity_check: false,
      },
    ]);

    renderWithProviders(<DynamicForm schema={schema} />);

    // Initially hidden
    expect(screen.queryByText('Extra Field')).not.toBeInTheDocument();
  });
});
