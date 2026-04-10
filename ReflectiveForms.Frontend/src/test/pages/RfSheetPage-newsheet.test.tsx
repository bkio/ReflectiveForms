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

import { useEntity } from '../../hooks/useEntity';
import { createEntity } from '../../api/client';
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

describe('RfSheetPage — new sheet', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renders title input for new sheet', () => {
    vi.mocked(useEntity).mockReturnValue({
      data: undefined,
      isLoading: false,
      error: null,
    } as unknown as ReturnType<typeof useEntity>);

    renderSheetPage('/sheets/new');
    const input = screen.getByPlaceholderText('Untitled Sheet');
    expect(input).toBeInTheDocument();
  });

  it('renders save button', () => {
    vi.mocked(useEntity).mockReturnValue({
      data: undefined,
      isLoading: false,
      error: null,
    } as unknown as ReturnType<typeof useEntity>);

    renderSheetPage('/sheets/new');
    expect(screen.getByText('Save')).toBeInTheDocument();
  });

  it('renders Univer spreadsheet container', () => {
    vi.mocked(useEntity).mockReturnValue({
      data: undefined,
      isLoading: false,
      error: null,
    } as unknown as ReturnType<typeof useEntity>);

    renderSheetPage('/sheets/new');
    expect(screen.getByText('Save')).toBeInTheDocument();
  });

  it('shows error toast when saving without title', async () => {
    vi.mocked(useEntity).mockReturnValue({
      data: undefined,
      isLoading: false,
      error: null,
    } as unknown as ReturnType<typeof useEntity>);

    renderSheetPage('/sheets/new');
    const saveButton = screen.getByText('Save');
    await userEvent.click(saveButton);

    expect(toast.error).toHaveBeenCalledWith('Sheet name is required');
  });

  it('calls createEntity on save with title', async () => {
    vi.mocked(useEntity).mockReturnValue({
      data: undefined,
      isLoading: false,
      error: null,
    } as unknown as ReturnType<typeof useEntity>);

    vi.mocked(createEntity).mockResolvedValue({ data: { id: 1, slug: 'test', title: { rendered: 'My Sheet' }, date: '', date_gmt: '', modified: '', modified_gmt: '', fields: {} } });

    renderSheetPage('/sheets/new');
    const input = screen.getByPlaceholderText('Untitled Sheet');
    await userEvent.type(input, 'My Sheet');
    await userEvent.click(screen.getByText('Save'));

    expect(createEntity).toHaveBeenCalledWith('rf-sheets', expect.objectContaining({
      title: { rendered: 'My Sheet' },
      fields: expect.objectContaining({
        sources: '[]',
        refresh_interval_seconds: 30,
      }),
    }));
  });

  it('shows success toast after creating sheet', async () => {
    vi.mocked(useEntity).mockReturnValue({
      data: undefined,
      isLoading: false,
      error: null,
    } as unknown as ReturnType<typeof useEntity>);

    vi.mocked(createEntity).mockResolvedValue({ data: { id: 5, slug: 'new-sheet', title: { rendered: 'New Sheet' }, date: '', date_gmt: '', modified: '', modified_gmt: '', fields: {} } });

    renderSheetPage('/sheets/new');
    await userEvent.type(screen.getByPlaceholderText('Untitled Sheet'), 'New Sheet');
    await userEvent.click(screen.getByText('Save'));

    expect(toast.success).toHaveBeenCalledWith('Sheet created');
  });

  it('shows error toast when createEntity fails', async () => {
    vi.mocked(useEntity).mockReturnValue({
      data: undefined,
      isLoading: false,
      error: null,
    } as unknown as ReturnType<typeof useEntity>);

    vi.mocked(createEntity).mockResolvedValue({ error: 'Server error' });

    renderSheetPage('/sheets/new');
    await userEvent.type(screen.getByPlaceholderText('Untitled Sheet'), 'Test');
    await userEvent.click(screen.getByText('Save'));

    expect(toast.error).toHaveBeenCalledWith('Server error');
  });
});
