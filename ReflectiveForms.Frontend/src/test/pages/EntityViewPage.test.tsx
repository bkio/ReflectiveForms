import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { EntityViewPage } from '../../pages/EntityViewPage';

// Mock the hooks
vi.mock('../../hooks/useEntity', () => ({
  useSchema: vi.fn(),
  useEntity: vi.fn(),
  useEntityList: vi.fn(),
  useCapabilities: vi.fn(() => ({ data: undefined })),
  useEntityHistory: vi.fn(() => ({ data: undefined })),
}));

import { useSchema, useEntity, useEntityList } from '../../hooks/useEntity';

const mockSchema = {
  entity_name: 'test_entity',
  readable_name: { singular: 'Test Entity', plural: 'Test Entities' },
  features: {
    has_author: false,
    has_tags: false,
    has_categories: false,
    has_parent_child: false,
    require_title_uniqueness: false,
    supports_frontend_edit: true,
    show_in_navigation: true,
  },
  fields: [
    {
      name: 'description',
      type: 'TextArea',
      label: 'Description',
      required: false,
      has_dynamic_choices_runtime: false,
      has_dynamic_choices_compile_time: false,
      has_logic_sanity_check: false,
    },
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
    {
      name: 'is_active',
      type: 'Checkbox',
      label: 'Active',
      required: false,
      has_dynamic_choices_runtime: false,
      has_dynamic_choices_compile_time: false,
      has_logic_sanity_check: false,
    },
    {
      name: 'count',
      type: 'Number',
      label: 'Count',
      required: false,
      has_dynamic_choices_runtime: false,
      has_dynamic_choices_compile_time: false,
      has_logic_sanity_check: false,
    },
    {
      name: 'start_date',
      type: 'DatePicker',
      label: 'Start Date',
      required: false,
      has_dynamic_choices_runtime: false,
      has_dynamic_choices_compile_time: false,
      has_logic_sanity_check: false,
    },
    {
      name: 'website',
      type: 'Url',
      label: 'Website',
      required: false,
      has_dynamic_choices_runtime: false,
      has_dynamic_choices_compile_time: false,
      has_logic_sanity_check: false,
    },
    {
      name: 'email',
      type: 'Email',
      label: 'Contact Email',
      required: false,
      has_dynamic_choices_runtime: false,
      has_dynamic_choices_compile_time: false,
      has_logic_sanity_check: false,
    },
    {
      name: 'content',
      type: 'WysiwygEditor',
      label: 'Content',
      required: false,
      has_dynamic_choices_runtime: false,
      has_dynamic_choices_compile_time: false,
      has_logic_sanity_check: false,
    },
  ],
  api_endpoints: {
    crud: '/rf/api/crud',
    sanity_check: '/rf/api/sanity_check',
    entity_lock: '/rf/api/entity_lock',
    media: '/rf/api/media',
  },
  schema_version: '1.0',
};

const mockEntityData = {
  id: 42,
  slug: 'test-entity',
  title: { rendered: 'My Test Entity' },
  date: '2026-01-01',
  date_gmt: '2026-01-01',
  modified: '2026-03-29',
  modified_gmt: '2026-03-29',
  fields: {
    description: 'This is a test description',
    status: 'published',
    is_active: true,
    count: 42,
    start_date: '20260329',
    website: 'https://example.com',
    email: 'test@example.com',
    content: '<p>Rich <strong>content</strong></p>',
  },
};

function renderViewPage(id = '42') {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });

  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[`/entities-view/test_entity?id=${id}`]}>
        <Routes>
          <Route path="/entities-view/:entityName" element={<EntityViewPage />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('EntityViewPage', () => {
  beforeEach(() => {
    vi.mocked(useSchema).mockReturnValue({
      data: mockSchema,
      isLoading: false,
      error: null,
    } as ReturnType<typeof useSchema>);

    vi.mocked(useEntity).mockReturnValue({
      data: mockEntityData,
      isLoading: false,
      error: null,
    } as unknown as ReturnType<typeof useEntity>);

    vi.mocked(useEntityList).mockReturnValue({
      data: undefined,
      isLoading: false,
      error: null,
    } as unknown as ReturnType<typeof useEntityList>);
  });

  it('renders entity title', () => {
    renderViewPage();
    expect(screen.getByText('My Test Entity')).toBeInTheDocument();
  });

  it('renders entity ID', () => {
    renderViewPage();
    expect(screen.getByText(/ID: 42/)).toBeInTheDocument();
  });

  it('renders readable name', () => {
    renderViewPage();
    expect(screen.getByText(/Test Entity — ID: 42/)).toBeInTheDocument();
  });

  it('renders text field value', () => {
    renderViewPage();
    expect(screen.getByText('This is a test description')).toBeInTheDocument();
  });

  it('renders select field with label', () => {
    renderViewPage();
    expect(screen.getByText('Published')).toBeInTheDocument();
  });

  it('renders checkbox as Yes/No badge', () => {
    renderViewPage();
    expect(screen.getByText('Yes')).toBeInTheDocument();
  });

  it('renders number value', () => {
    renderViewPage();
    expect(screen.getByText('42')).toBeInTheDocument();
  });

  it('renders date in formatted form', () => {
    renderViewPage();
    expect(screen.getByText('2026-03-29')).toBeInTheDocument();
  });

  it('renders URL as clickable link', () => {
    renderViewPage();
    const link = screen.getByText('https://example.com');
    expect(link).toBeInTheDocument();
    expect(link.closest('a')).toHaveAttribute('href', 'https://example.com');
    expect(link.closest('a')).toHaveAttribute('target', '_blank');
  });

  it('renders email as mailto link', () => {
    renderViewPage();
    const link = screen.getByText('test@example.com');
    expect(link).toBeInTheDocument();
    expect(link.closest('a')).toHaveAttribute('href', 'mailto:test@example.com');
  });

  it('renders WYSIWYG content as HTML', () => {
    renderViewPage();
    // The sanitized HTML should render
    expect(screen.getByText(/Rich/)).toBeInTheDocument();
  });

  it('renders Edit link', () => {
    renderViewPage();
    const editLink = screen.getByTitle('Edit');
    expect(editLink).toBeInTheDocument();
    expect(editLink).toHaveAttribute('href', '/entities-admin/test_entity?id=42');
  });

  it('renders Back to list link', () => {
    renderViewPage();
    const backLink = screen.getByTitle('Back to list');
    expect(backLink).toBeInTheDocument();
    expect(backLink).toHaveAttribute('href', '/entities/test_entity');
  });

  it('shows loading spinner when schema is loading', () => {
    vi.mocked(useSchema).mockReturnValue({
      data: undefined,
      isLoading: true,
      error: null,
    } as ReturnType<typeof useSchema>);

    renderViewPage();
    expect(screen.getByText((_, el) => el?.classList.contains('animate-spin') ?? false)).toBeInTheDocument();
  });

  it('shows error message on failure', () => {
    vi.mocked(useSchema).mockReturnValue({
      data: undefined,
      isLoading: false,
      error: new Error('Failed to load'),
    } as ReturnType<typeof useSchema>);

    renderViewPage();
    expect(screen.getByText('Failed to load')).toBeInTheDocument();
  });

  it('shows "Entity not found" when no data', () => {
    vi.mocked(useSchema).mockReturnValue({
      data: mockSchema,
      isLoading: false,
      error: null,
    } as ReturnType<typeof useSchema>);

    vi.mocked(useEntity).mockReturnValue({
      data: undefined,
      isLoading: false,
      error: null,
    } as unknown as ReturnType<typeof useEntity>);

    renderViewPage();
    expect(screen.getByText('Entity not found')).toBeInTheDocument();
  });

  it('shows "Not set" for empty field values', () => {
    vi.mocked(useEntity).mockReturnValue({
      data: {
        ...mockEntityData,
        fields: { description: '', status: '', is_active: false, count: 0, start_date: '', website: '', email: '', content: '' },
      },
      isLoading: false,
      error: null,
    } as unknown as ReturnType<typeof useEntity>);

    renderViewPage();
    // Several "Not set" should appear for empty string fields
    const notSets = screen.getAllByText('Not set');
    expect(notSets.length).toBeGreaterThan(0);
  });

  it('renders checkbox "No" for false values', () => {
    vi.mocked(useEntity).mockReturnValue({
      data: {
        ...mockEntityData,
        fields: { ...mockEntityData.fields, is_active: false },
      },
      isLoading: false,
      error: null,
    } as unknown as ReturnType<typeof useEntity>);

    renderViewPage();
    expect(screen.getByText('No')).toBeInTheDocument();
  });

  it('does not render metadata section when no features enabled', () => {
    renderViewPage();
    expect(screen.queryByTestId('metadata-section')).not.toBeInTheDocument();
  });

  it('renders metadata section with author when has_author is true', () => {
    const schemaWithAuthor = {
      ...mockSchema,
      features: { ...mockSchema.features, has_author: true },
    };
    vi.mocked(useSchema).mockReturnValue({
      data: schemaWithAuthor,
      isLoading: false,
      error: null,
    } as ReturnType<typeof useSchema>);
    vi.mocked(useEntity).mockReturnValue({
      data: { ...mockEntityData, author: 5 },
      isLoading: false,
      error: null,
    } as unknown as ReturnType<typeof useEntity>);
    vi.mocked(useEntityList).mockReturnValue({
      data: [{ id: 5, title: 'John Doe' }],
      isLoading: false,
      error: null,
    } as unknown as ReturnType<typeof useEntityList>);

    renderViewPage();
    expect(screen.getByTestId('metadata-section')).toBeInTheDocument();
    expect(screen.getByTestId('metadata-author')).toBeInTheDocument();
    expect(screen.getByText('John Doe')).toBeInTheDocument();
  });

  it('renders metadata section with tags when has_tags is true', () => {
    const schemaWithTags = {
      ...mockSchema,
      features: { ...mockSchema.features, has_tags: true },
    };
    vi.mocked(useSchema).mockReturnValue({
      data: schemaWithTags,
      isLoading: false,
      error: null,
    } as ReturnType<typeof useSchema>);
    vi.mocked(useEntity).mockReturnValue({
      data: { ...mockEntityData, tags: [1, 2] },
      isLoading: false,
      error: null,
    } as unknown as ReturnType<typeof useEntity>);
    vi.mocked(useEntityList).mockReturnValue({
      data: [{ id: 1, title: 'Tag A' }, { id: 2, title: 'Tag B' }],
      isLoading: false,
      error: null,
    } as unknown as ReturnType<typeof useEntityList>);

    renderViewPage();
    expect(screen.getByTestId('metadata-tags')).toBeInTheDocument();
    expect(screen.getByText('Tag A')).toBeInTheDocument();
    expect(screen.getByText('Tag B')).toBeInTheDocument();
  });

  it('renders metadata section with categories when has_categories is true', () => {
    const schemaWithCategories = {
      ...mockSchema,
      features: { ...mockSchema.features, has_categories: true },
    };
    vi.mocked(useSchema).mockReturnValue({
      data: schemaWithCategories,
      isLoading: false,
      error: null,
    } as ReturnType<typeof useSchema>);
    vi.mocked(useEntity).mockReturnValue({
      data: { ...mockEntityData, categories: [10, 20] },
      isLoading: false,
      error: null,
    } as unknown as ReturnType<typeof useEntity>);
    vi.mocked(useEntityList).mockReturnValue({
      data: [{ id: 10, title: 'Cat X' }, { id: 20, title: 'Cat Y' }],
      isLoading: false,
      error: null,
    } as unknown as ReturnType<typeof useEntityList>);

    renderViewPage();
    expect(screen.getByTestId('metadata-categories')).toBeInTheDocument();
    expect(screen.getByText('Cat X')).toBeInTheDocument();
    expect(screen.getByText('Cat Y')).toBeInTheDocument();
  });

  it('renders metadata section with parent when has_parent_child is true', () => {
    const schemaWithParent = {
      ...mockSchema,
      features: { ...mockSchema.features, has_parent_child: true },
    };
    vi.mocked(useSchema).mockReturnValue({
      data: schemaWithParent,
      isLoading: false,
      error: null,
    } as ReturnType<typeof useSchema>);
    vi.mocked(useEntity).mockReturnValue({
      data: { ...mockEntityData, parent: 7 },
      isLoading: false,
      error: null,
    } as unknown as ReturnType<typeof useEntity>);
    vi.mocked(useEntityList).mockReturnValue({
      data: [{ id: 7, title: 'Parent Item' }],
      isLoading: false,
      error: null,
    } as unknown as ReturnType<typeof useEntityList>);

    renderViewPage();
    expect(screen.getByTestId('metadata-parent')).toBeInTheDocument();
    expect(screen.getByText('Parent Item')).toBeInTheDocument();
  });

  it('top-level fields have card wrapper, nested fields do not', () => {
    const schemaWithGroup = {
      ...mockSchema,
      fields: [
        {
          name: 'address',
          type: 'Group',
          label: 'Address',
          required: false,
          has_dynamic_choices_runtime: false,
          has_dynamic_choices_compile_time: false,
          has_logic_sanity_check: false,
          group_options: {
            child_schema: [
              {
                name: 'street',
                type: 'Text',
                label: 'Street',
                required: false,
                has_dynamic_choices_runtime: false,
                has_dynamic_choices_compile_time: false,
                has_logic_sanity_check: false,
              },
            ],
            render_style: 'Full',
          },
        },
      ],
    };
    vi.mocked(useSchema).mockReturnValue({
      data: schemaWithGroup,
      isLoading: false,
      error: null,
    } as ReturnType<typeof useSchema>);
    vi.mocked(useEntity).mockReturnValue({
      data: { ...mockEntityData, fields: { address: { street: '123 Main St' } } },
      isLoading: false,
      error: null,
    } as unknown as ReturnType<typeof useEntity>);

    renderViewPage();

    // Top-level field-view for Address should have the card wrapper
    const addressField = screen.getByText('Address').closest('.field-view');
    expect(addressField).toBeTruthy();
    const addressCard = addressField!.querySelector('.bg-white.rounded-lg');
    expect(addressCard).toBeTruthy();

    // Nested street field should NOT have the card wrapper
    const streetField = screen.getByText('Street').closest('.field-view');
    expect(streetField).toBeTruthy();
    const streetCard = streetField!.querySelector('.bg-white.rounded-lg');
    expect(streetCard).toBeNull();
  });

  it('group fields use grid layout based on render_style', () => {
    const schemaWithGrid = {
      ...mockSchema,
      fields: [
        {
          name: 'details',
          type: 'Group',
          label: 'Details',
          required: false,
          has_dynamic_choices_runtime: false,
          has_dynamic_choices_compile_time: false,
          has_logic_sanity_check: false,
          group_options: {
            child_schema: [
              {
                name: 'first',
                type: 'Text',
                label: 'First',
                required: false,
                has_dynamic_choices_runtime: false,
                has_dynamic_choices_compile_time: false,
                has_logic_sanity_check: false,
              },
              {
                name: 'second',
                type: 'Text',
                label: 'Second',
                required: false,
                has_dynamic_choices_runtime: false,
                has_dynamic_choices_compile_time: false,
                has_logic_sanity_check: false,
              },
            ],
            render_style: 'Grid2',
          },
        },
      ],
    };
    vi.mocked(useSchema).mockReturnValue({
      data: schemaWithGrid,
      isLoading: false,
      error: null,
    } as ReturnType<typeof useSchema>);
    vi.mocked(useEntity).mockReturnValue({
      data: { ...mockEntityData, fields: { details: { first: 'A', second: 'B' } } },
      isLoading: false,
      error: null,
    } as unknown as ReturnType<typeof useEntity>);

    renderViewPage();

    // The group container should have grid classes
    const gridContainer = screen.getByText('First').closest('.grid');
    expect(gridContainer).toBeTruthy();
    expect(gridContainer!.className).toContain('md:grid-cols-2');
  });

  it('repeater items show structured headers', () => {
    const schemaWithRepeater = {
      ...mockSchema,
      fields: [
        {
          name: 'contacts',
          type: 'Repeater',
          label: 'Contacts',
          required: false,
          has_dynamic_choices_runtime: false,
          has_dynamic_choices_compile_time: false,
          has_logic_sanity_check: false,
          repeater_options: {
            item_schema: [
              {
                name: 'name',
                type: 'Text',
                label: 'Name',
                required: false,
                has_dynamic_choices_runtime: false,
                has_dynamic_choices_compile_time: false,
                has_logic_sanity_check: false,
              },
            ],
            min_items: 0,
            max_items: 10,
            add_button_label: 'Add',
            use_accordion: false,
            render_style: 'Full',
          },
        },
      ],
    };
    vi.mocked(useSchema).mockReturnValue({
      data: schemaWithRepeater,
      isLoading: false,
      error: null,
    } as ReturnType<typeof useSchema>);
    vi.mocked(useEntity).mockReturnValue({
      data: { ...mockEntityData, fields: { contacts: [{ name: 'John' }, { name: 'Jane' }] } },
      isLoading: false,
      error: null,
    } as unknown as ReturnType<typeof useEntity>);

    renderViewPage();

    expect(screen.getByText('Contacts #1')).toBeInTheDocument();
    expect(screen.getByText('Contacts #2')).toBeInTheDocument();
  });

  it('relation field shows resolved name as link', () => {
    const schemaWithRelation = {
      ...mockSchema,
      fields: [
        {
          name: 'related_post',
          type: 'Relation',
          label: 'Related Post',
          required: false,
          has_dynamic_choices_runtime: false,
          has_dynamic_choices_compile_time: false,
          has_logic_sanity_check: false,
          relation_options: {
            relation_entity_name: 'blog-post',
            is_relation_entity_not_exists_ok: false,
            allow_multiple: false,
          },
        },
      ],
    };
    vi.mocked(useSchema).mockReturnValue({
      data: schemaWithRelation,
      isLoading: false,
      error: null,
    } as ReturnType<typeof useSchema>);
    vi.mocked(useEntity).mockReturnValue({
      data: { ...mockEntityData, fields: { related_post: 99 } },
      isLoading: false,
      error: null,
    } as unknown as ReturnType<typeof useEntity>);
    vi.mocked(useEntityList).mockReturnValue({
      data: [{ id: 99, title: 'My Blog Post' }],
      isLoading: false,
      error: null,
    } as unknown as ReturnType<typeof useEntityList>);

    renderViewPage();

    const link = screen.getByText('My Blog Post');
    expect(link).toBeInTheDocument();
    expect(link.closest('a')).toHaveAttribute('href', '/entities-view/blog-post?id=99');
  });
});
