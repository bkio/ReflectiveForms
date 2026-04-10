import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { RfSheetPage } from '../../pages/RfSheetPage';

// Mock Univer — it can't run in jsdom
vi.mock('@univerjs/preset-sheets-core', () => ({
  UniverSheetsCorePreset: vi.fn(() => ({})),
}));
vi.mock('@univerjs/preset-sheets-core/locales/en-US', () => ({ default: {} }));
vi.mock('@univerjs/preset-sheets-core/lib/index.css', () => ({}));
vi.mock('@univerjs/presets', () => ({
  createUniver: vi.fn(() => ({
    univerAPI: {
      createWorkbook: vi.fn(),
      dispose: vi.fn(),
      addEvent: vi.fn(() => ({ dispose: vi.fn() })),
      onCommandExecuted: vi.fn(() => ({ dispose: vi.fn() })),
      Event: { BeforeSheetEditStart: 'BeforeSheetEditStart' },
      getFormula: vi.fn(() => ({
        registerFunction: vi.fn(() => ({ dispose: vi.fn() })),
        executeCalculation: vi.fn(),
        calculationEnd: vi.fn((_cb: unknown) => ({ dispose: vi.fn() })),
      })),
      getActiveWorkbook: vi.fn(() => ({
        save: vi.fn(() => ({})),
        getActiveSheet: vi.fn(() => ({
          getSelection: vi.fn(() => null),
        })),
      })),
    },
  })),
  LocaleType: { EN_US: 'en-US' },
  mergeLocales: vi.fn((...args: unknown[]) => args[0]),
}));

const stableSchemas = { data: {} };
const stableCapabilities = {
  data: { 'rf-sheets': { can_peek_all: true, can_read: true, can_create: true, can_update: true, can_delete: true } },
};
const stableDataStore = {
  entityData: new Map(),
  unauthorizedEntities: new Set(),
  isLoading: false,
  error: null,
  refresh: vi.fn(),
  getEntityField: vi.fn(() => '#NO_DATA'),
  getAllEntityRows: vi.fn(() => []),
};

vi.mock('../../hooks/useEntity', () => ({
  useEntity: vi.fn(),
  useAllSchemas: vi.fn(() => stableSchemas),
  useCapabilities: vi.fn(() => stableCapabilities),
}));

vi.mock('../../hooks/useAuth', () => ({
  useAuth: vi.fn(() => ({ user: { id: 1, name: 'Test User', email: 'test@example.com' } })),
}));

vi.mock('../../api/client', () => ({
  createEntity: vi.fn(),
  updateEntity: vi.fn(),
  bulkRead: vi.fn(() => Promise.resolve({ data: { results: [], unauthorized: [] } })),
  tryLockEntity: vi.fn(() => Promise.resolve({ data: { locked: true } })),
  unlockEntity: vi.fn(() => Promise.resolve({ data: {} })),
  fetchLockStatus: vi.fn(() => Promise.resolve({ data: null })),
  getApiBaseUrl: vi.fn(() => 'http://localhost:9000/rf/api'),
}));

vi.mock('sonner', () => ({
  toast: { success: vi.fn(), error: vi.fn() },
}));

vi.mock('../../lib/rf-sheet-schema-validator', () => ({
  detectStaleFields: vi.fn(() => []),
}));

vi.mock('../../lib/rf-sheet-export', () => ({
  exportWorkbookToXlsx: vi.fn(),
}));

vi.mock('../../hooks/useRfSheetData', () => ({
  useRfSheetData: vi.fn(() => stableDataStore),
}));

import { useEntity, useCapabilities } from '../../hooks/useEntity';

function renderSheetPage(path: string) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[path]}>
        <Routes>
          <Route path="/sheets/:sheetId" element={<RfSheetPage />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('RfSheetPage — UI features & design mode', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renders refresh button', () => {
    vi.mocked(useEntity).mockReturnValue({
      data: undefined, isLoading: false, error: null,
    } as unknown as ReturnType<typeof useEntity>);
    renderSheetPage('/sheets/new');
    expect(screen.getByTitle('Refresh data')).toBeInTheDocument();
  });

  it('renders entity panel toggle button', () => {
    vi.mocked(useEntity).mockReturnValue({
      data: undefined, isLoading: false, error: null,
    } as unknown as ReturnType<typeof useEntity>);
    renderSheetPage('/sheets/new');
    expect(screen.getByTitle('Hide entity panel')).toBeInTheDocument();
  });

  it('toggles entity panel on click', async () => {
    vi.mocked(useEntity).mockReturnValue({
      data: undefined, isLoading: false, error: null,
    } as unknown as ReturnType<typeof useEntity>);
    renderSheetPage('/sheets/new');
    const toggleBtn = screen.getByTitle('Hide entity panel');
    await userEvent.click(toggleBtn);
    expect(screen.getByTitle('Show entity panel')).toBeInTheDocument();
  });

  it('handles sheet entity being removed (returns undefined data)', () => {
    vi.mocked(useEntity).mockReturnValue({
      data: undefined, isLoading: false, error: null,
    } as unknown as ReturnType<typeof useEntity>);
    renderSheetPage('/sheets/999');
    // Without data, existing sheet has no access_level → view-only mode (h1, not input)
    expect(screen.getByText('Untitled Sheet')).toBeInTheDocument();
  });

  it('renders export button', () => {
    vi.mocked(useEntity).mockReturnValue({
      data: undefined, isLoading: false, error: null,
    } as unknown as ReturnType<typeof useEntity>);
    renderSheetPage('/sheets/new');
    expect(screen.getByTitle('Export to .xlsx')).toBeInTheDocument();
  });

  it('shows save button, panel toggle, and editable title in design mode', () => {
    vi.mocked(useEntity).mockReturnValue({
      data: undefined, isLoading: false, error: null,
    } as unknown as ReturnType<typeof useEntity>);
    vi.mocked(useCapabilities).mockReturnValue({
      data: { 'rf-sheets': { can_peek_all: true, can_read: true, can_create: true, can_update: true, can_delete: true } },
    } as unknown as ReturnType<typeof useCapabilities>);
    renderSheetPage('/sheets/new');
    expect(screen.getByText('Save')).toBeInTheDocument();
    expect(screen.getByTitle('Hide entity panel')).toBeInTheDocument();
    expect(screen.getByPlaceholderText('Untitled Sheet')).toBeInTheDocument();
  });
});
