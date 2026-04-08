import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { useRfSheetData } from '../../hooks/useRfSheetData';

vi.mock('../../api/client', () => ({
  bulkRead: vi.fn(),
}));

import { bulkRead } from '../../api/client';

describe('useRfSheetData', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  // ── Initial Fetch ──────────────────────────────────────────────────────

  it('fetches data on mount', async () => {
    vi.mocked(bulkRead).mockResolvedValue({
      data: {
        results: [
          { entity: 'employee', total_count: 2, rows: [
            { id: 1, fields: { name: 'Alice' } },
            { id: 2, fields: { name: 'Bob' } },
          ]},
        ],
        unauthorized: [],
      },
    });

    const { result } = renderHook(() =>
      useRfSheetData([{ entity: 'employee' }], 0),
    );

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false);
    });

    expect(bulkRead).toHaveBeenCalledWith([{ entity: 'employee' }]);
    expect(result.current.entityData.get('employee')?.size).toBe(2);
    expect(result.current.error).toBeNull();
  });

  it('does not fetch when sources are empty', async () => {
    const { result } = renderHook(() =>
      useRfSheetData([], 0),
    );

    expect(bulkRead).not.toHaveBeenCalled();
    expect(result.current.entityData.size).toBe(0);
  });

  // ── Entity Data Access ─────────────────────────────────────────────────

  it('getEntityField returns field value', async () => {
    vi.mocked(bulkRead).mockResolvedValue({
      data: {
        results: [
          { entity: 'employee', total_count: 1, rows: [
            { id: 5, fields: { name: 'Charlie', salary: 50000 } },
          ]},
        ],
        unauthorized: [],
      },
    });

    const { result } = renderHook(() =>
      useRfSheetData([{ entity: 'employee' }], 0),
    );

    await waitFor(() => expect(result.current.isLoading).toBe(false));

    expect(result.current.getEntityField('employee', 5, 'name')).toBe('Charlie');
    expect(result.current.getEntityField('employee', 5, 'salary')).toBe(50000);
  });

  it('getEntityField returns #NO_ACCESS for unauthorized entity', async () => {
    vi.mocked(bulkRead).mockResolvedValue({
      data: {
        results: [],
        unauthorized: ['salary_band'],
      },
    });

    const { result } = renderHook(() =>
      useRfSheetData([{ entity: 'salary_band' }], 0),
    );

    await waitFor(() => expect(result.current.isLoading).toBe(false));

    expect(result.current.getEntityField('salary_band', 1, 'amount')).toBe('#NO_ACCESS');
  });

  it('getEntityField returns #NO_DATA for missing entity', async () => {
    vi.mocked(bulkRead).mockResolvedValue({
      data: { results: [], unauthorized: [] },
    });

    const { result } = renderHook(() =>
      useRfSheetData([{ entity: 'employee' }], 0),
    );

    await waitFor(() => expect(result.current.isLoading).toBe(false));

    expect(result.current.getEntityField('nonexistent', 1, 'name')).toBe('#NO_DATA');
  });

  it('getEntityField returns #NOT_FOUND for missing row', async () => {
    vi.mocked(bulkRead).mockResolvedValue({
      data: {
        results: [
          { entity: 'employee', total_count: 1, rows: [
            { id: 1, fields: { name: 'Alice' } },
          ]},
        ],
        unauthorized: [],
      },
    });

    const { result } = renderHook(() =>
      useRfSheetData([{ entity: 'employee' }], 0),
    );

    await waitFor(() => expect(result.current.isLoading).toBe(false));

    expect(result.current.getEntityField('employee', 999, 'name')).toBe('#NOT_FOUND');
  });

  it('getEntityField returns #FIELD_REMOVED for missing field', async () => {
    vi.mocked(bulkRead).mockResolvedValue({
      data: {
        results: [
          { entity: 'employee', total_count: 1, rows: [
            { id: 1, fields: { name: 'Alice' } },
          ]},
        ],
        unauthorized: [],
      },
    });

    const { result } = renderHook(() =>
      useRfSheetData([{ entity: 'employee' }], 0),
    );

    await waitFor(() => expect(result.current.isLoading).toBe(false));

    expect(result.current.getEntityField('employee', 1, 'deleted_field')).toBe('#FIELD_REMOVED');
  });

  // ── getAllEntityRows ────────────────────────────────────────────────────

  it('getAllEntityRows returns all rows for an entity', async () => {
    vi.mocked(bulkRead).mockResolvedValue({
      data: {
        results: [
          { entity: 'employee', total_count: 3, rows: [
            { id: 1, fields: { name: 'Alice' } },
            { id: 2, fields: { name: 'Bob' } },
            { id: 3, fields: { name: 'Charlie' } },
          ]},
        ],
        unauthorized: [],
      },
    });

    const { result } = renderHook(() =>
      useRfSheetData([{ entity: 'employee' }], 0),
    );

    await waitFor(() => expect(result.current.isLoading).toBe(false));

    const rows = result.current.getAllEntityRows('employee');
    expect(rows).toHaveLength(3);
    expect(rows[0]).toEqual({ id: 1, fields: { name: 'Alice' } });
  });

  it('getAllEntityRows returns empty array for missing entity', async () => {
    vi.mocked(bulkRead).mockResolvedValue({
      data: { results: [], unauthorized: [] },
    });

    const { result } = renderHook(() =>
      useRfSheetData([{ entity: 'employee' }], 0),
    );

    await waitFor(() => expect(result.current.isLoading).toBe(false));

    expect(result.current.getAllEntityRows('nonexistent')).toEqual([]);
  });

  // ── Unauthorized ───────────────────────────────────────────────────────

  it('tracks unauthorized entities', async () => {
    vi.mocked(bulkRead).mockResolvedValue({
      data: {
        results: [
          { entity: 'employee', total_count: 1, rows: [{ id: 1, fields: { name: 'Alice' } }] },
        ],
        unauthorized: ['salary_band', 'secret_data'],
      },
    });

    const { result } = renderHook(() =>
      useRfSheetData([{ entity: 'employee' }, { entity: 'salary_band' }, { entity: 'secret_data' }], 0),
    );

    await waitFor(() => expect(result.current.isLoading).toBe(false));

    expect(result.current.unauthorizedEntities.has('salary_band')).toBe(true);
    expect(result.current.unauthorizedEntities.has('secret_data')).toBe(true);
    expect(result.current.unauthorizedEntities.has('employee')).toBe(false);
  });

  // ── Error Handling ─────────────────────────────────────────────────────

  it('sets error when bulkRead returns error', async () => {
    vi.mocked(bulkRead).mockResolvedValue({
      error: 'Server error',
    });

    const { result } = renderHook(() =>
      useRfSheetData([{ entity: 'employee' }], 0),
    );

    await waitFor(() => expect(result.current.isLoading).toBe(false));

    expect(result.current.error).toBe('Server error');
  });

  it('sets error when fetch throws', async () => {
    vi.mocked(bulkRead).mockRejectedValue(new Error('Network failure'));

    const { result } = renderHook(() =>
      useRfSheetData([{ entity: 'employee' }], 0),
    );

    await waitFor(() => expect(result.current.isLoading).toBe(false));

    expect(result.current.error).toBe('Network failure');
  });

  // ── Multiple Entities ──────────────────────────────────────────────────

  it('handles multiple entity sources', async () => {
    vi.mocked(bulkRead).mockResolvedValue({
      data: {
        results: [
          { entity: 'employee', total_count: 1, rows: [{ id: 1, fields: { name: 'Alice' } }] },
          { entity: 'department', total_count: 1, rows: [{ id: 10, fields: { dept_name: 'Engineering' } }] },
        ],
        unauthorized: [],
      },
    });

    const { result } = renderHook(() =>
      useRfSheetData([{ entity: 'employee' }, { entity: 'department' }], 0),
    );

    await waitFor(() => expect(result.current.isLoading).toBe(false));

    expect(result.current.entityData.size).toBe(2);
    expect(result.current.getEntityField('employee', 1, 'name')).toBe('Alice');
    expect(result.current.getEntityField('department', 10, 'dept_name')).toBe('Engineering');
  });

  // ── Refresh ────────────────────────────────────────────────────────────

  it('refresh() triggers a new fetch', async () => {
    vi.mocked(bulkRead).mockResolvedValue({
      data: { results: [], unauthorized: [] },
    });

    const { result } = renderHook(() =>
      useRfSheetData([{ entity: 'employee' }], 0),
    );

    await waitFor(() => expect(result.current.isLoading).toBe(false));

    expect(bulkRead).toHaveBeenCalledTimes(1);

    // Trigger manual refresh
    result.current.refresh();

    await waitFor(() => expect(bulkRead).toHaveBeenCalledTimes(2));
  });

  // ── Field Filtering ────────────────────────────────────────────────────

  it('passes fields filter to bulkRead when sources include fields', async () => {
    vi.mocked(bulkRead).mockResolvedValue({
      data: {
        results: [
          { entity: 'employee', total_count: 2, rows: [
            { id: 1, fields: { name: 'Alice' } },
            { id: 2, fields: { name: 'Bob' } },
          ]},
        ],
        unauthorized: [],
      },
    });

    const { result } = renderHook(() =>
      useRfSheetData([{ entity: 'employee', fields: ['name'] }], 0),
    );

    await waitFor(() => expect(result.current.isLoading).toBe(false));

    expect(bulkRead).toHaveBeenCalledWith([{ entity: 'employee', fields: ['name'] }]);
  });

  it('tracks fetchedFields per entity', async () => {
    vi.mocked(bulkRead).mockResolvedValue({
      data: {
        results: [
          { entity: 'employee', total_count: 2, rows: [
            { id: 1, fields: { name: 'Alice', salary: 60000 } },
            { id: 2, fields: { name: 'Bob', salary: 45000 } },
          ]},
        ],
        unauthorized: [],
      },
    });

    const { result } = renderHook(() =>
      useRfSheetData([{ entity: 'employee' }], 0),
    );

    await waitFor(() => expect(result.current.isLoading).toBe(false));

    const fetched = result.current.fetchedFields.get('employee');
    expect(fetched).toBeDefined();
    expect(fetched!.has('name')).toBe(true);
    expect(fetched!.has('salary')).toBe(true);
  });

  // ── Title Injection for RF.TITLE ──────────────────────────────────────

  it('injects title from root title.rendered into fields so RF.TITLE can access it', async () => {
    vi.mocked(bulkRead).mockResolvedValue({
      data: {
        results: [
          { entity: 'objective', total_count: 1, rows: [
            { id: 1, fields: { status: 'active' }, title: { rendered: 'Grow Revenue' } } as unknown as { id: number; fields: Record<string, unknown> },
          ]},
        ],
        unauthorized: [],
      },
    });

    const { result } = renderHook(() =>
      useRfSheetData([{ entity: 'objective' }], 0),
    );

    await waitFor(() => expect(result.current.isLoading).toBe(false));

    // RF.TITLE reads row.fields['title'] — it must be injected from the root attribute
    expect(result.current.getEntityField('objective', 1, 'title')).toBe('Grow Revenue');
    // The original field should still be accessible
    expect(result.current.getEntityField('objective', 1, 'status')).toBe('active');
  });

  it('injects title from root plain string title into fields', async () => {
    vi.mocked(bulkRead).mockResolvedValue({
      data: {
        results: [
          { entity: 'note', total_count: 1, rows: [
            { id: 7, fields: {}, title: 'Plain Title' } as unknown as { id: number; fields: Record<string, unknown> },
          ]},
        ],
        unauthorized: [],
      },
    });

    const { result } = renderHook(() =>
      useRfSheetData([{ entity: 'note' }], 0),
    );

    await waitFor(() => expect(result.current.isLoading).toBe(false));

    expect(result.current.getEntityField('note', 7, 'title')).toBe('Plain Title');
  });

  it('title injected by backend field-filter appears in fields map', async () => {
    // Simulates BulkRead with field filtering active: backend injects title into fields
    vi.mocked(bulkRead).mockResolvedValue({
      data: {
        results: [
          { entity: 'objective', total_count: 1, rows: [
            { id: 3, fields: { title: 'Injected Title', status: 'done' } },
          ]},
        ],
        unauthorized: [],
      },
    });

    const { result } = renderHook(() =>
      useRfSheetData([{ entity: 'objective', fields: ['title', 'status'] }], 0),
    );

    await waitFor(() => expect(result.current.isLoading).toBe(false));

    expect(result.current.getEntityField('objective', 3, 'title')).toBe('Injected Title');
  });

  it('refetches when fields in sources change', async () => {
    let sourceFields: string[] | undefined = ['name'];
    vi.mocked(bulkRead).mockResolvedValue({
      data: {
        results: [
          { entity: 'employee', total_count: 1, rows: [
            { id: 1, fields: { name: 'Alice' } },
          ]},
        ],
        unauthorized: [],
      },
    });

    const { result, rerender } = renderHook(
      ({ fields }) => useRfSheetData([{ entity: 'employee', fields }], 0),
      { initialProps: { fields: sourceFields } },
    );

    await waitFor(() => expect(result.current.isLoading).toBe(false));
    expect(bulkRead).toHaveBeenCalledTimes(1);

    // Change fields to include salary
    sourceFields = ['name', 'salary'];
    vi.mocked(bulkRead).mockResolvedValue({
      data: {
        results: [
          { entity: 'employee', total_count: 1, rows: [
            { id: 1, fields: { name: 'Alice', salary: 60000 } },
          ]},
        ],
        unauthorized: [],
      },
    });

    rerender({ fields: sourceFields });

    await waitFor(() => expect(bulkRead).toHaveBeenCalledTimes(2));
    expect(bulkRead).toHaveBeenLastCalledWith([{ entity: 'employee', fields: ['name', 'salary'] }]);
  });
});
