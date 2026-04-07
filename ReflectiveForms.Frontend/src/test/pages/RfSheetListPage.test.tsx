import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { RfSheetListPage } from '../../pages/RfSheetListPage';

vi.mock('../../hooks/useEntity', () => ({
  useEntityList: vi.fn(),
  useCapabilities: vi.fn(() => ({ data: undefined, isLoading: false, isSuccess: false })),
}));

import { useEntityList, useCapabilities } from '../../hooks/useEntity';

function renderSheetList() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });

  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={['/sheets']}>
        <Routes>
          <Route path="/sheets" element={<RfSheetListPage />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('RfSheetListPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renders loading state', () => {
    vi.mocked(useEntityList).mockReturnValue({
      data: undefined,
      isLoading: true,
      error: null,
    } as unknown as ReturnType<typeof useEntityList>);

    renderSheetList();
    expect(screen.getByText('Sheets')).toBeInTheDocument();
  });

  it('renders error state', () => {
    vi.mocked(useEntityList).mockReturnValue({
      data: undefined,
      isLoading: false,
      error: new Error('Network error'),
    } as unknown as ReturnType<typeof useEntityList>);

    renderSheetList();
    expect(screen.getByText(/Failed to load sheets/)).toBeInTheDocument();
  });

  it('renders empty state when no sheets exist', () => {
    vi.mocked(useEntityList).mockReturnValue({
      data: [],
      isLoading: false,
      error: null,
    } as unknown as ReturnType<typeof useEntityList>);

    renderSheetList();
    expect(screen.getByText('No sheets yet')).toBeInTheDocument();
  });

  it('renders sheet list with titles', () => {
    vi.mocked(useEntityList).mockReturnValue({
      data: [
        { id: 1, title: 'Sales Report', author: 'Alice', modified: '2025-01-15T10:00:00Z' },
        { id: 2, title: 'Department Overview', author: 'Bob', modified: '2025-01-20T14:00:00Z' },
      ],
      isLoading: false,
      error: null,
    } as unknown as ReturnType<typeof useEntityList>);

    renderSheetList();
    expect(screen.getByText('Sales Report')).toBeInTheDocument();
    expect(screen.getByText('Department Overview')).toBeInTheDocument();
  });

  it('renders author names for each sheet', () => {
    vi.mocked(useEntityList).mockReturnValue({
      data: [
        { id: 1, title: 'Report', author: 'Alice', modified: '2025-01-15T10:00:00Z' },
      ],
      isLoading: false,
      error: null,
    } as unknown as ReturnType<typeof useEntityList>);

    renderSheetList();
    expect(screen.getByText('Alice')).toBeInTheDocument();
  });

  it('shows "New Sheet" button when user has create permission', () => {
    vi.mocked(useEntityList).mockReturnValue({
      data: [],
      isLoading: false,
      error: null,
    } as unknown as ReturnType<typeof useEntityList>);

    vi.mocked(useCapabilities).mockReturnValue({
      data: { 'rf-sheets': { can_peek_all: true, can_read: true, can_create: true, can_update: false, can_delete: false } },
      isLoading: false,
      isSuccess: true,
    } as unknown as ReturnType<typeof useCapabilities>);

    renderSheetList();
    expect(screen.getByText('New Sheet')).toBeInTheDocument();
  });

  it('hides "New Sheet" button when user lacks create permission', () => {
    vi.mocked(useEntityList).mockReturnValue({
      data: [],
      isLoading: false,
      error: null,
    } as unknown as ReturnType<typeof useEntityList>);

    vi.mocked(useCapabilities).mockReturnValue({
      data: { 'rf-sheets': { can_peek_all: true, can_read: true, can_create: false, can_update: false, can_delete: false } },
      isLoading: false,
      isSuccess: true,
    } as unknown as ReturnType<typeof useCapabilities>);

    renderSheetList();
    expect(screen.queryByText('New Sheet')).not.toBeInTheDocument();
  });

  it('shows fallback title for sheets without a title', () => {
    vi.mocked(useEntityList).mockReturnValue({
      data: [
        { id: 42, author: 'Admin', modified: '2025-01-15T10:00:00Z' },
      ],
      isLoading: false,
      error: null,
    } as unknown as ReturnType<typeof useEntityList>);

    renderSheetList();
    expect(screen.getByText('Sheet #42')).toBeInTheDocument();
  });

  it('displays formatted modified dates', () => {
    vi.mocked(useEntityList).mockReturnValue({
      data: [
        { id: 1, title: 'Dated Sheet', author: 'Admin', modified: '2025-06-15T14:30:00Z' },
      ],
      isLoading: false,
      error: null,
    } as unknown as ReturnType<typeof useEntityList>);

    renderSheetList();
    // The exact format depends on locale, but the date should be rendered
    const dateCell = screen.queryByText(/2025/);
    expect(dateCell).toBeInTheDocument();
  });

  it('shows dash for missing author', () => {
    vi.mocked(useEntityList).mockReturnValue({
      data: [
        { id: 1, title: 'No Author Sheet', modified: '2025-01-15T10:00:00Z' },
      ],
      isLoading: false,
      error: null,
    } as unknown as ReturnType<typeof useEntityList>);

    renderSheetList();
    expect(screen.getByText('—')).toBeInTheDocument();
  });

  // ── Entity Added/Removed via re-render ───────────────────────────────

  it('reflects newly added sheets on re-render', () => {
    const mockReturn = {
      data: [{ id: 1, title: 'First Sheet', author: 'Admin', modified: '2025-01-15T10:00:00Z' }],
      isLoading: false,
      error: null,
    };
    vi.mocked(useEntityList).mockReturnValue(mockReturn as unknown as ReturnType<typeof useEntityList>);

    const { rerender } = renderSheetList();
    expect(screen.getByText('First Sheet')).toBeInTheDocument();
    expect(screen.queryByText('Second Sheet')).not.toBeInTheDocument();

    // Simulate entity added
    vi.mocked(useEntityList).mockReturnValue({
      data: [
        { id: 1, title: 'First Sheet', author: 'Admin', modified: '2025-01-15T10:00:00Z' },
        { id: 2, title: 'Second Sheet', author: 'Admin', modified: '2025-01-16T10:00:00Z' },
      ],
      isLoading: false,
      error: null,
    } as unknown as ReturnType<typeof useEntityList>);

    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    rerender(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter initialEntries={['/sheets']}>
          <Routes>
            <Route path="/sheets" element={<RfSheetListPage />} />
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>,
    );

    expect(screen.getByText('First Sheet')).toBeInTheDocument();
    expect(screen.getByText('Second Sheet')).toBeInTheDocument();
  });

  it('reflects removed sheets on re-render', () => {
    vi.mocked(useEntityList).mockReturnValue({
      data: [
        { id: 1, title: 'Sheet A', author: 'Admin', modified: '2025-01-15T10:00:00Z' },
        { id: 2, title: 'Sheet B', author: 'Admin', modified: '2025-01-16T10:00:00Z' },
      ],
      isLoading: false,
      error: null,
    } as unknown as ReturnType<typeof useEntityList>);

    const { rerender } = renderSheetList();
    expect(screen.getByText('Sheet A')).toBeInTheDocument();
    expect(screen.getByText('Sheet B')).toBeInTheDocument();

    // Simulate entity removed
    vi.mocked(useEntityList).mockReturnValue({
      data: [
        { id: 1, title: 'Sheet A', author: 'Admin', modified: '2025-01-15T10:00:00Z' },
      ],
      isLoading: false,
      error: null,
    } as unknown as ReturnType<typeof useEntityList>);

    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    rerender(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter initialEntries={['/sheets']}>
          <Routes>
            <Route path="/sheets" element={<RfSheetListPage />} />
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>,
    );

    expect(screen.getByText('Sheet A')).toBeInTheDocument();
    expect(screen.queryByText('Sheet B')).not.toBeInTheDocument();
  });

  it('transitions from empty to populated when first sheet is created', () => {
    vi.mocked(useEntityList).mockReturnValue({
      data: [],
      isLoading: false,
      error: null,
    } as unknown as ReturnType<typeof useEntityList>);

    const { rerender } = renderSheetList();
    expect(screen.getByText('No sheets yet')).toBeInTheDocument();

    vi.mocked(useEntityList).mockReturnValue({
      data: [{ id: 1, title: 'Brand New Sheet', author: 'Admin' }],
      isLoading: false,
      error: null,
    } as unknown as ReturnType<typeof useEntityList>);

    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    rerender(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter initialEntries={['/sheets']}>
          <Routes>
            <Route path="/sheets" element={<RfSheetListPage />} />
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>,
    );

    expect(screen.queryByText('No sheets yet')).not.toBeInTheDocument();
    expect(screen.getByText('Brand New Sheet')).toBeInTheDocument();
  });

  it('transitions from populated to empty when all sheets are deleted', () => {
    vi.mocked(useEntityList).mockReturnValue({
      data: [{ id: 1, title: 'Last Sheet', author: 'Admin' }],
      isLoading: false,
      error: null,
    } as unknown as ReturnType<typeof useEntityList>);

    const { rerender } = renderSheetList();
    expect(screen.getByText('Last Sheet')).toBeInTheDocument();

    vi.mocked(useEntityList).mockReturnValue({
      data: [],
      isLoading: false,
      error: null,
    } as unknown as ReturnType<typeof useEntityList>);

    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    rerender(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter initialEntries={['/sheets']}>
          <Routes>
            <Route path="/sheets" element={<RfSheetListPage />} />
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>,
    );

    expect(screen.queryByText('Last Sheet')).not.toBeInTheDocument();
    expect(screen.getByText('No sheets yet')).toBeInTheDocument();
  });
});
