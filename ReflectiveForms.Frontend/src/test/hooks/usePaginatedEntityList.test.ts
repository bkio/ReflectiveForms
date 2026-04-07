import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { createElement } from 'react';
import { usePaginatedEntityList } from '../../hooks/useEntity';
import * as client from '../../api/client';

vi.mock('../../api/client');

const mockPage1 = {
  items: [
    { id: 1, title: 'Entity 1' },
    { id: 2, title: 'Entity 2' },
  ],
  next_page_token: 'token_page2',
  total_count: 5,
};

const mockPage2 = {
  items: [
    { id: 3, title: 'Entity 3' },
    { id: 4, title: 'Entity 4' },
  ],
  next_page_token: 'token_page3',
  total_count: 5,
};

const mockPage3 = {
  items: [
    { id: 5, title: 'Entity 5' },
  ],
  next_page_token: null,
  total_count: 5,
};

function createWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return ({ children }: { children: React.ReactNode }) =>
    createElement(QueryClientProvider, { client: queryClient }, children);
}

describe('usePaginatedEntityList', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  afterEach(() => {
    vi.resetAllMocks();
  });

  it('should fetch the first page', async () => {
    vi.mocked(client.peekAllEntitiesPaginated).mockResolvedValue({ data: mockPage1 });

    const { result } = renderHook(() => usePaginatedEntityList('TestEntity', 2), {
      wrapper: createWrapper(),
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(result.current.data?.pages).toHaveLength(1);
    expect(result.current.data?.pages[0].items).toEqual(mockPage1.items);
    expect(result.current.data?.pages[0].total_count).toBe(5);
    expect(result.current.hasNextPage).toBe(true);
    expect(client.peekAllEntitiesPaginated).toHaveBeenCalledWith('TestEntity', 2, undefined);
  });

  it('should fetch the next page when requested', async () => {
    vi.mocked(client.peekAllEntitiesPaginated)
      .mockResolvedValueOnce({ data: mockPage1 })
      .mockResolvedValueOnce({ data: mockPage2 });

    const { result } = renderHook(() => usePaginatedEntityList('TestEntity', 2), {
      wrapper: createWrapper(),
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.hasNextPage).toBe(true);

    // Fetch next page
    result.current.fetchNextPage();

    await waitFor(() => expect(result.current.data?.pages).toHaveLength(2));

    expect(result.current.data?.pages[1].items).toEqual(mockPage2.items);
    expect(client.peekAllEntitiesPaginated).toHaveBeenCalledWith('TestEntity', 2, 'token_page2');
  });

  it('should know when there are no more pages', async () => {
    vi.mocked(client.peekAllEntitiesPaginated).mockResolvedValue({ data: mockPage3 });

    const { result } = renderHook(() => usePaginatedEntityList('TestEntity', 2), {
      wrapper: createWrapper(),
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.hasNextPage).toBe(false);
  });

  it('should handle API errors', async () => {
    vi.mocked(client.peekAllEntitiesPaginated).mockResolvedValue({ error: 'Server error' });

    const { result } = renderHook(() => usePaginatedEntityList('TestEntity', 2), {
      wrapper: createWrapper(),
    });

    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(result.current.error?.message).toBe('Server error');
  });

  it('should use custom page size', async () => {
    vi.mocked(client.peekAllEntitiesPaginated).mockResolvedValue({ data: mockPage1 });

    renderHook(() => usePaginatedEntityList('TestEntity', 50), {
      wrapper: createWrapper(),
    });

    await waitFor(() =>
      expect(client.peekAllEntitiesPaginated).toHaveBeenCalledWith('TestEntity', 50, undefined)
    );
  });
});
