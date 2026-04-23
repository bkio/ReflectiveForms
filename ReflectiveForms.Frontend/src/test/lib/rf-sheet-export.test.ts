import { describe, it, expect, vi, beforeEach } from 'vitest';
import { exportWorkbookToXlsx } from '../../lib/rf-sheet-export';

// Mock xlsx module
vi.mock('xlsx', () => {
  const sheets: Array<{ name: string; ws: Record<string, unknown> }> = [];
  return {
    utils: {
      book_new: vi.fn(() => ({ SheetNames: [], Sheets: {} })),
      aoa_to_sheet: vi.fn((data: unknown[][]) => {
        // Build a cell-addressable worksheet object like the real xlsx library
        const ws: Record<string, unknown> = { __data: data };
        for (let r = 0; r < data.length; r++) {
          for (let c = 0; c < (data[r]?.length ?? 0); c++) {
            const v = data[r][c];
            if (v === null || v === undefined || v === '') continue;
            const col = String.fromCharCode(65 + c); // A-Z (sufficient for tests)
            ws[`${col}${r + 1}`] = { v, t: typeof v === 'number' ? 'n' : 's' };
          }
        }
        return ws;
      }),
      book_append_sheet: vi.fn((_wb: unknown, ws: Record<string, unknown>, name: string) => {
        sheets.push({ name, ws });
      }),
      encode_cell: vi.fn(({ r, c }: { r: number; c: number }) => {
        const col = String.fromCharCode(65 + c);
        return `${col}${r + 1}`;
      }),
    },
    writeFile: vi.fn(),
    __sheets: sheets,
  };
});

import * as XLSX from 'xlsx';

describe('exportWorkbookToXlsx', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    (XLSX as any).__sheets.length = 0;
  });

  function createMockUniverAPI(snapshot: Record<string, unknown> = {}) {
    // Build mock Facade sheets that return getValues() from cellData
    const sheets = (snapshot.sheets ?? {}) as Record<string, Record<string, unknown>>;
    const mockSheets: Record<string, unknown> = {};

    for (const [sheetId, sheetData] of Object.entries(sheets)) {
      const cellData = (sheetData.cellData ?? {}) as Record<string, Record<string, Record<string, unknown>>>;
      let maxRow = 0;
      let maxCol = 0;
      for (const rowIdx of Object.keys(cellData)) {
        const rn = Number(rowIdx);
        if (rn > maxRow) maxRow = rn;
        for (const colIdx of Object.keys(cellData[rowIdx])) {
          const cn = Number(colIdx);
          if (cn > maxCol) maxCol = cn;
        }
      }
      const numRows = maxRow + 1;
      const numCols = maxCol + 1;

      // Build a 2D values array from cellData (simulates Facade getValues)
      const valuesGrid: unknown[][] = [];
      for (let r = 0; r < numRows; r++) {
        const row: unknown[] = [];
        for (let c = 0; c < numCols; c++) {
          const cell = cellData[r]?.[c];
          row.push(cell?.v ?? null);
        }
        valuesGrid.push(row);
      }

      mockSheets[sheetId] = {
        getMaxRows: vi.fn(() => numRows),
        getMaxColumns: vi.fn(() => numCols),
        getRange: vi.fn(() => ({
          getValues: vi.fn(() => valuesGrid),
        })),
      };
    }

    return {
      getActiveWorkbook: vi.fn(() => ({
        save: vi.fn(() => snapshot),
        getSheetBySheetId: vi.fn((id: string) => mockSheets[id] ?? null),
        getActiveSheet: vi.fn(() => {
          const ids = Object.keys(mockSheets);
          return ids.length > 0 ? mockSheets[ids[0]] : null;
        }),
      })),
    };
  }

  it('does nothing when no active workbook', () => {
    const api = { getActiveWorkbook: vi.fn(() => null) };
    exportWorkbookToXlsx(api, 'test');
    expect(XLSX.writeFile).not.toHaveBeenCalled();
  });

  it('creates an xlsx from a simple snapshot', () => {
    const snapshot = {
      sheetOrder: ['sheet1'],
      sheets: {
        sheet1: {
          name: 'Data',
          cellData: {
            0: { 0: { v: 'Name' }, 1: { v: 'Age' } },
            1: { 0: { v: 'Alice' }, 1: { v: 30 } },
            2: { 0: { v: 'Bob' }, 1: { v: 25 } },
          },
        },
      },
    };

    const api = createMockUniverAPI(snapshot);
    exportWorkbookToXlsx(api, 'my-sheet');

    expect(XLSX.writeFile).toHaveBeenCalledWith(
      expect.any(Object),
      'my-sheet.xlsx',
    );
    expect(XLSX.utils.aoa_to_sheet).toHaveBeenCalledWith([
      ['Name', 'Age'],
      ['Alice', 30],
      ['Bob', 25],
    ]);
  });

  it('converts #NO_ACCESS values to N/A', () => {
    const snapshot = {
      sheetOrder: ['s1'],
      sheets: {
        s1: {
          name: 'Sheet1',
          cellData: {
            0: { 0: { v: '#NO_ACCESS' } },
          },
        },
      },
    };

    const api = createMockUniverAPI(snapshot);
    exportWorkbookToXlsx(api, 'test');

    expect(XLSX.utils.aoa_to_sheet).toHaveBeenCalledWith([['N/A']]);
  });

  it('converts N/A sentinel values to N/A in export', () => {
    const snapshot = {
      sheetOrder: ['s1'],
      sheets: {
        s1: {
          name: 'Sheet1',
          cellData: {
            0: { 0: { v: 'N/A' } },
          },
        },
      },
    };

    const api = createMockUniverAPI(snapshot);
    exportWorkbookToXlsx(api, 'test');

    expect(XLSX.utils.aoa_to_sheet).toHaveBeenCalledWith([['N/A']]);
  });

  it('uses computed value for formula cells', () => {
    const snapshot = {
      sheetOrder: ['s1'],
      sheets: {
        s1: {
          name: 'Sheet1',
          cellData: {
            0: { 0: { f: '=RF.LIST("employee","name")', v: 'Alice' } },
          },
        },
      },
    };

    const api = createMockUniverAPI(snapshot);
    exportWorkbookToXlsx(api, 'test');

    expect(XLSX.utils.aoa_to_sheet).toHaveBeenCalledWith([['Alice']]);
  });

  it('fills empty cells with empty string', () => {
    const snapshot = {
      sheetOrder: ['s1'],
      sheets: {
        s1: {
          name: 'Sheet1',
          cellData: {
            0: { 0: { v: 'A' }, 2: { v: 'C' } },
          },
        },
      },
    };

    const api = createMockUniverAPI(snapshot);
    exportWorkbookToXlsx(api, 'test');

    expect(XLSX.utils.aoa_to_sheet).toHaveBeenCalledWith([['A', '', 'C']]);
  });

  it('appends .xlsx extension if not present', () => {
    const snapshot = {
      sheetOrder: ['s1'],
      sheets: { s1: { name: 'S', cellData: {} } },
    };

    const api = createMockUniverAPI(snapshot);
    exportWorkbookToXlsx(api, 'report');
    expect(XLSX.writeFile).toHaveBeenCalledWith(expect.any(Object), 'report.xlsx');
  });

  it('does not double .xlsx extension', () => {
    const snapshot = {
      sheetOrder: ['s1'],
      sheets: { s1: { name: 'S', cellData: {} } },
    };

    const api = createMockUniverAPI(snapshot);
    exportWorkbookToXlsx(api, 'report.xlsx');
    expect(XLSX.writeFile).toHaveBeenCalledWith(expect.any(Object), 'report.xlsx');
  });

  it('handles empty snapshot gracefully', () => {
    const api = createMockUniverAPI({});
    exportWorkbookToXlsx(api, 'empty');
    // Should not throw, writeFile should still be called (empty workbook)
    expect(XLSX.writeFile).toHaveBeenCalled();
  });

  it('truncates sheet names to 31 characters', () => {
    const longName = 'ThisIsAVeryLongSheetNameThatExceeds31Characters';
    const snapshot = {
      sheetOrder: ['s1'],
      sheets: {
        s1: { name: longName, cellData: { 0: { 0: { v: 'data' } } } },
      },
    };

    const api = createMockUniverAPI(snapshot);
    exportWorkbookToXlsx(api, 'test');

    expect(XLSX.utils.book_append_sheet).toHaveBeenCalledWith(
      expect.any(Object),
      expect.any(Object),
      longName.substring(0, 31),
    );
  });

  it('preserves standard formulas in exported worksheet', () => {
    const snapshot = {
      sheetOrder: ['s1'],
      sheets: {
        s1: {
          name: 'Sheet1',
          cellData: {
            0: { 0: { v: 10 }, 1: { v: 20 }, 2: { f: '=SUM(A1,B1)', v: 30 } },
          },
        },
      },
    };

    const api = createMockUniverAPI(snapshot);
    exportWorkbookToXlsx(api, 'test');

    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const sheets = (XLSX as any).__sheets;
    expect(sheets).toHaveLength(1);
    // Cell C1 should have the formula (without leading =)
    expect(sheets[0].ws['C1']).toEqual(expect.objectContaining({ f: 'SUM(A1,B1)' }));
  });

  it('does NOT write RF formulas — exports computed value only', () => {
    const snapshot = {
      sheetOrder: ['s1'],
      sheets: {
        s1: {
          name: 'Sheet1',
          cellData: {
            0: { 0: { f: '=RF.LIST("employee","name")', v: 'Alice' } },
          },
        },
      },
    };

    const api = createMockUniverAPI(snapshot);
    exportWorkbookToXlsx(api, 'test');

    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const sheets = (XLSX as any).__sheets;
    expect(sheets).toHaveLength(1);
    // Cell A1 should have the computed value, NOT a formula
    expect(sheets[0].ws['A1']).toEqual(
      expect.objectContaining({ v: 'Alice' }),
    );
    expect(sheets[0].ws['A1']).not.toHaveProperty('f');
  });
});
