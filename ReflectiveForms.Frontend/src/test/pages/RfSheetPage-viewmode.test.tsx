import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
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
import { detectStaleFields } from '../../lib/rf-sheet-schema-validator';

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

describe('RfSheetPage — view mode & stale fields', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('hides save button in view mode', () => {
    const mockSheetData = {
      id: 7, slug: 'report', title: { rendered: 'Report' },
      date: '', date_gmt: '', modified: '', modified_gmt: '',
      fields: { sources: '[]', bound_regions: '[]', workbook_data: '{}', refresh_interval_seconds: 30 },
    };
    vi.mocked(useEntity).mockReturnValue({ data: mockSheetData, isLoading: false, error: null } as unknown as ReturnType<typeof useEntity>);
    vi.mocked(useCapabilities).mockReturnValue({
      data: { 'rf-sheets': { can_peek_all: true, can_read: true, can_create: false, can_update: false, can_delete: false } },
    } as unknown as ReturnType<typeof useCapabilities>);

    renderSheetPage('/sheets/7');
    expect(screen.queryByText('Save')).not.toBeInTheDocument();
  });

  it('hides entity panel toggle in view mode', () => {
    const mockSheetData = {
      id: 7, slug: 'report', title: { rendered: 'Report' },
      date: '', date_gmt: '', modified: '', modified_gmt: '',
      fields: { sources: '[]', bound_regions: '[]', workbook_data: '{}', refresh_interval_seconds: 30 },
    };
    vi.mocked(useEntity).mockReturnValue({ data: mockSheetData, isLoading: false, error: null } as unknown as ReturnType<typeof useEntity>);
    vi.mocked(useCapabilities).mockReturnValue({
      data: { 'rf-sheets': { can_peek_all: true, can_read: true, can_create: false, can_update: false, can_delete: false } },
    } as unknown as ReturnType<typeof useCapabilities>);

    renderSheetPage('/sheets/7');
    expect(screen.queryByTitle('Hide entity panel')).not.toBeInTheDocument();
    expect(screen.queryByTitle('Show entity panel')).not.toBeInTheDocument();
  });

  it('shows static title instead of input in view mode', () => {
    const mockSheetData = {
      id: 7, slug: 'report', title: { rendered: 'Sales Report' },
      date: '', date_gmt: '', modified: '', modified_gmt: '',
      fields: { sources: '[]', bound_regions: '[]', workbook_data: '{}', refresh_interval_seconds: 30 },
    };
    vi.mocked(useEntity).mockReturnValue({ data: mockSheetData, isLoading: false, error: null } as unknown as ReturnType<typeof useEntity>);
    vi.mocked(useCapabilities).mockReturnValue({
      data: { 'rf-sheets': { can_peek_all: true, can_read: true, can_create: false, can_update: false, can_delete: false } },
    } as unknown as ReturnType<typeof useCapabilities>);

    renderSheetPage('/sheets/7');
    expect(screen.queryByPlaceholderText('Untitled Sheet')).not.toBeInTheDocument();
    expect(screen.getByText('Sales Report')).toBeInTheDocument();
  });

  it('still shows refresh and export buttons in view mode', () => {
    const mockSheetData = {
      id: 7, slug: 'report', title: { rendered: 'Report' },
      date: '', date_gmt: '', modified: '', modified_gmt: '',
      fields: { sources: '[]', bound_regions: '[]', workbook_data: '{}', refresh_interval_seconds: 30 },
    };
    vi.mocked(useEntity).mockReturnValue({ data: mockSheetData, isLoading: false, error: null } as unknown as ReturnType<typeof useEntity>);
    vi.mocked(useCapabilities).mockReturnValue({
      data: { 'rf-sheets': { can_peek_all: true, can_read: true, can_create: false, can_update: false, can_delete: false } },
    } as unknown as ReturnType<typeof useCapabilities>);

    renderSheetPage('/sheets/7');
    expect(screen.getByTitle('Refresh data')).toBeInTheDocument();
    expect(screen.getByTitle('Export to .xlsx')).toBeInTheDocument();
  });

  it('shows warning banner when stale fields are detected', () => {
    const mockSheetData = {
      id: 7, slug: 'report', title: { rendered: 'Report' },
      date: '', date_gmt: '', modified: '', modified_gmt: '',
      fields: {
        sources: '[]', bound_regions: '[]',
        workbook_data: '{"sheets":{"s1":{"cellData":{"0":{"0":{"f":"=RF.LIST(\\"employee\\",\\"removed_field\\")"}}}}}}',
        refresh_interval_seconds: 30,
      },
    };
    vi.mocked(useEntity).mockReturnValue({ data: mockSheetData, isLoading: false, error: null } as unknown as ReturnType<typeof useEntity>);
    vi.mocked(detectStaleFields).mockReturnValue([
      { entity: 'employee', field: 'removed_field' },
    ]);

    renderSheetPage('/sheets/7');
    expect(screen.getByText(/references fields that no longer exist/)).toBeInTheDocument();
    expect(screen.getByText(/employee\.removed_field/)).toBeInTheDocument();
  });

  it('does not show warning banner when no stale fields', () => {
    vi.mocked(useEntity).mockReturnValue({ data: undefined, isLoading: false, error: null } as unknown as ReturnType<typeof useEntity>);
    vi.mocked(detectStaleFields).mockReturnValue([]);

    renderSheetPage('/sheets/new');
    expect(screen.queryByText(/references fields that no longer exist/)).not.toBeInTheDocument();
  });
});
