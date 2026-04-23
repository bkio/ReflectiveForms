import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, act } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { RfSheetPage } from '../../pages/RfSheetPage';

// Capture the onCommandExecuted callback so tests can simulate Univer edits
let capturedCommandListener: ((params: { id: string; type: number }) => void) | null = null;

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
      onCommandExecuted: vi.fn((cb: (params: { id: string; type: number }) => void) => {
        capturedCommandListener = cb;
        return { dispose: vi.fn() };
      }),
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
  useGlobalSettings: vi.fn(() => ({})),
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
import { updateEntity } from '../../api/client';

/** Advance fake timers AND flush the microtask queue so resolved promises settle. */
async function advanceAndFlush(ms: number) {
  act(() => { vi.advanceTimersByTime(ms); });
  await act(async () => {});
}

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
  author: 1,
  access_level: 'owner' as const,
  fields: {
    sources: '[]',
    bound_regions: '[]',
    workbook_data: '{}',
    refresh_interval_seconds: 30,
  },
};

/** Render and wait for the lock to be acquired (design mode). */
async function renderExistingSheet() {
  renderSheetPage('/sheets/7');
  // Flush the lock acquisition promise (tryLockEntity is async in useEffect)
  await advanceAndFlush(0);
  // Confirm we're in design mode
  expect(screen.getByText('Save Now')).toBeInTheDocument();
}

describe('RfSheetPage — autosave', () => {
  beforeEach(() => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    vi.clearAllMocks();
    capturedCommandListener = null;
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  // ─── Autosave disabled for new sheets ──────────────────

  it('does not show autosave indicator for new sheets', async () => {
    vi.mocked(useEntity).mockReturnValue({
      data: undefined, isLoading: false, error: null,
    } as unknown as ReturnType<typeof useEntity>);

    renderSheetPage('/sheets/new');

    // Type a title to trigger potential autosave
    const input = screen.getByPlaceholderText('Untitled Sheet');
    await userEvent.type(input, 'New Sheet');

    await advanceAndFlush(6000); // past wait duration
    await advanceAndFlush(4000); // past countdown

    expect(screen.queryByTestId('autosave-indicator')).not.toBeInTheDocument();
  });

  it('shows "Save" button (not "Save Now") for new sheets', async () => {
    vi.mocked(useEntity).mockReturnValue({
      data: undefined, isLoading: false, error: null,
    } as unknown as ReturnType<typeof useEntity>);

    renderSheetPage('/sheets/new');
    expect(screen.getByText('Save')).toBeInTheDocument();
    expect(screen.queryByText('Save Now')).not.toBeInTheDocument();
  });

  // ─── Autosave disabled in view mode ────────────────────

  it('does not show autosave indicator in view mode', async () => {
    vi.mocked(useEntity).mockReturnValue({
      data: { ...mockSheetData, access_level: 'view' as const },
      isLoading: false, error: null,
    } as unknown as ReturnType<typeof useEntity>);
    vi.mocked(useCapabilities).mockReturnValue({
      data: { 'rf-sheets': { can_peek_all: true, can_read: true, can_create: false, can_update: false, can_delete: false } },
    } as unknown as ReturnType<typeof useCapabilities>);

    renderSheetPage('/sheets/7');

    await advanceAndFlush(10000);

    expect(screen.queryByTestId('autosave-indicator')).not.toBeInTheDocument();
    expect(screen.queryByText('Save Now')).not.toBeInTheDocument();
  });

  // ─── Full autosave cycle via Univer command ────────────

  it('triggers autosave when Univer command executes, saves after wait + countdown', async () => {
    vi.mocked(useEntity).mockReturnValue({
      data: mockSheetData, isLoading: false, error: null,
    } as unknown as ReturnType<typeof useEntity>);
    vi.mocked(updateEntity).mockResolvedValue({ data: mockSheetData });

    await renderExistingSheet();

    // Simulate a Univer user-level command (type=0)
    expect(capturedCommandListener).not.toBeNull();
    act(() => { capturedCommandListener!({ id: 'sheet.mutation.set-cell', type: 0 }); });

    // Advance past wait period (5000ms)
    await advanceAndFlush(5100);

    // Should be in checking or countdown now — advance past countdown (3000ms)
    await advanceAndFlush(3100);

    // Save should have been called
    expect(updateEntity).toHaveBeenCalledWith('rf-sheets', expect.objectContaining({
      id: 7,
      title: { rendered: 'Sales Report' },
    }));

    // Saved indicator should appear
    expect(screen.getByTestId('autosave-saved')).toBeInTheDocument();
  });

  // ─── Title change triggers autosave ────────────────────

  it('triggers autosave when title is changed on existing sheet', async () => {
    vi.mocked(useEntity).mockReturnValue({
      data: mockSheetData, isLoading: false, error: null,
    } as unknown as ReturnType<typeof useEntity>);
    vi.mocked(updateEntity).mockResolvedValue({ data: mockSheetData });

    await renderExistingSheet();

    const input = screen.getByPlaceholderText('Untitled Sheet');
    await userEvent.type(input, ' Updated');

    // Advance past wait + countdown
    await advanceAndFlush(5100);
    await advanceAndFlush(3100);

    expect(updateEntity).toHaveBeenCalledWith('rf-sheets', expect.objectContaining({
      title: { rendered: 'Sales Report Updated' },
    }));
  });

  // ─── Sanity check fails when title is empty ────────────

  it('shows validation error when title is empty', async () => {
    vi.mocked(useEntity).mockReturnValue({
      data: mockSheetData, isLoading: false, error: null,
    } as unknown as ReturnType<typeof useEntity>);

    await renderExistingSheet();

    // Clear the title
    const input = screen.getByPlaceholderText('Untitled Sheet');
    await userEvent.clear(input);

    // Trigger autosave via Univer command
    act(() => { capturedCommandListener!({ id: 'sheet.mutation.set-cell', type: 0 }); });

    // Advance past wait period
    await advanceAndFlush(5100);

    // Validation error should appear
    expect(screen.getByTestId('autosave-validation-error')).toBeInTheDocument();
    expect(screen.getByText('Sheet name is required')).toBeInTheDocument();

    // updateEntity should NOT have been called
    expect(updateEntity).not.toHaveBeenCalled();
  });

  // ─── Save Now bypasses autosave timers ─────────────────

  it('Save Now bypasses countdown and saves immediately', async () => {
    vi.mocked(useEntity).mockReturnValue({
      data: mockSheetData, isLoading: false, error: null,
    } as unknown as ReturnType<typeof useEntity>);
    vi.mocked(updateEntity).mockResolvedValue({ data: mockSheetData });

    await renderExistingSheet();

    // Start an autosave via Univer command
    act(() => { capturedCommandListener!({ id: 'sheet.mutation.set-cell', type: 0 }); });

    // Advance partially into wait — should NOT have saved yet
    await advanceAndFlush(2000);
    expect(updateEntity).not.toHaveBeenCalled();

    // Click Save Now — should save immediately
    await userEvent.click(screen.getByText('Save Now'));
    await advanceAndFlush(100);

    expect(updateEntity).toHaveBeenCalledTimes(1);
    expect(screen.getByTestId('autosave-saved')).toBeInTheDocument();
  });

  // ─── Error state ───────────────────────────────────────

  it('shows error indicator when autosave fails', async () => {
    vi.mocked(useEntity).mockReturnValue({
      data: mockSheetData, isLoading: false, error: null,
    } as unknown as ReturnType<typeof useEntity>);
    vi.mocked(updateEntity).mockResolvedValue({ error: 'Server error' });

    await renderExistingSheet();

    act(() => { capturedCommandListener!({ id: 'sheet.mutation.set-cell', type: 0 }); });

    // Advance past wait + countdown
    await advanceAndFlush(5100);
    await advanceAndFlush(3100);

    expect(updateEntity).toHaveBeenCalled();
    expect(screen.getByTestId('autosave-error')).toBeInTheDocument();
  });

  // ─── Debounce: rapid edits reset wait timer ────────────

  it('resets wait timer on rapid Univer edits (debounce)', async () => {
    vi.mocked(useEntity).mockReturnValue({
      data: mockSheetData, isLoading: false, error: null,
    } as unknown as ReturnType<typeof useEntity>);
    vi.mocked(updateEntity).mockResolvedValue({ data: mockSheetData });

    await renderExistingSheet();

    // First edit
    act(() => { capturedCommandListener!({ id: 'sheet.mutation.set-cell', type: 0 }); });

    // Advance 3s (within the 5s wait window)
    await advanceAndFlush(3000);
    expect(updateEntity).not.toHaveBeenCalled();

    // Second edit — resets the wait timer
    act(() => { capturedCommandListener!({ id: 'sheet.mutation.set-cell', type: 0 }); });

    // Advance another 3s — only 3s since last edit, not enough (wait = 5s)
    await advanceAndFlush(3000);
    expect(updateEntity).not.toHaveBeenCalled();

    // Advance remaining 2.1s to pass the 5s wait from last edit
    await advanceAndFlush(2100);
    // Then countdown (3s)
    await advanceAndFlush(3100);

    expect(updateEntity).toHaveBeenCalledTimes(1);
  });

  // ─── OPERATION type commands do NOT trigger autosave ───

  it('does not trigger autosave for OPERATION type commands (scroll, selection)', async () => {
    vi.mocked(useEntity).mockReturnValue({
      data: mockSheetData, isLoading: false, error: null,
    } as unknown as ReturnType<typeof useEntity>);

    await renderExistingSheet();

    // Simulate an OPERATION command (type=1, e.g. scroll/selection)
    act(() => { capturedCommandListener!({ id: 'sheet.operation.set-scroll', type: 1 }); });

    // Advance past wait + countdown
    await advanceAndFlush(5100);
    await advanceAndFlush(3100);

    expect(updateEntity).not.toHaveBeenCalled();
    expect(screen.queryByTestId('autosave-indicator')).not.toBeInTheDocument();
  });

  // ─── Saved indicator auto-dismisses ────────────────────

  it('saved indicator auto-dismisses after 2s', async () => {
    vi.mocked(useEntity).mockReturnValue({
      data: mockSheetData, isLoading: false, error: null,
    } as unknown as ReturnType<typeof useEntity>);
    vi.mocked(updateEntity).mockResolvedValue({ data: mockSheetData });

    await renderExistingSheet();

    act(() => { capturedCommandListener!({ id: 'sheet.mutation.set-cell', type: 0 }); });
    await advanceAndFlush(5100);
    await advanceAndFlush(3100);

    expect(screen.getByTestId('autosave-saved')).toBeInTheDocument();

    // Wait 2s for auto-dismiss
    await advanceAndFlush(2100);

    expect(screen.queryByTestId('autosave-saved')).not.toBeInTheDocument();
  });
});
