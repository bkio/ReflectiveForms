import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { BrowserRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { AdminLayout } from '../../../components/layout/AdminLayout';
import { RfConfigProvider } from '../../../lib/RfConfigProvider';
import type { RfConfig } from '../../../lib/types';
import * as useEntityModule from '../../../hooks/useEntity';

// Mock the useAllSchemas hook
vi.mock('../../../hooks/useEntity', async () => {
  const actual = await vi.importActual('../../../hooks/useEntity');
  return {
    ...actual,
    useAllSchemas: vi.fn(),
  };
});

// Mock client setApiBaseUrl to avoid side effects
vi.mock('../../../api/client', () => ({
  setApiBaseUrl: vi.fn(),
  getApiBaseUrl: vi.fn(() => 'http://test/rf/api'),
}));

const defaultConfig: RfConfig = {
  apiBaseUrl: 'http://test/rf/api',
};

function renderWithProviders(ui: React.ReactElement, config: RfConfig = defaultConfig) {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
    },
  });

  return render(
    <RfConfigProvider config={config}>
      <QueryClientProvider client={queryClient}>
        <BrowserRouter>{ui}</BrowserRouter>
      </QueryClientProvider>
    </RfConfigProvider>
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

    expect(screen.getByTestId('brand-name')).toHaveTextContent('ReflectiveForms');
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
    // Email is not rendered when AuthProvider is not present (auth is null)
  });

  it('should render dark mode toggle', () => {
    renderWithProviders(<AdminLayout />);

    const toggleButtons = screen.getAllByTitle('Dark mode');
    expect(toggleButtons.length).toBeGreaterThan(0);
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

  // --- Branding tests ---

  it('should render custom appName from config', () => {
    renderWithProviders(<AdminLayout />, {
      ...defaultConfig,
      appName: 'School Manager',
    });

    expect(screen.getByTestId('brand-name')).toHaveTextContent('School Manager');
  });

  it('should render custom logo as image URL', () => {
    renderWithProviders(<AdminLayout />, {
      ...defaultConfig,
      appName: 'My App',
      logo: '/logo.svg',
    });

    const logoImg = document.querySelector('img[src="/logo.svg"]');
    expect(logoImg).toBeInTheDocument();
  });

  it('should render custom logo as React component', () => {
    const CustomLogo = ({ className }: { className?: string }) => (
      <svg data-testid="custom-logo" className={className}><circle r="10" /></svg>
    );

    renderWithProviders(<AdminLayout />, {
      ...defaultConfig,
      logo: CustomLogo,
    });

    expect(screen.getByTestId('custom-logo')).toBeInTheDocument();
  });

  it('should apply primaryColor as CSS variable', () => {
    renderWithProviders(<AdminLayout />, {
      ...defaultConfig,
      primaryColor: '#e11d48',
    });

    expect(document.documentElement.style.getPropertyValue('--rf-primary')).toBe('#e11d48');
  });

  // --- Custom pages tests ---

  it('should render custom pages in sidebar', () => {
    const TestIcon = ({ className }: { className?: string }) => <span className={className}>📊</span>;
    const TestPage = () => <div>test page</div>;

    renderWithProviders(<AdminLayout />, {
      ...defaultConfig,
      customPages: [{
        path: '/analytics',
        label: 'Analytics Dashboard',
        icon: TestIcon,
        component: TestPage,
        section: 'Reports',
      }],
    });

    expect(screen.getByText('Analytics Dashboard')).toBeInTheDocument();
  });

  it('should group custom pages by section heading', () => {
    const TestIcon = ({ className }: { className?: string }) => <span className={className}>icon</span>;
    const PageA = () => <div>a</div>;
    const PageB = () => <div>b</div>;

    renderWithProviders(<AdminLayout />, {
      ...defaultConfig,
      customPages: [
        { path: '/a', label: 'Page A', icon: TestIcon, component: PageA, section: 'Analytics' },
        { path: '/b', label: 'Page B', icon: TestIcon, component: PageB, section: 'Analytics' },
      ],
    });

    const analyticsSection = screen.getByTestId('custom-section-analytics');
    expect(analyticsSection).toBeInTheDocument();
    expect(analyticsSection).toHaveTextContent('Analytics');
    expect(analyticsSection).toHaveTextContent('Page A');
    expect(analyticsSection).toHaveTextContent('Page B');
  });

  it('should render custom pages under "Custom" section when no section specified', () => {
    const TestIcon = ({ className }: { className?: string }) => <span className={className}>icon</span>;
    const TestPage = () => <div>test</div>;

    renderWithProviders(<AdminLayout />, {
      ...defaultConfig,
      customPages: [{
        path: '/my-page',
        label: 'My Page',
        icon: TestIcon,
        component: TestPage,
      }],
    });

    const customSection = screen.getByTestId('custom-section-custom');
    expect(customSection).toBeInTheDocument();
    expect(customSection).toHaveTextContent('My Page');
  });

  it('should not render custom sections when no custom pages', () => {
    renderWithProviders(<AdminLayout />);

    const customSections = document.querySelectorAll('[data-testid^="custom-section-"]');
    expect(customSections.length).toBe(0);
  });
});
