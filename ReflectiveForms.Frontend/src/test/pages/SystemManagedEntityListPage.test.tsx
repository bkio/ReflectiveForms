import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { EntityListPage } from '../../pages/EntityListPage';

vi.mock('../../hooks/useEntity', () => ({
  useSchema: vi.fn(),
  useAllSchemas: vi.fn(() => ({ data: undefined })),
  useEntityList: vi.fn(),
  useDeleteEntity: vi.fn(),
  useCapabilities: vi.fn(() => ({ data: undefined, isLoading: false, isSuccess: false })),
}));

vi.mock('sonner', () => ({
  toast: { success: vi.fn(), error: vi.fn() },
}));

import { useSchema, useEntityList, useDeleteEntity } from '../../hooks/useEntity';

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
    has_individual_sharing: false,
    custom_frontend_list_route: null,
  },
  fields: [],
  api_endpoints: {
    crud: '/rf/api/crud',
    sanity_check: '/rf/api/sanity_check',
    entity_lock: '/rf/api/entity_lock',
    media: '/rf/api/media',
  },
  schema_version: '1.0',
};

const mixedEntities = [
  { id: 1, title: 'Root User', modified: '2026-04-10T12:00:00Z', is_system_managed: true },
  { id: 2, title: 'Regular User', modified: '2026-04-11T12:00:00Z' },
  { id: 3, title: 'Another User', modified: '2026-04-12T12:00:00Z', is_system_managed: false },
];

function renderList(entityName = 'users') {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });

  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[`/entities/${entityName}`]}>
        <Routes>
          <Route path="/entities/:entityName" element={<EntityListPage />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('EntityListPage — System Managed Entities', () => {
  beforeEach(() => {
    vi.mocked(useSchema).mockReturnValue({
      data: mockSchema,
      isLoading: false,
      error: null,
    } as unknown as ReturnType<typeof useSchema>);

    vi.mocked(useEntityList).mockReturnValue({
      data: mixedEntities,
      isLoading: false,
      error: null,
    } as unknown as ReturnType<typeof useEntityList>);

    vi.mocked(useDeleteEntity).mockReturnValue({
      mutateAsync: vi.fn().mockResolvedValue({}),
    } as unknown as ReturnType<typeof useDeleteEntity>);
  });

  it('shows "System" badge for system-managed entities', () => {
    renderList();
    expect(screen.getByTestId('system-badge-1')).toBeInTheDocument();
    expect(screen.getByTestId('system-badge-1')).toHaveTextContent('System');
  });

  it('does not show "System" badge for non-system-managed entities', () => {
    renderList();
    expect(screen.queryByTestId('system-badge-2')).not.toBeInTheDocument();
    expect(screen.queryByTestId('system-badge-3')).not.toBeInTheDocument();
  });

  it('hides Edit button for system-managed entities', () => {
    renderList();
    const systemRow = screen.getByTestId('entity-row-1');
    const editLinks = systemRow.querySelectorAll('a[title="Edit"]');
    expect(editLinks.length).toBe(0);
  });

  it('shows Edit button for non-system-managed entities', () => {
    renderList();
    const regularRow = screen.getByTestId('entity-row-2');
    const editLink = regularRow.querySelector('a[title="Edit"]');
    expect(editLink).toBeInTheDocument();
  });

  it('hides Delete button for system-managed entities', () => {
    renderList();
    const systemRow = screen.getByTestId('entity-row-1');
    const deleteBtn = systemRow.querySelector('button[title="Delete"]');
    expect(deleteBtn).not.toBeInTheDocument();
  });

  it('shows Delete button for non-system-managed entities', () => {
    renderList();
    const regularRow = screen.getByTestId('entity-row-2');
    const deleteBtn = regularRow.querySelector('button[title="Delete"]');
    expect(deleteBtn).toBeInTheDocument();
  });

  it('hides Clone button for system-managed entities', () => {
    renderList();
    const systemRow = screen.getByTestId('entity-row-1');
    const cloneLink = systemRow.querySelector('a[title="Clone"]');
    expect(cloneLink).not.toBeInTheDocument();
  });

  it('shows Clone button for non-system-managed entities', () => {
    renderList();
    const regularRow = screen.getByTestId('entity-row-2');
    const cloneLink = regularRow.querySelector('a[title="Clone"]');
    expect(cloneLink).toBeInTheDocument();
  });

  it('system-managed entity title links to view page instead of edit page', () => {
    renderList();
    const systemRow = screen.getByTestId('entity-row-1');
    const titleLink = systemRow.querySelector('a.text-blue-600');
    expect(titleLink?.getAttribute('href')).toBe('/entities-view/users?id=1');
  });

  it('non-system-managed entity title links to edit page', () => {
    renderList();
    const regularRow = screen.getByTestId('entity-row-2');
    const titleLink = regularRow.querySelector('a.text-blue-600');
    expect(titleLink?.getAttribute('href')).toBe('/entities-admin/users?id=2');
  });

  it('still shows View button for system-managed entities', () => {
    renderList();
    const systemRow = screen.getByTestId('entity-row-1');
    const viewLink = systemRow.querySelector('a[title="View"]');
    expect(viewLink).toBeInTheDocument();
  });
});
