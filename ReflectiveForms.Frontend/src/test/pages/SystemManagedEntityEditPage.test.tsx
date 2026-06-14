import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { EntityEditPage } from '../../pages/EntityEditPage';

vi.mock('../../hooks/useEntity', () => ({
  useSchema: vi.fn(),
  useEntity: vi.fn(),
  useCapabilities: vi.fn(() => ({ data: undefined })),
  useEntityHistory: vi.fn(() => ({ data: undefined })),
}));

vi.mock('../../components/form/DynamicForm', () => ({
  DynamicForm: () => <div data-testid="dynamic-form">DynamicForm rendered</div>,
}));

import { useSchema, useEntity } from '../../hooks/useEntity';

const mockSchema = {
  entity_name: 'users',
  readable_name: { singular: 'User', plural: 'Users' },
  features: {
    has_author: false,
    has_tags: false,
    has_categories: false,
    has_parent_child: false,
    require_title_uniqueness: true,
    supports_frontend_edit: true,
    show_in_navigation: true,
  },
  fields: [
    {
      name: 'email_address',
      type: 'Email',
      label: 'Email',
      required: true,
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

const systemManagedEntity = {
  id: 1,
  slug: 'root-user',
  title: { rendered: 'Root User' },
  date: '2026-01-01',
  date_gmt: '2026-01-01',
  modified: '2026-04-10',
  modified_gmt: '2026-04-10',
  fields: { email_address: 'admin@example.com' },
  is_system_managed: true,
};

function renderEditPage(id: string) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });

  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[`/entities-admin/users?id=${id}`]}>
        <Routes>
          <Route path="/entities-admin/:entityName" element={<EntityEditPage />} />
          <Route
            path="/entities-view/:entityName"
            element={<div data-testid="view-page">Redirected to View Page</div>}
          />
          <Route
            path="/entities/:entityName"
            element={<div data-testid="list-page">Redirected to List Page</div>}
          />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('EntityEditPage — System Managed Entity Redirect', () => {
  beforeEach(() => {
    vi.mocked(useSchema).mockReturnValue({
      data: mockSchema,
      isLoading: false,
      error: null,
    } as ReturnType<typeof useSchema>);
  });

  it('redirects to view page when editing a system-managed entity', () => {
    vi.mocked(useEntity).mockReturnValue({
      data: systemManagedEntity,
      isLoading: false,
      error: null,
    } as unknown as ReturnType<typeof useEntity>);

    renderEditPage('1');
    expect(screen.getByTestId('view-page')).toBeInTheDocument();
  });

  it('does not redirect when entity is not system-managed', () => {
    vi.mocked(useEntity).mockReturnValue({
      data: { ...systemManagedEntity, id: 2, is_system_managed: undefined },
      isLoading: false,
      error: null,
    } as unknown as ReturnType<typeof useEntity>);

    renderEditPage('2');
    expect(screen.queryByTestId('view-page')).not.toBeInTheDocument();
    expect(screen.getByTestId('dynamic-form')).toBeInTheDocument();
  });
});
