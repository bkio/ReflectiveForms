import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { EntityViewPage } from '../../pages/EntityViewPage';

// Mock the hooks
vi.mock('../../hooks/useEntity', () => ({
  useSchema: vi.fn(),
  useEntity: vi.fn(),
}));

import { useSchema, useEntity } from '../../hooks/useEntity';

const mockSchema = {
  entity_name: 'test_entity',
  readable_name: { singular: 'Test Entity', plural: 'Test Entities' },
  features: {
    has_author: false,
    has_tags: false,
    has_categories: false,
    has_parent_child: false,
    require_title_uniqueness: false,
    supports_frontend_edit: 'ForAllAuthorized',
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
});
