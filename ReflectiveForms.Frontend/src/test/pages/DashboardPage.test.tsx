import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { DashboardPage } from '../../pages/DashboardPage';

vi.mock('../../hooks/useEntity', () => ({
  useAllSchemas: vi.fn(),
  useCapabilities: vi.fn(() => ({ data: undefined })),
}));

vi.mock('../../lib/AiAssistantContext', () => ({
  AiAssistantProvider: ({ children }: { children: React.ReactNode }) => children,
  useAiAssistantOptional: vi.fn(() => null),
}));

import { useAllSchemas } from '../../hooks/useEntity';

const queryClient = new QueryClient({
  defaultOptions: { queries: { retry: false } },
});

function renderWithProviders(ui: React.ReactElement) {
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>{ui}</MemoryRouter>
    </QueryClientProvider>,
  );
}

function makeSchema(entityName: string, plural: string, overrides: Record<string, unknown> = {}) {
  return {
    entity_name: entityName,
    readable_name: { singular: plural.slice(0, -1), plural },
    features: {
      has_author: false,
      has_tags: false,
      has_categories: false,
      has_parent_child: false,
      require_title_uniqueness: false,
      supports_frontend_edit: true,
      show_in_navigation: true,
      has_individual_sharing: false,
      supports_semantic_search: false,
      supports_ai_generation: false,
      supports_ai_diff_summary: false,
      supports_natural_language_filter: false,
      ...overrides,
    },
    fields: [],
    api_endpoints: { crud: '', sanity_check: '', entity_lock: '', media: '' },
    schema_version: '1.0',
  };
}

describe('DashboardPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('should show visible entities on dashboard', async () => {
    const mockSchemas = {
      Articles: makeSchema('articles', 'Articles'),
      Pages: makeSchema('pages', 'Pages'),
    };

    vi.mocked(useAllSchemas).mockReturnValue({
      data: mockSchemas,
      isLoading: false,
      error: null,
    } as any);

    renderWithProviders(<DashboardPage />);

    expect(screen.getByText('Dashboard')).toBeInTheDocument();
    expect(screen.getByText('Articles')).toBeInTheDocument();
    expect(screen.getByText('Pages')).toBeInTheDocument();
  });

  it('should hide entities with show_in_navigation=false from dashboard', async () => {
    const mockSchemas = {
      Articles: makeSchema('articles', 'Articles'),
      HiddenItems: makeSchema('hidden-items', 'Hidden Items', { show_in_navigation: false }),
    };

    vi.mocked(useAllSchemas).mockReturnValue({
      data: mockSchemas,
      isLoading: false,
      error: null,
    } as any);

    renderWithProviders(<DashboardPage />);

    expect(screen.getByText('Articles')).toBeInTheDocument();
    expect(screen.queryByText('Hidden Items')).not.toBeInTheDocument();
  });

  it('should show loading state', () => {
    vi.mocked(useAllSchemas).mockReturnValue({
      data: undefined,
      isLoading: true,
      error: null,
    } as any);

    renderWithProviders(<DashboardPage />);

    // Should show a loading spinner
    const spinner = document.querySelector('.animate-spin');
    expect(spinner).toBeInTheDocument();
  });

  it('should show error state', () => {
    vi.mocked(useAllSchemas).mockReturnValue({
      data: undefined,
      isLoading: false,
      error: new Error('Failed to load'),
    } as any);

    renderWithProviders(<DashboardPage />);

    expect(screen.getByText('Error')).toBeInTheDocument();
    expect(screen.getByText('Failed to load')).toBeInTheDocument();
  });
});
