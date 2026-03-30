import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { EntityListPage, buildEntityTree, TreeNode } from '../../pages/EntityListPage';
import { PeekEntity } from '../../types/schema';

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
    } as unknown as ReturnType<typeof useSchema>);

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
    } as unknown as ReturnType<typeof useSchema>);
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
    } as unknown as ReturnType<typeof useSchema>);
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

// --------------- Hierarchy / tree tests ---------------

const parentChildSchema = {
  ...mockSchema,
  entity_name: 'page',
  readable_name: { singular: 'Page', plural: 'Pages' },
  features: { ...mockSchema.features, has_parent_child: true },
};

/**
 * Generate a linear chain of entities: 1 → 2 → 3 → ... → depth.
 * Entity 1 is the root, entity 2 is child of 1, etc.
 */
function makeChain(depth: number) {
  return Array.from({ length: depth }, (_, i) => ({
    id: i + 1,
    title: `Gen${i + 1}`,
    author: 'Admin',
    modified: '2026-01-01T00:00:00Z',
    parent_id: i === 0 ? undefined : i, // parent of entity i+1 is entity i
  }));
}

function renderHierarchyList(entities: PeekEntity[]) {
  vi.mocked(useSchema).mockReturnValue({
    data: parentChildSchema,
    isLoading: false,
    error: null,
  } as unknown as ReturnType<typeof useSchema>);

  vi.mocked(useEntityList).mockReturnValue({
    data: entities,
    isLoading: false,
    error: null,
  } as unknown as ReturnType<typeof useEntityList>);

  vi.mocked(useDeleteEntity).mockReturnValue({
    mutateAsync: vi.fn().mockResolvedValue({}),
  } as unknown as ReturnType<typeof useDeleteEntity>);

  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });

  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={['/entities/page']}>
        <Routes>
          <Route path="/entities/:entityName" element={<EntityListPage />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('buildEntityTree', () => {
  it('builds a flat list with no parents', () => {
    const entities = [
      { id: 1, title: 'A' },
      { id: 2, title: 'B' },
    ];
    const tree = buildEntityTree(entities);
    expect(tree).toHaveLength(2);
    expect(tree[0].depth).toBe(0);
    expect(tree[1].depth).toBe(0);
  });

  it('builds a 6-generation linear chain', () => {
    const chain = makeChain(6);
    const tree = buildEntityTree(chain);

    expect(tree).toHaveLength(1);
    expect(tree[0].entity.title).toBe('Gen1');
    expect(tree[0].depth).toBe(0);

    let node: TreeNode = tree[0];
    for (let gen = 2; gen <= 6; gen++) {
      expect(node.children).toHaveLength(1);
      node = node.children[0];
      expect(node.entity.title).toBe(`Gen${gen}`);
      expect(node.depth).toBe(gen - 1);
    }
    // Leaf has no children
    expect(node.children).toHaveLength(0);
  });

  it('handles orphans as roots', () => {
    const entities = [
      { id: 1, title: 'Root' },
      { id: 2, title: 'Child', parent_id: 1 },
      { id: 3, title: 'Orphan', parent_id: 999 }, // parent not in list
    ];
    const tree = buildEntityTree(entities);
    const rootTitles = tree.map(n => n.entity.title);
    expect(rootTitles).toContain('Root');
    expect(rootTitles).toContain('Orphan'); // orphan becomes root
    expect(tree).toHaveLength(2);
  });

  it('supports multiple children per parent', () => {
    const entities = [
      { id: 1, title: 'Parent' },
      { id: 2, title: 'Child A', parent_id: 1 },
      { id: 3, title: 'Child B', parent_id: 1 },
      { id: 4, title: 'Child C', parent_id: 1 },
    ];
    const tree = buildEntityTree(entities);
    expect(tree).toHaveLength(1);
    expect(tree[0].children).toHaveLength(3);
  });

  it('handles parent_id of 0 or negative as root', () => {
    const entities = [
      { id: 1, title: 'A', parent_id: 0 },
      { id: 2, title: 'B', parent_id: -1 },
    ];
    const tree = buildEntityTree(entities);
    expect(tree).toHaveLength(2);
    expect(tree[0].depth).toBe(0);
    expect(tree[1].depth).toBe(0);
  });
});

describe('EntityListPage – hierarchical rendering', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renders only the root when children are collapsed (6 generations)', () => {
    const chain = makeChain(6);
    renderHierarchyList(chain);

    // Only root visible by default (children collapsed)
    expect(screen.getByText('Gen1')).toBeInTheDocument();
    expect(screen.queryByText('Gen2')).not.toBeInTheDocument();
    expect(screen.queryByText('Gen6')).not.toBeInTheDocument();
  });

  it('expanding progressively reveals each generation (6 levels)', async () => {
    const user = userEvent.setup();
    const chain = makeChain(6);
    renderHierarchyList(chain);

    for (let gen = 1; gen <= 5; gen++) {
      // Expand gen
      const toggle = screen.getByTestId(`toggle-${gen}`);
      await user.click(toggle);
      // gen+1 should now be visible
      expect(screen.getByText(`Gen${gen + 1}`)).toBeInTheDocument();
    }

    // All 6 generations visible
    for (let gen = 1; gen <= 6; gen++) {
      expect(screen.getByText(`Gen${gen}`)).toBeInTheDocument();
    }
  });

  it('collapsing a parent hides all descendants (6 levels)', async () => {
    const user = userEvent.setup();
    const chain = makeChain(6);
    renderHierarchyList(chain);

    // Expand all
    for (let gen = 1; gen <= 5; gen++) {
      await user.click(screen.getByTestId(`toggle-${gen}`));
    }
    expect(screen.getByText('Gen6')).toBeInTheDocument();

    // Collapse root (id=1)
    await user.click(screen.getByTestId('toggle-1'));

    // Only root visible
    expect(screen.getByText('Gen1')).toBeInTheDocument();
    expect(screen.queryByText('Gen2')).not.toBeInTheDocument();
    expect(screen.queryByText('Gen6')).not.toBeInTheDocument();
  });

  it('child rows have increasing data-depth attributes (6 levels)', async () => {
    const user = userEvent.setup();
    const chain = makeChain(6);
    renderHierarchyList(chain);

    // Expand all
    for (let gen = 1; gen <= 5; gen++) {
      await user.click(screen.getByTestId(`toggle-${gen}`));
    }

    for (let gen = 1; gen <= 6; gen++) {
      const row = screen.getByTestId(`entity-row-${gen}`);
      expect(row.getAttribute('data-depth')).toBe(String(gen - 1));
    }
  });

  it('child rows have increasing indentation (6 levels)', async () => {
    const user = userEvent.setup();
    const chain = makeChain(6);
    renderHierarchyList(chain);

    // Expand all
    for (let gen = 1; gen <= 5; gen++) {
      await user.click(screen.getByTestId(`toggle-${gen}`));
    }

    // Root (depth=0) – no paddingLeft
    const rootCell = screen.getByTestId('entity-row-1').querySelector('td .flex') as HTMLElement;
    expect(rootCell.style.paddingLeft).toBe('');

    // Children have depth*24 paddingLeft
    for (let gen = 2; gen <= 6; gen++) {
      const cell = screen.getByTestId(`entity-row-${gen}`).querySelector('td .flex') as HTMLElement;
      expect(cell.style.paddingLeft).toBe(`${(gen - 1) * 24}px`);
    }
  });

  it('leaf nodes have invisible toggle buttons', async () => {
    const user = userEvent.setup();
    const chain = makeChain(6);
    renderHierarchyList(chain);

    // Expand to leaf
    for (let gen = 1; gen <= 5; gen++) {
      await user.click(screen.getByTestId(`toggle-${gen}`));
    }

    // Gen6 is the leaf – toggle should be invisible
    const leafToggle = screen.getByTestId('toggle-6');
    expect(leafToggle.className).toContain('invisible');
  });

  it('shows flat list when has_parent_child is false (no toggle buttons)', () => {
    vi.mocked(useSchema).mockReturnValue({
      data: mockSchema, // has_parent_child: false
      isLoading: false,
      error: null,
    } as unknown as ReturnType<typeof useSchema>);

    vi.mocked(useEntityList).mockReturnValue({
      data: threeEntities,
      isLoading: false,
      error: null,
    } as unknown as ReturnType<typeof useEntityList>);

    vi.mocked(useDeleteEntity).mockReturnValue({
      mutateAsync: vi.fn().mockResolvedValue({}),
    } as unknown as ReturnType<typeof useDeleteEntity>);

    renderList();

    // All visible
    expect(screen.getByText('Alpha Post')).toBeInTheDocument();
    expect(screen.getByText('Beta Post')).toBeInTheDocument();
    // No toggle buttons
    expect(screen.queryByTestId('toggle-1')).not.toBeInTheDocument();
  });

  it('renders a broad tree with 6+ generations correctly', async () => {
    const user = userEvent.setup();
    // Root → two children → each has a child → each has a child → each has a child → each has a child (6 gens)
    const entities = [
      { id: 1, title: 'Root', author: 'Admin', modified: '2026-01-01T00:00:00Z' },
      { id: 2, title: 'L2-A', parent_id: 1, author: 'Admin', modified: '2026-01-01T00:00:00Z' },
      { id: 3, title: 'L2-B', parent_id: 1, author: 'Admin', modified: '2026-01-01T00:00:00Z' },
      { id: 4, title: 'L3-A', parent_id: 2, author: 'Admin', modified: '2026-01-01T00:00:00Z' },
      { id: 5, title: 'L3-B', parent_id: 3, author: 'Admin', modified: '2026-01-01T00:00:00Z' },
      { id: 6, title: 'L4-A', parent_id: 4, author: 'Admin', modified: '2026-01-01T00:00:00Z' },
      { id: 7, title: 'L4-B', parent_id: 5, author: 'Admin', modified: '2026-01-01T00:00:00Z' },
      { id: 8, title: 'L5-A', parent_id: 6, author: 'Admin', modified: '2026-01-01T00:00:00Z' },
      { id: 9, title: 'L5-B', parent_id: 7, author: 'Admin', modified: '2026-01-01T00:00:00Z' },
      { id: 10, title: 'L6-A', parent_id: 8, author: 'Admin', modified: '2026-01-01T00:00:00Z' },
      { id: 11, title: 'L6-B', parent_id: 9, author: 'Admin', modified: '2026-01-01T00:00:00Z' },
    ];
    renderHierarchyList(entities);

    // Only root visible initially
    expect(screen.getByText('Root')).toBeInTheDocument();
    expect(screen.queryByText('L2-A')).not.toBeInTheDocument();

    // Expand root
    await user.click(screen.getByTestId('toggle-1'));
    expect(screen.getByText('L2-A')).toBeInTheDocument();
    expect(screen.getByText('L2-B')).toBeInTheDocument();

    // Expand down A-branch
    await user.click(screen.getByTestId('toggle-2'));
    await user.click(screen.getByTestId('toggle-4'));
    await user.click(screen.getByTestId('toggle-6'));
    await user.click(screen.getByTestId('toggle-8'));

    expect(screen.getByText('L6-A')).toBeInTheDocument();
    expect(screen.getByTestId('entity-row-10').getAttribute('data-depth')).toBe('5'); // 6th generation = depth 5
  });
});
