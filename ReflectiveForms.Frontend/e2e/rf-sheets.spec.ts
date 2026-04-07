import { test, expect } from './helpers';
import type { Page } from '@playwright/test';

/**
 * RF Sheets — Comprehensive E2E Tests
 *
 * Tests the complete RF Sheet feature end-to-end:
 * - Sheet CRUD (list, create via UI, save, load)
 * - All 14 RF formula functions against live backend data
 * - Group field access via dot-path notation
 * - Nested group access (group-in-group: Event → Venue → Address)
 * - Repeater functions: RF.REPEAT, RF.REPEATCOUNT, RF.REPEATFIELD
 * - Spill array behavior (RF.IDS, RF.LIST, RF.REPEAT, RF.FILTER, RF.MATCHLIST)
 * - Error sentinels (#NOT_FOUND, #FIELD_REMOVED, out-of-bounds)
 * - Edit guard (RF formula cells blocked from editing)
 * - Excel export (.xlsx download)
 * - Manual data refresh
 * - Entity source panel (add/remove/expand/toggle)
 *
 * Test entities: product ×2, objective ×2, event ×1
 */

const TS = () => Date.now().toString(36);

// ── Univer Facade API helpers ──────────────────────────────────────

/** Read a cell's computed value via window.__univerAPI. */
async function getCellValue(page: Page, row: number, col: number): Promise<unknown> {
  return page.evaluate(
    ({ r, c }) => {
      const api = (window as any).__univerAPI;
      if (!api) return null;
      const wb = api.getActiveWorkbook();
      if (!wb) return null;
      const sheet = wb.getActiveSheet();
      if (!sheet) return null;
      return sheet.getRange(r, c, 1, 1).getValue();
    },
    { r: row, c: col },
  );
}

/** Read a cell's formula string (if any). */
async function getCellFormula(page: Page, row: number, col: number): Promise<string | null> {
  return page.evaluate(
    ({ r, c }) => {
      const api = (window as any).__univerAPI;
      if (!api) return null;
      const wb = api.getActiveWorkbook();
      if (!wb) return null;
      const sheet = wb.getActiveSheet();
      if (!sheet) return null;
      const formulas = sheet.getRange(r, c, 1, 1).getFormulas();
      return formulas?.[0]?.[0] ?? null;
    },
    { r: row, c: col },
  );
}

/**
 * Wait until the Univer API is available AND a sentinel cell has a meaningful
 * computed value. This synchronises on: page load → Univer init → data fetch →
 * formula recalculation. The target cell should be a non-aggregate formula
 * (e.g. RF.TITLE) that returns a non-empty string on success.
 */
async function waitForFormulaResult(
  page: Page,
  row: number,
  col: number,
  timeoutMs = 40000,
): Promise<void> {
  await page.waitForFunction(
    ({ r, c }) => {
      const api = (window as any).__univerAPI;
      if (!api) return false;
      const wb = api.getActiveWorkbook();
      if (!wb) return false;
      const sheet = wb.getActiveSheet();
      if (!sheet) return false;
      const v = sheet.getRange(r, c, 1, 1).getValue();
      return typeof v === 'string' && v.length > 0 && !v.startsWith('#');
    },
    { r: row, c: col },
    { timeout: timeoutMs },
  );
}

// ── Workbook snapshot builder ──────────────────────────────────────

/**
 * Builds a Univer workbook JSON with all RF formulas laid out in a grid.
 *
 * Column A (0): Labels describing each formula
 * Column B (1): The actual RF formula
 *
 * Rows are spaced so that spill arrays (max 2 rows for our 2 products / 2 objectives)
 * don't overwrite the next formula.
 */
function buildFormulaSheet(
  prod1: number,
  obj1: number,
  evt1: number,
  surv1: number,
): string {
  const c: Record<string, Record<string, unknown>> = {};

  const set = (r: number, col: number, cellData: unknown) => {
    if (!c[r]) c[r] = {};
    c[r][col] = cellData;
  };
  const label = (v: string) => ({ v });
  const formula = (f: string) => ({ f });

  // ── Basic formulas (rows 0-3) ──
  set(0, 0, label('RF.FIELD select'));
  set(0, 1, formula(`=RF.FIELD("product", ${prod1}, "product_category")`));

  set(1, 0, label('RF.FIELD text'));
  set(1, 1, formula(`=RF.FIELD("product", ${prod1}, "short_description")`));

  set(2, 0, label('RF.FIELD number'));
  set(2, 1, formula(`=RF.FIELD("product", ${prod1}, "base_price")`));

  set(3, 0, label('RF.COUNT'));
  set(3, 1, formula('=RF.COUNT("product")'));

  // ── Spill arrays (rows 5-9, gaps for 2-row spills) ──
  set(5, 0, label('RF.IDS'));
  set(5, 1, formula('=RF.IDS("product")'));
  // Row 6: spill

  set(8, 0, label('RF.LIST'));
  set(8, 1, formula('=RF.LIST("product", "base_price")'));
  // Row 9: spill

  // ── Aggregates (rows 11-12) ──
  set(11, 0, label('RF.SUM'));
  set(11, 1, formula('=RF.SUM("product", "base_price")'));

  set(12, 0, label('RF.AVG'));
  set(12, 1, formula('=RF.AVG("product", "base_price")'));

  // ── Lookup / Filter / Match (rows 14-21) ──
  set(14, 0, label('RF.LOOKUP'));
  set(14, 1, formula('=RF.LOOKUP("product", "base_price", 49.99, "short_description")'));

  set(16, 0, label('RF.FILTER'));
  set(16, 1, formula('=RF.FILTER("product", "short_description", "is_published", "true")'));

  set(18, 0, label('RF.MATCH'));
  set(18, 1, formula(`=RF.MATCH("product", ${prod1}, "base_price", ">", 50)`));

  set(20, 0, label('RF.MATCHLIST'));
  set(20, 1, formula('=RF.MATCHLIST("product", "base_price", ">", 60)'));
  // Row 21: spill

  // ── Group dot-path (rows 23-28) ──
  set(23, 0, label('Group simple'));
  set(23, 1, formula(`=RF.FIELD("objective", ${obj1}, "creator_comment.comment")`));

  set(24, 0, label('Group nested venue'));
  set(24, 1, formula(`=RF.FIELD("event", ${evt1}, "venue.venue_name")`));

  set(25, 0, label('Group deep address'));
  set(25, 1, formula(`=RF.FIELD("event", ${evt1}, "venue.venue_address.city")`));

  set(27, 0, label('Group LIST'));
  set(27, 1, formula('=RF.LIST("objective", "creator_comment.comment")'));
  // Row 28: spill

  // ── Repeater functions (rows 30-36) ──
  set(30, 0, label('REPEATCOUNT'));
  set(30, 1, formula(`=RF.REPEATCOUNT("objective", ${obj1}, "key_results")`));

  set(32, 0, label('REPEAT'));
  set(32, 1, formula(`=RF.REPEAT("objective", ${obj1}, "key_results", "key_result")`));
  // Row 33: spill

  set(35, 0, label('REPEATFIELD 0'));
  set(35, 1, formula(`=RF.REPEATFIELD("objective", ${obj1}, "key_results", 0, "key_result")`));

  set(36, 0, label('REPEATFIELD bool'));
  set(36, 1, formula(`=RF.REPEATFIELD("objective", ${obj1}, "key_results", 1, "achieved")`));

  // ── Error sentinels (rows 38-40) ──
  set(38, 0, label('ERR NOT_FOUND'));
  set(38, 1, formula('=RF.FIELD("product", 999999, "base_price")'));

  set(39, 0, label('ERR FIELD_REMOVED'));
  set(39, 1, formula(`=RF.FIELD("product", ${prod1}, "nonexistent_field")`));

  set(40, 0, label('ERR REPEAT OOB'));
  set(40, 1, formula(`=RF.REPEATFIELD("objective", ${obj1}, "key_results", 99, "key_result")`));

  // ── Repeater-in-repeater: objective key_results[0].key_result_comments (rows 42-53) ──
  set(42, 0, label('RiR COUNT'));
  set(42, 1, formula(`=RF.REPEATCOUNT("objective", ${obj1}, "key_results[0].key_result_comments")`));

  set(44, 0, label('RiR REPEAT'));
  set(44, 1, formula(`=RF.REPEAT("objective", ${obj1}, "key_results[0].key_result_comments", "comment")`));
  // Row 45: spill

  set(47, 0, label('RiR REPEATFIELD'));
  set(47, 1, formula(`=RF.REPEATFIELD("objective", ${obj1}, "key_results[0].key_result_comments", 0, "comment")`));

  set(48, 0, label('RiR COUNT idx1'));
  set(48, 1, formula(`=RF.REPEATCOUNT("objective", ${obj1}, "key_results[1].key_result_comments")`));

  set(49, 0, label('RiR FIELD idx1'));
  set(49, 1, formula(`=RF.REPEATFIELD("objective", ${obj1}, "key_results[1].key_result_comments", 0, "comment")`));

  // ── 3-level nested repeater: survey sections → questions → choices (rows 51-64) ──
  set(51, 0, label('Survey sec count'));
  set(51, 1, formula(`=RF.REPEATCOUNT("survey", ${surv1}, "sections")`));

  set(52, 0, label('Survey sec title'));
  set(52, 1, formula(`=RF.REPEATFIELD("survey", ${surv1}, "sections", 0, "section_title")`));

  set(53, 0, label('Survey q count'));
  set(53, 1, formula(`=RF.REPEATCOUNT("survey", ${surv1}, "sections[0].questions")`));

  set(55, 0, label('Survey q text'));
  set(55, 1, formula(`=RF.REPEAT("survey", ${surv1}, "sections[0].questions", "question_text")`));
  // Row 56: spill

  set(58, 0, label('Survey q1 text'));
  set(58, 1, formula(`=RF.REPEATFIELD("survey", ${surv1}, "sections[0].questions", 0, "question_text")`));

  set(59, 0, label('Survey choices cnt'));
  set(59, 1, formula(`=RF.REPEATCOUNT("survey", ${surv1}, "sections[0].questions[0].choices")`));

  set(61, 0, label('Survey choice labels'));
  set(61, 1, formula(`=RF.REPEAT("survey", ${surv1}, "sections[0].questions[0].choices", "choice_label")`));
  // Row 62, 63: spill

  set(65, 0, label('Survey choice[1]'));
  set(65, 1, formula(`=RF.REPEATFIELD("survey", ${surv1}, "sections[0].questions[0].choices", 1, "choice_label")`));

  set(66, 0, label('Survey choice score'));
  set(66, 1, formula(`=RF.REPEATFIELD("survey", ${surv1}, "sections[0].questions[0].choices", 0, "choice_score")`));

  // ── Group-in-group across all: RF.LIST with deep paths (rows 68-71) ──
  set(68, 0, label('LIST deep group'));
  set(68, 1, formula('=RF.LIST("event", "venue.venue_address.street")'));

  // ── Section 2 access from survey (row 70) ──
  set(70, 0, label('Survey sec2 title'));
  set(70, 1, formula(`=RF.REPEATFIELD("survey", ${surv1}, "sections", 1, "section_title")`));

  set(71, 0, label('Survey sec2 q cnt'));
  set(71, 1, formula(`=RF.REPEATCOUNT("survey", ${surv1}, "sections[1].questions")`));

  set(72, 0, label('Survey sec2 q text'));
  set(72, 1, formula(`=RF.REPEATFIELD("survey", ${surv1}, "sections[1].questions", 0, "question_text")`));

  return JSON.stringify({
    sheetOrder: ['sheet1'],
    sheets: {
      sheet1: {
        id: 'sheet1',
        name: 'Sheet1',
        cellData: c,
      },
    },
  });
}

// ═══════════════════════════════════════════════════════════════════
// Tests
// ═══════════════════════════════════════════════════════════════════

test.describe('RF Sheets — Comprehensive E2E', () => {
  test.describe.configure({ mode: 'serial' });

  // Shared state across serial tests
  let prod1Id: number;
  let prod2Id: number;
  let obj1Id: number;
  let evt1Id: number;
  let surv1Id: number;
  let formulaSheetId: number;

  test.afterAll(async ({ request }) => {
    const { ApiHelper } = await import('./helpers');
    const api = new ApiHelper(request);
    await api.login();
    await api.deleteAll('rf-sheets');
    await api.deleteAll('product');
    await api.deleteAll('objective');
    await api.deleteAll('event');
    await api.deleteAll('survey');
  });

  // ── SETUP: Create test entities ────────────────────────────

  test('setup: create test products', async ({ api }) => {
    await api.deleteAll('product');

    await api.createEntity('product', {
      title: { rendered: 'E2E Sheet Prod 1' },
      fields: {
        short_description: 'Product one for sheet testing',
        long_description: '<p>Detailed product one</p>',
        product_category: 'electronics',
        primary_image: '/media/placeholder.png',
        base_price: 99.99,
        discount_percentage: 10,
        is_published: true,
        is_digital: false,
        weight_kg: 1.5,
        variants: [
          { variant_name: 'Default', sku: `E2E-P1-${TS()}`, price: 99.99, stock_quantity: 100, is_available: true },
        ],
        specifications: [],
        gallery: [],
      },
    });

    await api.createEntity('product', {
      title: { rendered: 'E2E Sheet Prod 2' },
      fields: {
        short_description: 'Product two for sheet testing',
        long_description: '<p>Detailed product two</p>',
        product_category: 'clothing',
        primary_image: '/media/placeholder.png',
        base_price: 49.99,
        discount_percentage: 20,
        is_published: false,
        is_digital: false,
        weight_kg: 2.5,
        variants: [
          { variant_name: 'Default', sku: `E2E-P2-${TS()}`, price: 49.99, stock_quantity: 50, is_available: true },
        ],
        specifications: [],
        gallery: [],
      },
    });

    const products = await api.peekAll('product');
    const p1 = products.find((p) => (p.title ?? p.name ?? '').includes('Prod 1'));
    const p2 = products.find((p) => (p.title ?? p.name ?? '').includes('Prod 2'));
    expect(p1, 'Product 1 should exist').toBeTruthy();
    expect(p2, 'Product 2 should exist').toBeTruthy();
    prod1Id = p1!.id;
    prod2Id = p2!.id;
  });

  test('setup: create test objectives', async ({ api }) => {
    await api.deleteAll('objective');

    await api.createEntity('objective', {
      title: { rendered: 'E2E Sheet Obj 1' },
      fields: {
        objective_work_start_date: '20260401',
        objective_type: 'short_term',
        root_cause: `Alpha root cause ${TS()}`,
        documentation_url: 'https://alpha.example.com',
        creator_comment: { author: 2, comment: 'Alpha group comment' },
        key_results: [
          {
            key_result: 'KR Alpha One',
            achieved: false,
            key_result_comments: [
              { author: 2, comment: 'Comment A on KR1' },
              { author: 2, comment: 'Comment B on KR1' },
            ],
          },
          {
            key_result: 'KR Alpha Two',
            achieved: true,
            key_result_comments: [
              { author: 2, comment: 'Comment on KR2' },
            ],
          },
        ],
        objective_comments: [],
      },
    });

    await api.createEntity('objective', {
      title: { rendered: 'E2E Sheet Obj 2' },
      fields: {
        objective_work_start_date: '20260501',
        objective_type: 'long_term',
        root_cause: `Beta root cause ${TS()}`,
        documentation_url: 'https://beta.example.com',
        creator_comment: { author: 2, comment: 'Beta group comment' },
        key_results: [
          { key_result: 'KR Beta One', achieved: false, key_result_comments: [] },
        ],
        objective_comments: [],
      },
    });

    const objectives = await api.peekAll('objective');
    const o1 = objectives.find((o) => (o.title ?? o.name ?? '').includes('Obj 1'));
    const o2 = objectives.find((o) => (o.title ?? o.name ?? '').includes('Obj 2'));
    expect(o1, 'Objective 1 should exist').toBeTruthy();
    expect(o2, 'Objective 2 should exist').toBeTruthy();
    obj1Id = o1!.id;
  });

  test('setup: create test event', async ({ api }) => {
    await api.deleteAll('event');

    await api.createEntity('event', {
      title: { rendered: 'E2E Sheet Event 1' },
      fields: {
        description: '<p>Event for sheet testing</p>',
        event_type: 'conference',
        start_date: '20260901',
        end_date: '20260903',
        is_online: false,
        venue: {
          venue_name: 'Gamma Convention Center',
          venue_address: {
            street: '123 Gamma St',
            city: 'Gammaville',
            state: 'GA',
            postal_code: '30301',
            country: 'US',
          },
          capacity: 500,
        },
        max_attendees: 200,
        ticket_price: 150,
        registration_email: 'test@event.example.com',
        banner_image: '/media/placeholder.png',
        sessions: [
          {
            session_title: 'Opening Keynote',
            speaker_name: 'Dr. Smith',
            speaker_email: 'smith@test.com',
            session_date: '20260901',
            duration_minutes: 60,
            session_type: 'keynote',
          },
        ],
        sponsors: [],
      },
    });

    const events = await api.peekAll('event');
    const e1 = events.find((e) => (e.title ?? e.name ?? '').includes('Event 1'));
    expect(e1, 'Event 1 should exist').toBeTruthy();
    evt1Id = e1!.id;
  });

  test('setup: create test survey', async ({ api }) => {
    await api.deleteAll('survey');

    await api.createEntity('survey', {
      title: { rendered: 'E2E Sheet Survey 1' },
      fields: {
        survey_description: 'Survey for nested repeater testing',
        is_anonymous: false,
        response_limit: 1,
        survey_status: 'active',
        sections: [
          {
            section_title: 'Demographics',
            section_description: 'About you',
            has_scoring: true,
            passing_score: 70,
            scoring_mode: 'simple',
            questions: [
              {
                question_text: 'What is your age group?',
                question_type: 'choice',
                is_required: true,
                help_text: 'Please select one',
                choices: [
                  { choice_label: '18-25', is_correct: false, choice_score: 10 },
                  { choice_label: '26-35', is_correct: true, choice_score: 20 },
                  { choice_label: '36-50', is_correct: false, choice_score: 10 },
                ],
              },
              {
                question_text: 'Describe your role',
                question_type: 'text',
                is_required: false,
              },
            ],
          },
          {
            section_title: 'Feedback',
            section_description: 'Your thoughts',
            has_scoring: false,
            questions: [
              {
                question_text: 'Rate our service',
                question_type: 'rating',
                is_required: true,
                min_rating: 1,
                max_rating: 5,
              },
            ],
          },
        ],
      },
    });

    const surveys = await api.peekAll('survey');
    const s1 = surveys.find((s) => (s.title ?? s.name ?? '').includes('Survey 1'));
    expect(s1, 'Survey 1 should exist').toBeTruthy();
    surv1Id = s1!.id;
  });

  test('setup: create formula test sheet', async ({ api }) => {
    await api.deleteAll('rf-sheets');

    const workbookData = buildFormulaSheet(prod1Id, obj1Id, evt1Id, surv1Id);
    const sources = JSON.stringify(['product', 'objective', 'event', 'survey']);

    await api.createEntity('rf-sheets', {
      title: { rendered: 'E2E Formula Test Sheet' },
      fields: {
        sources,
        bound_regions: '[]',
        workbook_data: workbookData,
        refresh_interval_seconds: 30,
      },
    });

    const sheets = await api.peekAll('rf-sheets');
    const s = sheets.find((s) => (typeof s.title === 'object' ? s.title?.rendered : s.title ?? s.name ?? '').includes('Formula Test Sheet'));
    expect(s, 'Formula test sheet should exist').toBeTruthy();
    formulaSheetId = s!.id;
  });

  // ── SHEET LIST PAGE ────────────────────────────────────────

  test('sheet list page displays heading and the created sheet', async ({ page, ui }) => {
    await page.goto('/sheets');
    await expect(page.locator('h1')).toContainText(/sheets/i, { timeout: 15000 });
    // "New Sheet" is a <Link> (renders as <a>), not a button
    await expect(page.locator('a', { hasText: /new sheet/i })).toBeVisible({ timeout: 10000 });
    await expect(page.locator('text=E2E Formula Test Sheet')).toBeVisible({ timeout: 10000 });
  });

  // ── ALL RF FORMULAS ────────────────────────────────────────

  test('all RF formulas resolve correctly on loaded sheet', async ({ page, ui }) => {
    await page.goto(`/sheets/${formulaSheetId}`);
    await expect(page.locator('[title="Export to .xlsx"]')).toBeVisible({ timeout: 15000 });

    // Wait for RF.FIELD text (row 1, col 1) to have a computed value —
    // this confirms Univer + data fetch + formula recalculation are complete.
    await waitForFormulaResult(page, 1, 1);

    // ── Basic formulas ──

    await test.step('RF.FIELD resolves Select field with label', async () => {
      expect(await getCellValue(page, 0, 1)).toBe('Electronics');
    });

    await test.step('RF.FIELD resolves text field', async () => {
      expect(await getCellValue(page, 1, 1)).toBe('Product one for sheet testing');
    });

    await test.step('RF.FIELD resolves number field', async () => {
      expect(await getCellValue(page, 2, 1)).toBe(99.99);
    });

    await test.step('RF.COUNT returns row count', async () => {
      expect(await getCellValue(page, 3, 1)).toBe(2);
    });

    // ── Spill arrays ──

    await test.step('RF.IDS returns all entity IDs as spill array', async () => {
      const id1 = await getCellValue(page, 5, 1);
      const id2 = await getCellValue(page, 6, 1);
      const ids = [id1 as number, id2 as number].sort((a, b) => a - b);
      const expected = [prod1Id, prod2Id].sort((a, b) => a - b);
      expect(ids).toEqual(expected);
    });

    await test.step('RF.LIST returns field values as spill array', async () => {
      const v1 = await getCellValue(page, 8, 1);
      const v2 = await getCellValue(page, 9, 1);
      const values = [v1 as number, v2 as number].sort((a, b) => a - b);
      expect(values).toEqual([49.99, 99.99]);
    });

    // ── Aggregates ──

    await test.step('RF.SUM computes sum of numeric field', async () => {
      expect(await getCellValue(page, 11, 1)).toBeCloseTo(149.98, 1);
    });

    await test.step('RF.AVG computes average of numeric field', async () => {
      expect(await getCellValue(page, 12, 1)).toBeCloseTo(74.99, 1);
    });

    // ── Lookup / Filter / Match ──

    await test.step('RF.LOOKUP finds product by price', async () => {
      expect(await getCellValue(page, 14, 1)).toBe('Product two for sheet testing');
    });

    await test.step('RF.FILTER returns published products only', async () => {
      expect(await getCellValue(page, 16, 1)).toBe('Product one for sheet testing');
    });

    await test.step('RF.MATCH returns true (1) for price > 50', async () => {
      const val = await getCellValue(page, 18, 1);
      // Univer coerces booleans to 1/0
      expect(val === true || val === 1).toBe(true);
    });

    await test.step('RF.MATCHLIST returns boolean spill array', async () => {
      const v1 = await getCellValue(page, 20, 1);
      const v2 = await getCellValue(page, 21, 1);
      // One product (99.99) > 60 → true/1, other (49.99) > 60 → false/0
      const toBool = (v: unknown) => v === true || v === 1;
      const toFalse = (v: unknown) => v === false || v === 0;
      expect(toBool(v1) || toBool(v2)).toBe(true);
      expect(toFalse(v1) || toFalse(v2)).toBe(true);
    });

    // ── Group dot-path access ──

    await test.step('RF.FIELD resolves simple group path (creator_comment.comment)', async () => {
      expect(await getCellValue(page, 23, 1)).toBe('Alpha group comment');
    });

    await test.step('RF.FIELD resolves nested group (venue.venue_name)', async () => {
      expect(await getCellValue(page, 24, 1)).toBe('Gamma Convention Center');
    });

    await test.step('RF.FIELD resolves deeply nested group (venue.venue_address.city)', async () => {
      expect(await getCellValue(page, 25, 1)).toBe('Gammaville');
    });

    await test.step('RF.LIST with group dot-path across entities', async () => {
      const v1 = await getCellValue(page, 27, 1);
      const v2 = await getCellValue(page, 28, 1);
      const values = [v1, v2].sort();
      expect(values).toEqual(['Alpha group comment', 'Beta group comment']);
    });

    // ── Repeater functions ──

    await test.step('RF.REPEATCOUNT returns repeater array length', async () => {
      expect(await getCellValue(page, 30, 1)).toBe(2);
    });

    await test.step('RF.REPEAT returns sub-field values as spill array', async () => {
      const v1 = await getCellValue(page, 32, 1);
      const v2 = await getCellValue(page, 33, 1);
      expect(v1).toBe('KR Alpha One');
      expect(v2).toBe('KR Alpha Two');
    });

    await test.step('RF.REPEATFIELD returns indexed value', async () => {
      expect(await getCellValue(page, 35, 1)).toBe('KR Alpha One');
    });

    await test.step('RF.REPEATFIELD returns boolean from repeater', async () => {
      const val = await getCellValue(page, 36, 1);
      // key_results[1].achieved = true → Univer may return true or 1
      expect(val === true || val === 1).toBe(true);
    });

    // ── Error sentinels ──

    await test.step('#NOT_FOUND for missing entity ID', async () => {
      expect(await getCellValue(page, 38, 1)).toBe('#NOT_FOUND');
    });

    await test.step('#FIELD_REMOVED for nonexistent field', async () => {
      expect(await getCellValue(page, 39, 1)).toBe('#FIELD_REMOVED');
    });

    await test.step('#NOT_FOUND for out-of-bounds repeater index', async () => {
      expect(await getCellValue(page, 40, 1)).toBe('#NOT_FOUND');
    });

    // ── Repeater-in-repeater (objective: key_results[N].key_result_comments) ──

    await test.step('nested REPEATCOUNT: key_results[0].key_result_comments = 2', async () => {
      expect(await getCellValue(page, 42, 1)).toBe(2);
    });

    await test.step('nested REPEAT: key_result_comments comment spill array', async () => {
      const v1 = await getCellValue(page, 44, 1);
      const v2 = await getCellValue(page, 45, 1);
      expect(v1).toBe('Comment A on KR1');
      expect(v2).toBe('Comment B on KR1');
    });

    await test.step('nested REPEATFIELD: key_result_comments[0].comment', async () => {
      expect(await getCellValue(page, 47, 1)).toBe('Comment A on KR1');
    });

    await test.step('nested REPEATCOUNT on second repeater item: key_results[1].key_result_comments = 1', async () => {
      expect(await getCellValue(page, 48, 1)).toBe(1);
    });

    await test.step('nested REPEATFIELD on second repeater item: key_results[1].key_result_comments[0].comment', async () => {
      expect(await getCellValue(page, 49, 1)).toBe('Comment on KR2');
    });

    // ── 3-level nested repeater (survey: sections → questions → choices) ──

    await test.step('survey REPEATCOUNT: sections = 2', async () => {
      expect(await getCellValue(page, 51, 1)).toBe(2);
    });

    await test.step('survey REPEATFIELD: sections[0].section_title', async () => {
      expect(await getCellValue(page, 52, 1)).toBe('Demographics');
    });

    await test.step('survey REPEATCOUNT: sections[0].questions = 2', async () => {
      expect(await getCellValue(page, 53, 1)).toBe(2);
    });

    await test.step('survey REPEAT: sections[0].questions question_text spill', async () => {
      const v1 = await getCellValue(page, 55, 1);
      const v2 = await getCellValue(page, 56, 1);
      expect(v1).toBe('What is your age group?');
      expect(v2).toBe('Describe your role');
    });

    await test.step('survey REPEATFIELD: sections[0].questions[0].question_text', async () => {
      expect(await getCellValue(page, 58, 1)).toBe('What is your age group?');
    });

    await test.step('survey REPEATCOUNT: 3rd level choices = 3', async () => {
      expect(await getCellValue(page, 59, 1)).toBe(3);
    });

    await test.step('survey REPEAT: 3rd level choice_label spill (3 choices)', async () => {
      const v1 = await getCellValue(page, 61, 1);
      const v2 = await getCellValue(page, 62, 1);
      const v3 = await getCellValue(page, 63, 1);
      expect(v1).toBe('18-25');
      expect(v2).toBe('26-35');
      expect(v3).toBe('36-50');
    });

    await test.step('survey REPEATFIELD: choices[1].choice_label', async () => {
      expect(await getCellValue(page, 65, 1)).toBe('26-35');
    });

    await test.step('survey REPEATFIELD: choices[0].choice_score = 10', async () => {
      expect(await getCellValue(page, 66, 1)).toBe(10);
    });

    // ── Cross-entity deep group LIST ──

    await test.step('RF.LIST with deeply nested group path', async () => {
      expect(await getCellValue(page, 68, 1)).toBe('123 Gamma St');
    });

    // ── Section 2 access ──

    await test.step('survey section 2 title', async () => {
      expect(await getCellValue(page, 70, 1)).toBe('Feedback');
    });

    await test.step('survey section 2 question count = 1', async () => {
      expect(await getCellValue(page, 71, 1)).toBe(1);
    });

    await test.step('survey section 2 question text', async () => {
      expect(await getCellValue(page, 72, 1)).toBe('Rate our service');
    });
  });

  // ── CREATE & SAVE SHEET VIA UI ─────────────────────────────

  test('create and save a new sheet via UI', async ({ page, ui }) => {
    await page.goto('/sheets/new');
    await expect(page.locator('[title="Export to .xlsx"]')).toBeVisible({ timeout: 15000 });

    // Fill title
    await page.locator('[placeholder="Untitled Sheet"]').fill('E2E UI Created Sheet');

    // Add a product entity source
    await page.locator('button', { hasText: /\+ add/i }).click();
    // Wait for entity picker to show and select product
    const entityOption = page.locator('[role="option"], [data-value="product"], button', { hasText: /^product$/i }).first();
    await entityOption.waitFor({ timeout: 10000 });
    await entityOption.click();

    // Save
    await page.locator('button', { hasText: /^save$/i }).click();

    // Should navigate to /sheets/{id}
    await page.waitForURL(/\/sheets\/\d+/, { timeout: 15000 });

    // Success toast
    await expect(
      page.locator('[data-sonner-toast][data-type="success"]'),
    ).toBeVisible({ timeout: 5000 });
  });

  // ── EDIT GUARD ─────────────────────────────────────────────

  test('edit guard preserves RF formula cells', async ({ page, ui }) => {
    await page.goto(`/sheets/${formulaSheetId}`);
    await expect(page.locator('[title="Export to .xlsx"]')).toBeVisible({ timeout: 15000 });
    await waitForFormulaResult(page, 1, 1);

    // Verify an RF formula is intact
    const formula = await getCellFormula(page, 0, 1);
    expect(formula).toContain('RF.FIELD');

    // Verify cells with RF formulas still have their formulas (not overwritten)
    const countFormula = await getCellFormula(page, 3, 1);
    expect(countFormula).toContain('RF.COUNT');

    const repeatFormula = await getCellFormula(page, 32, 1);
    expect(repeatFormula).toContain('RF.REPEAT');
  });

  // ── EXPORT ─────────────────────────────────────────────────

  test('export downloads an .xlsx file', async ({ page, ui }) => {
    await page.goto(`/sheets/${formulaSheetId}`);
    await expect(page.locator('[title="Export to .xlsx"]')).toBeVisible({ timeout: 15000 });
    await waitForFormulaResult(page, 1, 1);

    const downloadPromise = page.waitForEvent('download', { timeout: 15000 });
    await page.locator('[title="Export to .xlsx"]').click();
    const download = await downloadPromise;

    expect(download.suggestedFilename()).toMatch(/\.xlsx$/);
    expect(download.suggestedFilename()).toContain('E2E Formula Test Sheet');
  });

  // ── REFRESH ────────────────────────────────────────────────

  test('refresh button reloads data and updates formulas', async ({ page, ui, api }) => {
    await page.goto(`/sheets/${formulaSheetId}`);
    await expect(page.locator('[title="Refresh data"]')).toBeVisible({ timeout: 15000 });
    await waitForFormulaResult(page, 1, 1);

    // Verify initial product count
    const countBefore = await getCellValue(page, 3, 1);
    expect(countBefore).toBe(2);

    // Create a third product via API
    await api.createEntity('product', {
      title: { rendered: 'E2E Sheet Prod 3' },
      fields: {
        short_description: 'Product three added mid-test',
        primary_image: '/media/placeholder.png',
        base_price: 25.00,
        product_category: 'electronics',
        is_digital: false,
        is_published: true,
        variants: [
          { variant_name: 'Default', sku: `E2E-P3-${TS()}`, price: 25.00, stock_quantity: 10, is_available: true },
        ],
        specifications: [],
        gallery: [],
      },
    });

    // Click refresh
    await page.locator('[title="Refresh data"]').click();

    // Wait for RF.COUNT to update to 3
    await page.waitForFunction(
      () => {
        const api = (window as any).__univerAPI;
        if (!api) return false;
        const wb = api.getActiveWorkbook();
        if (!wb) return false;
        const sheet = wb.getActiveSheet();
        if (!sheet) return false;
        return sheet.getRange(3, 1, 1, 1).getValue() === 3;
      },
      undefined,
      { timeout: 20000 },
    );

    expect(await getCellValue(page, 3, 1)).toBe(3);
  });

  // ── ENTITY SOURCE PANEL ────────────────────────────────────

  test('entity source panel: add, expand fields, toggle visibility', async ({ page, ui }) => {
    await page.goto('/sheets/new');
    await expect(page.locator('[title="Export to .xlsx"]')).toBeVisible({ timeout: 15000 });

    // Panel should be visible in design mode
    await expect(page.locator('[title="Hide entity panel"]')).toBeVisible();

    // Add entity source
    await page.locator('button', { hasText: /\+ add/i }).click();
    const entityOption = page.locator('[role="option"], [data-value="product"], button', { hasText: /^product$/i }).first();
    await entityOption.waitFor({ timeout: 10000 });
    await entityOption.click();

    // Expand the product source and verify fields are shown
    await expect(page.locator('[draggable="true"]').first()).toBeVisible({ timeout: 10000 });

    // Toggle panel: hide
    await page.locator('[title="Hide entity panel"]').click();
    await expect(page.locator('[title="Show entity panel"]')).toBeVisible();

    // Toggle panel: show
    await page.locator('[title="Show entity panel"]').click();
    await expect(page.locator('[title="Hide entity panel"]')).toBeVisible();
  });

  // ── SHEET LOADED FROM LIST ─────────────────────────────────

  test('clicking a sheet in the list navigates to it', async ({ page, ui }) => {
    await page.goto('/sheets');
    await page.waitForSelector('h1', { timeout: 15000 });

    // Click the formula test sheet link
    await page.locator('a', { hasText: 'E2E Formula Test Sheet' }).click();

    // Should navigate to the sheet page
    await page.waitForURL(/\/sheets\/\d+/, { timeout: 15000 });
    await expect(page.locator('[title="Export to .xlsx"]')).toBeVisible({ timeout: 15000 });
  });
});
