import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { BrowserRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { AdminLayout } from '../../../components/layout/AdminLayout';
import * as useEntityModule from '../../../hooks/useEntity';

// Mock the useAllSchemas hook
vi.mock('../../../hooks/useEntity', async () => {
  const actual = await vi.importActual('../../../hooks/useEntity');
  return {
    ...actual,
    useAllSchemas: vi.fn(),
  };
});

function renderWithProviders(ui: React.ReactElement) {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
    },
  });

  return render(
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>{ui}</BrowserRouter>
    </QueryClientProvider>
  );
}

describe('AdminLayout', () => {
  const mockSchemas = {
    Article: {
      entity_name: 'Article',
      readable_name: { singular: 'Article', plural: 'Articles' },
      features: {
        supports_frontend_edit: true,
        has_author: false,
        has_tags: false,
        has_categories: false,
        has_parent_child: false,
        require_title_uniqueness: false,
      },
      fields: [],
    },
    Page: {
      entity_name: 'Page',
      readable_name: { singular: 'Page', plural: 'Pages' },
      features: {
        supports_frontend_edit: true,
        has_author: false,
        has_tags: false,
        has_categories: false,
        has_parent_child: false,
        require_title_uniqueness: false,
      },
      fields: [],
    },
  };

  beforeEach(() => {
    vi.mocked(useEntityModule.useAllSchemas).mockReturnValue({
      data: mockSchemas,
      isLoading: false,
      error: null,
    } as any);
  });

  it('should render brand/logo', () => {
    renderWithProviders(<AdminLayout />);

    expect(screen.getByText('ReflectiveForms')).toBeInTheDocument();
  });

  it('should render dashboard link', () => {
    renderWithProviders(<AdminLayout />);

    expect(screen.getByText('Dashboard')).toBeInTheDocument();
  });

  it('should render entity types section', () => {
    renderWithProviders(<AdminLayout />);

    expect(screen.getByText('Content Types')).toBeInTheDocument();
  });

  it('should render entity type links', async () => {
    renderWithProviders(<AdminLayout />);

    await waitFor(() => {
      expect(screen.getByText('Articles')).toBeInTheDocument();
      expect(screen.getByText('Pages')).toBeInTheDocument();
    });
  });

  it('should show loading state', () => {
    vi.mocked(useEntityModule.useAllSchemas).mockReturnValue({
      data: undefined,
      isLoading: true,
      error: null,
    } as any);

    renderWithProviders(<AdminLayout />);

    // Should show skeleton loaders
    const skeletons = document.querySelectorAll('.animate-pulse');
    expect(skeletons.length).toBeGreaterThan(0);
  });

  it('should render admin user section', () => {
    renderWithProviders(<AdminLayout />);

    expect(screen.getByText('Admin')).toBeInTheDocument();
    expect(screen.getByText('admin@example.com')).toBeInTheDocument();
  });

  it('should render settings button', () => {
    renderWithProviders(<AdminLayout />);

    const settingsButtons = screen.getAllByTitle('Settings');
    expect(settingsButtons.length).toBeGreaterThan(0);
  });

  it('should render logout button', () => {
    renderWithProviders(<AdminLayout />);

    expect(screen.getByTitle('Logout')).toBeInTheDocument();
  });

  it('should render breadcrumb with Home', () => {
    renderWithProviders(<AdminLayout />);

    expect(screen.getByText('Home')).toBeInTheDocument();
  });

  it('should show view-only entities (supports_frontend_edit = false) in sidebar', async () => {
    const schemasWithViewOnly = {
      ...mockSchemas,
      ViewOnlyEntity: {
        entity_name: 'ViewOnlyEntity',
        readable_name: { singular: 'ViewOnly', plural: 'View Only Entities' },
        features: {
          supports_frontend_edit: false,
          has_author: false,
          has_tags: false,
          has_categories: false,
          has_parent_child: false,
          require_title_uniqueness: false,
        },
        fields: [],
      },
    };

    vi.mocked(useEntityModule.useAllSchemas).mockReturnValue({
      data: schemasWithViewOnly,
      isLoading: false,
      error: null,
    } as any);

    renderWithProviders(<AdminLayout />);

    await waitFor(() => {
      expect(screen.getByText('Articles')).toBeInTheDocument();
      expect(screen.getByText('View Only Entities')).toBeInTheDocument();
    });
  });

  it('should toggle mobile menu', async () => {
    userEvent.setup();

    // Simulate mobile viewport
    Object.defineProperty(window, 'innerWidth', { value: 500, writable: true });
    window.dispatchEvent(new Event('resize'));

    renderWithProviders(<AdminLayout />);

    // Find the menu button (mobile toggle)
    const menuButton = document.querySelector('button[class*="lg:hidden"]');
    expect(menuButton).toBeInTheDocument();
  });
});
