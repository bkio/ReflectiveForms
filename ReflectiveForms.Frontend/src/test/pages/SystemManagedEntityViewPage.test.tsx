import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { EntityViewPage } from '../../pages/EntityViewPage';

vi.mock('../../hooks/useEntity', () => ({
  useSchema: vi.fn(),
  useEntity: vi.fn(),
  useEntityList: vi.fn(),
  useCapabilities: vi.fn(() => ({ data: undefined })),
  useEntityHistory: vi.fn(() => ({ data: undefined })),
}));

import { useSchema, useEntity, useEntityList } from '../../hooks/useEntity';

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

const regularEntity = {
  id: 2,
  slug: 'regular-user',
  title: { rendered: 'Regular User' },
  date: '2026-01-02',
  date_gmt: '2026-01-02',
  modified: '2026-04-11',
  modified_gmt: '2026-04-11',
  fields: { email_address: 'user@example.com' },
};

function renderViewPage(entityData: typeof systemManagedEntity | typeof regularEntity) {
  vi.mocked(useEntity).mockReturnValue({
    data: entityData,
    isLoading: false,
    error: null,
  } as unknown as ReturnType<typeof useEntity>);

  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });

  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[`/entities-view/users?id=${entityData.id}`]}>
        <Routes>
          <Route path="/entities-view/:entityName" element={<EntityViewPage />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('EntityViewPage — System Managed Entities', () => {
  beforeEach(() => {
    vi.mocked(useSchema).mockReturnValue({
      data: mockSchema,
      isLoading: false,
      error: null,
    } as ReturnType<typeof useSchema>);

    vi.mocked(useEntityList).mockReturnValue({
      data: undefined,
      isLoading: false,
      error: null,
    } as unknown as ReturnType<typeof useEntityList>);
  });

  it('hides Edit button for system-managed entity', () => {
    renderViewPage(systemManagedEntity);
    expect(screen.queryByTitle('Edit')).not.toBeInTheDocument();
  });

  it('shows Edit button for non-system-managed entity', () => {
    renderViewPage(regularEntity);
    expect(screen.getByTitle('Edit')).toBeInTheDocument();
    expect(screen.getByTitle('Edit')).toHaveAttribute('href', '/entities-admin/users?id=2');
  });

  it('still renders entity title for system-managed entity', () => {
    renderViewPage(systemManagedEntity);
    expect(screen.getByText('Root User')).toBeInTheDocument();
  });

  it('still renders entity fields for system-managed entity', () => {
    renderViewPage(systemManagedEntity);
    expect(screen.getByText('admin@example.com')).toBeInTheDocument();
  });
});
