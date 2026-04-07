import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { createElement, ReactNode } from 'react';
import { SearchableSelect } from '../../components/form/SearchableSelect';

// Mock the hook that SearchableSelect uses internally
vi.mock('../../hooks/useEntity', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../hooks/useEntity')>();
  return {
    ...actual,
    usePaginatedEntityList: vi.fn(),
  };
});

import { usePaginatedEntityList } from '../../hooks/useEntity';

const mockData = {
  pages: [
    {
      items: [
        { id: 1, title: 'Alpha Entity' },
        { id: 2, title: 'Beta Entity' },
        { id: 3, title: 'Gamma Entity' },
      ],
      next_page_token: null,
      total_count: 3,
    },
  ],
  pageParams: [undefined],
};

function Wrapper({ children }: { children: ReactNode }) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return createElement(QueryClientProvider, { client: queryClient }, children);
}

describe('SearchableSelect', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(usePaginatedEntityList).mockReturnValue({
      data: mockData,
      fetchNextPage: vi.fn(),
      hasNextPage: false,
      isFetchingNextPage: false,
      isLoading: false,
    } as any);
  });

  afterEach(() => {
    vi.resetAllMocks();
  });

  it('renders with placeholder when no value selected', async () => {
    const onChange = vi.fn();
    render(
      <Wrapper>
        <SearchableSelect entityName="test" value={-1} onChange={onChange} placeholder="-- Pick One --" />
      </Wrapper>
    );

    await waitFor(() => {
      expect(screen.getByRole('button', { expanded: false })).toBeInTheDocument();
    });
    expect(screen.getByText('-- Pick One --')).toBeInTheDocument();
  });

  it('opens dropdown on click', async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    render(
      <Wrapper>
        <SearchableSelect entityName="test" value={-1} onChange={onChange} />
      </Wrapper>
    );

    await waitFor(() => {
      expect(screen.getByRole('button')).toBeInTheDocument();
    });

    await user.click(screen.getByRole('button'));

    expect(screen.getByPlaceholderText('Search...')).toBeInTheDocument();
    expect(screen.getByRole('listbox')).toBeInTheDocument();
  });

  it('shows options after loading', async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    render(
      <Wrapper>
        <SearchableSelect entityName="test" value={-1} onChange={onChange} />
      </Wrapper>
    );

    await waitFor(() => {
      expect(screen.getByRole('button')).toBeInTheDocument();
    });

    await user.click(screen.getByRole('button'));

    await waitFor(() => {
      expect(screen.getByText('Alpha Entity')).toBeInTheDocument();
      expect(screen.getByText('Beta Entity')).toBeInTheDocument();
      expect(screen.getByText('Gamma Entity')).toBeInTheDocument();
    });
  });

  it('filters options by search text', async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    render(
      <Wrapper>
        <SearchableSelect entityName="test" value={-1} onChange={onChange} />
      </Wrapper>
    );

    await waitFor(() => {
      expect(screen.getByRole('button')).toBeInTheDocument();
    });

    await user.click(screen.getByRole('button'));

    await waitFor(() => {
      expect(screen.getByText('Alpha Entity')).toBeInTheDocument();
    });

    // Type in search
    const searchInput = screen.getByPlaceholderText('Search...');
    await user.type(searchInput, 'Beta');

    // Only Beta should be shown (plus placeholder option)
    expect(screen.getByText('Beta Entity')).toBeInTheDocument();
    expect(screen.queryByText('Alpha Entity')).not.toBeInTheDocument();
    expect(screen.queryByText('Gamma Entity')).not.toBeInTheDocument();
  });

  it('calls onChange when an option is clicked', async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    render(
      <Wrapper>
        <SearchableSelect entityName="test" value={-1} onChange={onChange} />
      </Wrapper>
    );

    await waitFor(() => {
      expect(screen.getByRole('button')).toBeInTheDocument();
    });

    await user.click(screen.getByRole('button'));

    await waitFor(() => {
      expect(screen.getByText('Beta Entity')).toBeInTheDocument();
    });

    await user.click(screen.getByText('Beta Entity'));

    expect(onChange).toHaveBeenCalledWith(2);
  });

  it('shows selected entity label when value is set', async () => {
    const onChange = vi.fn();
    render(
      <Wrapper>
        <SearchableSelect entityName="test" value={1} onChange={onChange} />
      </Wrapper>
    );

    await waitFor(() => {
      expect(screen.getByText('Alpha Entity')).toBeInTheDocument();
    });
  });

  it('closes dropdown on Escape key', async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    render(
      <Wrapper>
        <SearchableSelect entityName="test" value={-1} onChange={onChange} />
      </Wrapper>
    );

    await waitFor(() => {
      expect(screen.getByRole('button')).toBeInTheDocument();
    });

    await user.click(screen.getByRole('button'));
    expect(screen.getByRole('listbox')).toBeInTheDocument();

    await user.keyboard('{Escape}');
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument();
  });

  it('is disabled when disabled prop is true', async () => {
    const onChange = vi.fn();
    render(
      <Wrapper>
        <SearchableSelect entityName="test" value={-1} onChange={onChange} disabled />
      </Wrapper>
    );

    await waitFor(() => {
      expect(screen.getByRole('button')).toBeInTheDocument();
    });

    expect(screen.getByRole('button')).toBeDisabled();
  });

  it('clears selection with clear button', async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    render(
      <Wrapper>
        <SearchableSelect entityName="test" value={1} onChange={onChange} />
      </Wrapper>
    );

    await waitFor(() => {
      expect(screen.getByText('Alpha Entity')).toBeInTheDocument();
    });

    // The trigger button has aria-haspopup; find the (X) inside it
    const trigger = screen.getByRole('button', { name: /alpha entity/i });
    const clearButton = trigger.querySelector('[role="button"]');
    expect(clearButton).toBeInTheDocument();
    await user.click(clearButton!);

    expect(onChange).toHaveBeenCalledWith(-1);
  });

  it('shows pagination info when total_count is available', async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    render(
      <Wrapper>
        <SearchableSelect entityName="test" value={-1} onChange={onChange} />
      </Wrapper>
    );

    await waitFor(() => {
      expect(screen.getByRole('button')).toBeInTheDocument();
    });

    await user.click(screen.getByRole('button'));

    await waitFor(() => {
      expect(screen.getByText('3 of 3 loaded')).toBeInTheDocument();
    });
  });
});
