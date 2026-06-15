import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { DashboardPage } from '../../pages/DashboardPage';

vi.mock('../../hooks/useEntity', () => ({
  useAllSchemas: vi.fn(),
  useCapabilities: vi.fn(() => ({ data: undefined })),
  useGlobalSettings: vi.fn(() => ({})),
}));

vi.mock('../../lib/AiAssistantContext', () => ({
  AiAssistantProvider: ({ children }: { children: React.ReactNode }) => children,
  useAiAssistantOptional: vi.fn(() => null),
}));

import { useAllSchemas, useGlobalSettings } from '../../hooks/useEntity';

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

  it('should hide reserved entities listed in reserved_entity_types_to_hide_in_navigation', () => {
    const mockSchemas = {
      articles: makeSchema('articles', 'Articles'),
      tags: makeSchema('tags', 'Tags'),
      categories: makeSchema('categories', 'Categories'),
    };

    vi.mocked(useAllSchemas).mockReturnValue({
      data: mockSchemas,
      isLoading: false,
      error: null,
    } as any);
    vi.mocked(useGlobalSettings).mockReturnValue({
      reserved_entity_types_to_hide_in_navigation: ['tags', 'categories'],
    });

    renderWithProviders(<DashboardPage />);

    expect(screen.getByText('Articles')).toBeInTheDocument();
    expect(screen.queryByText('Tags')).not.toBeInTheDocument();
    expect(screen.queryByText('Categories')).not.toBeInTheDocument();
  });

  it('should show reserved entities when hide list is empty', () => {
    const mockSchemas = {
      articles: makeSchema('articles', 'Articles'),
      tags: makeSchema('tags', 'Tags'),
    };

    vi.mocked(useAllSchemas).mockReturnValue({
      data: mockSchemas,
      isLoading: false,
      error: null,
    } as any);
    vi.mocked(useGlobalSettings).mockReturnValue({
      reserved_entity_types_to_hide_in_navigation: [],
    });

    renderWithProviders(<DashboardPage />);

    expect(screen.getByText('Articles')).toBeInTheDocument();
    expect(screen.getByText('Tags')).toBeInTheDocument();
  });

  it('should show reserved entities when hide list is undefined', () => {
    const mockSchemas = {
      articles: makeSchema('articles', 'Articles'),
      media: makeSchema('media', 'Media'),
    };

    vi.mocked(useAllSchemas).mockReturnValue({
      data: mockSchemas,
      isLoading: false,
      error: null,
    } as any);
    vi.mocked(useGlobalSettings).mockReturnValue({});

    renderWithProviders(<DashboardPage />);

    expect(screen.getByText('Articles')).toBeInTheDocument();
    expect(screen.getByText('Media')).toBeInTheDocument();
  });
});
