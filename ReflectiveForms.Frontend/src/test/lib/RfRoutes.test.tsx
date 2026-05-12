import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter, Routes } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { RfRoutes } from '../../lib/RfRoutes';

const mockUseGlobalSettings = vi.fn(() => ({}));

vi.mock('../../hooks/useEntity', () => ({
  useGlobalSettings: (...args: unknown[]) => mockUseGlobalSettings(...args),
}));

// Stub out page components to avoid pulling in complex dependencies
vi.mock('../../pages/DashboardPage', () => ({
  DashboardPage: () => <div data-testid="dashboard">Dashboard</div>,
}));
vi.mock('../../pages/EntityListPage', () => ({
  EntityListPage: () => <div>EntityList</div>,
}));
vi.mock('../../pages/EntityEditPage', () => ({
  EntityEditPage: () => <div>EntityEdit</div>,
}));
vi.mock('../../pages/EntityViewPage', () => ({
  EntityViewPage: () => <div>EntityView</div>,
}));
vi.mock('../../pages/RevisionDiffPage', () => ({
  RevisionDiffPage: () => <div>RevisionDiff</div>,
}));
vi.mock('../../pages/RfSheetListPage', () => ({
  RfSheetListPage: () => <div data-testid="sheet-list">SheetList</div>,
}));
vi.mock('../../pages/RfSheetPage', () => ({
  RfSheetPage: () => <div data-testid="sheet-page">SheetPage</div>,
}));

function renderWithRoute(initialPath: string) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter initialEntries={[initialPath]}>
        <Routes>{RfRoutes()}</Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('RfRoutes', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  describe('sheets enabled (default)', () => {
    beforeEach(() => {
      mockUseGlobalSettings.mockReturnValue({});
    });

    it('renders sheet list route at /sheets', () => {
      renderWithRoute('/sheets');
      expect(screen.getByTestId('sheet-list')).toBeInTheDocument();
    });

    it('renders sheet page route at /sheets/:sheetId', () => {
      renderWithRoute('/sheets/abc-123');
      expect(screen.getByTestId('sheet-page')).toBeInTheDocument();
    });

    it('renders dashboard at /', () => {
      renderWithRoute('/');
      expect(screen.getByTestId('dashboard')).toBeInTheDocument();
    });
  });

  describe('sheets explicitly enabled', () => {
    beforeEach(() => {
      mockUseGlobalSettings.mockReturnValue({ sheets_enabled: true });
    });

    it('renders sheet list route at /sheets', () => {
      renderWithRoute('/sheets');
      expect(screen.getByTestId('sheet-list')).toBeInTheDocument();
    });
  });

  describe('sheets disabled', () => {
    beforeEach(() => {
      mockUseGlobalSettings.mockReturnValue({ sheets_enabled: false });
    });

    it('does NOT render sheet list route at /sheets', () => {
      renderWithRoute('/sheets');
      expect(screen.queryByTestId('sheet-list')).not.toBeInTheDocument();
    });

    it('does NOT render sheet page route at /sheets/:sheetId', () => {
      renderWithRoute('/sheets/abc-123');
      expect(screen.queryByTestId('sheet-page')).not.toBeInTheDocument();
    });

    it('still renders dashboard at /', () => {
      renderWithRoute('/');
      expect(screen.getByTestId('dashboard')).toBeInTheDocument();
    });
  });
});
