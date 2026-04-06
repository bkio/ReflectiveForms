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

import { useEntity } from '../../hooks/useEntity';
import { updateEntity } from '../../api/client';
import { toast } from 'sonner';

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

const mockSheetData = {
  id: 7,
  slug: 'sales-report',
  title: { rendered: 'Sales Report' },
  date: '2025-01-01T00:00:00Z',
  date_gmt: '2025-01-01T00:00:00Z',
  modified: '2025-01-15T00:00:00Z',
  modified_gmt: '2025-01-15T00:00:00Z',
  fields: {
    sources: '[{"entity":"employee"}]',
    bound_regions: '[]',
    workbook_data: '{}',
    refresh_interval_seconds: 30,
  },
};

describe('RfSheetPage — existing sheet', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renders loading state for existing sheet', () => {
    vi.mocked(useEntity).mockReturnValue({
      data: undefined,
      isLoading: true,
      error: null,
    } as unknown as ReturnType<typeof useEntity>);

    renderSheetPage('/sheets/7');
    expect(screen.queryByPlaceholderText('Untitled Sheet')).not.toBeInTheDocument();
  });

  it('loads existing sheet title', () => {
    vi.mocked(useEntity).mockReturnValue({
      data: mockSheetData,
      isLoading: false,
      error: null,
    } as unknown as ReturnType<typeof useEntity>);

    renderSheetPage('/sheets/7');
    const input = screen.getByPlaceholderText('Untitled Sheet') as HTMLInputElement;
    expect(input.value).toBe('Sales Report');
  });

  it('calls updateEntity on save for existing sheet', async () => {
    vi.mocked(useEntity).mockReturnValue({
      data: mockSheetData,
      isLoading: false,
      error: null,
    } as unknown as ReturnType<typeof useEntity>);

    vi.mocked(updateEntity).mockResolvedValue({ data: mockSheetData });

    renderSheetPage('/sheets/7');
    await userEvent.click(screen.getByText('Save'));

    expect(updateEntity).toHaveBeenCalledWith('rf-sheets', expect.objectContaining({
      id: 7,
      title: { rendered: 'Sales Report' },
    }));
  });

  it('shows success toast on successful update', async () => {
    vi.mocked(useEntity).mockReturnValue({
      data: mockSheetData,
      isLoading: false,
      error: null,
    } as unknown as ReturnType<typeof useEntity>);

    vi.mocked(updateEntity).mockResolvedValue({ data: mockSheetData });

    renderSheetPage('/sheets/7');
    await userEvent.click(screen.getByText('Save'));

    expect(toast.success).toHaveBeenCalledWith('Sheet saved');
  });

  it('shows error toast on failed update', async () => {
    vi.mocked(useEntity).mockReturnValue({
      data: mockSheetData,
      isLoading: false,
      error: null,
    } as unknown as ReturnType<typeof useEntity>);

    vi.mocked(updateEntity).mockResolvedValue({ error: 'Update failed' });

    renderSheetPage('/sheets/7');
    await userEvent.click(screen.getByText('Save'));

    expect(toast.error).toHaveBeenCalledWith('Update failed');
  });

  it('allows editing the sheet title', async () => {
    vi.mocked(useEntity).mockReturnValue({
      data: mockSheetData,
      isLoading: false,
      error: null,
    } as unknown as ReturnType<typeof useEntity>);

    renderSheetPage('/sheets/7');
    const input = screen.getByPlaceholderText('Untitled Sheet') as HTMLInputElement;
    await userEvent.clear(input);
    await userEvent.type(input, 'Updated Report');
    expect(input.value).toBe('Updated Report');
  });
});
