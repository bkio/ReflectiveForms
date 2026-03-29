import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { EntityListPage } from '../../pages/EntityListPage';

vi.mock('../../hooks/useEntity', () => ({
  useSchema: vi.fn(),
  useEntityList: vi.fn(),
  useDeleteEntity: vi.fn(),
}));

vi.mock('sonner', () => ({
  toast: { success: vi.fn(), error: vi.fn() },
}));

import { useSchema, useEntityList, useDeleteEntity } from '../../hooks/useEntity';

const mockSchema = {
  entity_name: 'blog-post',
  readable_name: { singular: 'Blog Post', plural: 'Blog Posts' },
  features: {
    has_author: true,
    has_tags: true,
    has_categories: false,
    has_parent_child: false,
    require_title_uniqueness: false,
    supports_frontend_edit: true,
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

function makeEntities(count: number) {
  return Array.from({ length: count }, (_, i) => ({
    id: i + 1,
    title: `Post ${String.fromCharCode(65 + (i % 26))}${i}`,
    author: `Author ${i % 3 === 0 ? 'Alice' : i % 3 === 1 ? 'Bob' : 'Carol'}`,
    modified: `2026-03-${String(10 + i).padStart(2, '0')}T12:00:00Z`,
  }));
}

const threeEntities = [
  { id: 1, title: 'Alpha Post', author: 'Alice', modified: '2026-03-10T12:00:00Z' },
  { id: 2, title: 'Beta Post', author: 'Bob', modified: '2026-03-15T12:00:00Z' },
  { id: 3, title: 'Gamma Post', author: 'Carol', modified: '2026-03-20T12:00:00Z' },
];

function renderList() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });

  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={['/entities/blog-post']}>
        <Routes>
          <Route path="/entities/:entityName" element={<EntityListPage />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('EntityListPage', () => {
  beforeEach(() => {
    vi.mocked(useSchema).mockReturnValue({
      data: mockSchema,
      isLoading: false,
      error: null,
    } as ReturnType<typeof useSchema>);

    vi.mocked(useEntityList).mockReturnValue({
      data: threeEntities,
      isLoading: false,
      error: null,
    } as unknown as ReturnType<typeof useEntityList>);

    vi.mocked(useDeleteEntity).mockReturnValue({
      mutateAsync: vi.fn().mockResolvedValue({}),
    } as unknown as ReturnType<typeof useDeleteEntity>);
  });

  it('renders table with entity titles as links', () => {
    renderList();
    expect(screen.getByText('Alpha Post')).toBeInTheDocument();
    expect(screen.getByText('Beta Post')).toBeInTheDocument();
    expect(screen.getByText('Gamma Post')).toBeInTheDocument();
  });

  it('renders total count', () => {
    renderList();
    expect(screen.getByText('3 total')).toBeInTheDocument();
  });

  it('renders "Add New" button when editable', () => {
    renderList();
    expect(screen.getByText('Add New')).toBeInTheDocument();
  });

  it('hides "Add New" button when not editable', () => {
    vi.mocked(useSchema).mockReturnValue({
      data: { ...mockSchema, features: { ...mockSchema.features, supports_frontend_edit: false } },
      isLoading: false,
      error: null,
    } as ReturnType<typeof useSchema>);
    renderList();
    expect(screen.queryByText('Add New')).not.toBeInTheDocument();
  });

  it('shows author column when has_author is true', () => {
    renderList();
    expect(screen.getByText('Author')).toBeInTheDocument();
    expect(screen.getByText('Alice')).toBeInTheDocument();
  });

  it('hides author column when has_author is false', () => {
    vi.mocked(useSchema).mockReturnValue({
      data: { ...mockSchema, features: { ...mockSchema.features, has_author: false } },
      isLoading: false,
      error: null,
    } as ReturnType<typeof useSchema>);
    renderList();
    expect(screen.queryByText('Author')).not.toBeInTheDocument();
  });

  it('shows formatted dates in Last Modified column', () => {
    renderList();
    // 2026-03-10 → "Mar 10, 2026"
    expect(screen.getByText('Mar 10, 2026')).toBeInTheDocument();
  });

  it('shows empty state when no entities', () => {
    vi.mocked(useEntityList).mockReturnValue({
      data: [],
      isLoading: false,
      error: null,
    } as unknown as ReturnType<typeof useEntityList>);
    renderList();
    expect(screen.getByText(/No blog posts found/i)).toBeInTheDocument();
  });

  it('search input filters entities by title', async () => {
    const user = userEvent.setup();
    renderList();

    const searchInput = screen.getByTestId('search-input');
    await user.type(searchInput, 'Alpha');

    expect(screen.getByText('Alpha Post')).toBeInTheDocument();
    expect(screen.queryByText('Beta Post')).not.toBeInTheDocument();
    expect(screen.queryByText('Gamma Post')).not.toBeInTheDocument();
  });

  it('search input filters by author too', async () => {
    const user = userEvent.setup();
    renderList();

    const searchInput = screen.getByTestId('search-input');
    await user.type(searchInput, 'Bob');

    expect(screen.getByText('Beta Post')).toBeInTheDocument();
    expect(screen.queryByText('Alpha Post')).not.toBeInTheDocument();
  });

  it('shows filter count when searching', async () => {
    const user = userEvent.setup();
    renderList();

    const searchInput = screen.getByTestId('search-input');
    await user.type(searchInput, 'Alpha');

    expect(screen.getByTestId('filter-count')).toHaveTextContent('Showing 1 of 3');
  });

  it('clear button resets search', async () => {
    const user = userEvent.setup();
    renderList();

    const searchInput = screen.getByTestId('search-input');
    await user.type(searchInput, 'Alpha');
    expect(screen.queryByText('Beta Post')).not.toBeInTheDocument();

    const clearBtn = screen.getByTestId('search-clear');
    await user.click(clearBtn);

    expect(screen.getByText('Alpha Post')).toBeInTheDocument();
    expect(screen.getByText('Beta Post')).toBeInTheDocument();
    expect(screen.queryByTestId('filter-count')).not.toBeInTheDocument();
  });

  it('shows empty search results message', async () => {
    const user = userEvent.setup();
    renderList();

    const searchInput = screen.getByTestId('search-input');
    await user.type(searchInput, 'zzzzz');

    expect(screen.getByText(/No results for "zzzzz"/)).toBeInTheDocument();
  });

  it('clicking Title header sorts by title ascending then descending', async () => {
    const user = userEvent.setup();
    renderList();

    // Default sort is by modified desc
    // Click Title → sort asc
    const titleHeader = screen.getByText('Title').closest('th')!;
    await user.click(titleHeader);

    const rows = screen.getAllByRole('row').filter(row => {
      const cells = within(row).queryAllByRole('cell');
      return cells.length > 0;
    });
    const titles = rows.map(row => within(row).getAllByRole('cell')[0].textContent);
    expect(titles).toEqual(['Alpha Post', 'Beta Post', 'Gamma Post']);

    // Click again → sort desc
    await user.click(titleHeader);
    const rows2 = screen.getAllByRole('row').filter(row => {
      const cells = within(row).queryAllByRole('cell');
      return cells.length > 0;
    });
    const titles2 = rows2.map(row => within(row).getAllByRole('cell')[0].textContent);
    expect(titles2).toEqual(['Gamma Post', 'Beta Post', 'Alpha Post']);
  });

  it('clicking Last Modified header sorts by date', async () => {
    const user = userEvent.setup();
    renderList();

    // Default is modified desc (Gamma=Mar 20, Beta=Mar 15, Alpha=Mar 10)
    const modHeader = screen.getByText('Last Modified').closest('th')!;
    // Click → asc
    await user.click(modHeader);

    const rows = screen.getAllByRole('row').filter(row => {
      const cells = within(row).queryAllByRole('cell');
      return cells.length > 0;
    });
    const firstTitle = within(rows[0]).getAllByRole('cell')[0].textContent;
    expect(firstTitle).toBe('Alpha Post');
  });

  describe('pagination', () => {
    it('shows pagination when more than PAGE_SIZE entities', () => {
      vi.mocked(useEntityList).mockReturnValue({
        data: makeEntities(25),
        isLoading: false,
        error: null,
      } as unknown as ReturnType<typeof useEntityList>);
      renderList();

      expect(screen.getByText('Page 1 of 2')).toBeInTheDocument();
      expect(screen.getByText('Next')).toBeInTheDocument();
    });

    it('does not show pagination for 3 entities', () => {
      renderList();
      expect(screen.queryByText(/Page \d+ of/)).not.toBeInTheDocument();
    });

    it('Next button advances page', async () => {
      const user = userEvent.setup();
      vi.mocked(useEntityList).mockReturnValue({
        data: makeEntities(25),
        isLoading: false,
        error: null,
      } as unknown as ReturnType<typeof useEntityList>);
      renderList();

      await user.click(screen.getByText('Next'));
      expect(screen.getByText('Page 2 of 2')).toBeInTheDocument();
    });

    it('Previous button goes back', async () => {
      const user = userEvent.setup();
      vi.mocked(useEntityList).mockReturnValue({
        data: makeEntities(25),
        isLoading: false,
        error: null,
      } as unknown as ReturnType<typeof useEntityList>);
      renderList();

      await user.click(screen.getByText('Next'));
      expect(screen.getByText('Page 2 of 2')).toBeInTheDocument();

      await user.click(screen.getByText('Previous'));
      expect(screen.getByText('Page 1 of 2')).toBeInTheDocument();
    });

    it('search resets page to 1', async () => {
      const user = userEvent.setup();
      vi.mocked(useEntityList).mockReturnValue({
        data: makeEntities(25),
        isLoading: false,
        error: null,
      } as unknown as ReturnType<typeof useEntityList>);
      renderList();

      await user.click(screen.getByText('Next'));
      expect(screen.getByText('Page 2 of 2')).toBeInTheDocument();

      const searchInput = screen.getByTestId('search-input');
      await user.type(searchInput, 'Post A');
      // Should have reset to page 1 (or no pagination at all if filtered to <20)
      expect(screen.queryByText('Page 2')).not.toBeInTheDocument();
    });
  });
});
