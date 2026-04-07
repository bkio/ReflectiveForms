import * as XLSX from 'xlsx';

/**
 * Exports the current Univer workbook to an .xlsx file.
 * Uses the Facade API (getRange/getValues) to read **computed** cell values,
 * which includes spill-array results from RF formulas. The raw snapshot only
 * stores the anchor cell of a spill array, so reading from it would miss
 * the dynamically expanded values.
 */
/* eslint-disable @typescript-eslint/no-explicit-any */
export function exportWorkbookToXlsx(univerAPI: any, fileName: string): void {
  const workbook = univerAPI.getActiveWorkbook();
  if (!workbook) return;

  const snapshot = workbook.save();
  const wb = XLSX.utils.book_new();

  const sheets = snapshot.sheets ?? {};
  const sheetOrder = snapshot.sheetOrder ?? Object.keys(sheets);

  for (const sheetId of sheetOrder) {
    const sheetData = sheets[sheetId];
    if (!sheetData) continue;

    const sheetName = sheetData.name ?? 'Sheet';

    // Get the Facade worksheet for this sheet by looking it up
    let facadeSheet: any = null;
    try {
      facadeSheet = workbook.getSheetBySheetId(sheetId);
    } catch { /* fallback below */ }

    if (!facadeSheet) {
      // Fallback: try active sheet if there's only one
      try {
        facadeSheet = workbook.getActiveSheet();
      } catch { /* skip this sheet */ continue; }
    }
    if (!facadeSheet) continue;

    const maxRows = facadeSheet.getMaxRows();
    const maxCols = facadeSheet.getMaxColumns();

    // Read the full grid of computed values via the Facade API
    let values: any[][] = [];
    try {
      const range = facadeSheet.getRange(0, 0, maxRows, maxCols);
      values = range.getValues() ?? [];
    } catch {
      // Fallback: try snapshot-based export
      values = [];
    }

    if (values.length === 0) {
      // Fallback to snapshot if Facade API didn't work
      const cellData = sheetData.cellData ?? {};
      let fallbackMaxRow = 0;
      let fallbackMaxCol = 0;
      for (const rowIdx of Object.keys(cellData)) {
        const rn = Number(rowIdx);
        if (rn > fallbackMaxRow) fallbackMaxRow = rn;
        const rowCells = cellData[rowIdx];
        for (const colIdx of Object.keys(rowCells)) {
          const cn = Number(colIdx);
          if (cn > fallbackMaxCol) fallbackMaxCol = cn;
        }
      }
      for (let r = 0; r <= fallbackMaxRow; r++) {
        const row: any[] = [];
        for (let c = 0; c <= fallbackMaxCol; c++) {
          const cell = cellData[r]?.[c];
          row.push(cell?.v ?? '');
        }
        values.push(row);
      }
    }

    // Trim trailing empty rows and columns
    let lastRow = -1;
    let lastCol = -1;
    for (let r = 0; r < values.length; r++) {
      for (let c = 0; c < (values[r]?.length ?? 0); c++) {
        const v = values[r][c];
        if (v !== null && v !== undefined && v !== '') {
          if (r > lastRow) lastRow = r;
          if (c > lastCol) lastCol = c;
        }
      }
    }

    const rows: any[][] = [];
    for (let r = 0; r <= lastRow; r++) {
      const row: any[] = [];
      for (let c = 0; c <= lastCol; c++) {
        let value = values[r]?.[c] ?? '';

        // N/A sentinel → export as literal "N/A"
        if (typeof value === 'string' && (value === '#NO_ACCESS' || value === 'N/A')) {
          value = 'N/A';
        }

        row.push(value);
      }
      rows.push(row);
    }

    const ws = XLSX.utils.aoa_to_sheet(rows.length > 0 ? rows : [['']]);

    // Restore non-RF formulas so Excel can evaluate them natively.
    // RF formulas (RF.*) stay as computed values since Excel doesn't
    // understand them.
    const cellData = sheetData.cellData ?? {};
    for (const rowIdx of Object.keys(cellData)) {
      const r = Number(rowIdx);
      if (r > lastRow) continue;
      const rowCells = cellData[rowIdx];
      for (const colIdx of Object.keys(rowCells)) {
        const c = Number(colIdx);
        if (c > lastCol) continue;
        const formula: string | undefined = rowCells[colIdx]?.f;
        if (!formula) continue;
        // Skip RF custom formulas — they have no Excel equivalent
        if (formula.toUpperCase().includes('RF.')) continue;
        const cellRef = XLSX.utils.encode_cell({ r, c });
        const existing = ws[cellRef];
        // Strip leading '=' — xlsx expects the bare formula
        const bare = formula.startsWith('=') ? formula.slice(1) : formula;
        ws[cellRef] = { ...existing, f: bare };
      }
    }

    XLSX.utils.book_append_sheet(wb, ws, sheetName.substring(0, 31));
  }

  // Trigger download
  XLSX.writeFile(wb, fileName.endsWith('.xlsx') ? fileName : `${fileName}.xlsx`);
}
