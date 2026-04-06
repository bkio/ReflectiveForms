import type { EntitySchema } from '../types/schema';
import { toTopLevelField } from './rf-sheet-formatters';

/**
 * Scans a Univer workbook snapshot for RF formula references and validates
 * them against current schemas. Returns a list of missing fields.
 */
export interface StaleFieldReference {
  entity: string;
  field: string;
}

/**
 * Extracts RF formula references from a workbook snapshot's cell data,
 * then checks each entity.field pair against the provided schemas.
 * Returns an array of references that no longer exist in the schema.
 */
export function detectStaleFields(
  workbookSnapshot: Record<string, unknown>,
  schemas: Record<string, EntitySchema>,
): StaleFieldReference[] {
  const referencedFields = extractFieldReferences(workbookSnapshot);
  const stale: StaleFieldReference[] = [];

  for (const ref of referencedFields) {
    const entitySchema = schemas[ref.entity];
    if (!entitySchema) {
      // Entity itself might be removed — but that's a different issue
      // Only flag if entity exists but field doesn't
      continue;
    }
    // 'id' and 'title' are always valid (built-in)
    if (ref.field === 'id' || ref.field === 'title') continue;

    const fieldExists = entitySchema.fields.some((f) => f.name === ref.field);
    if (!fieldExists) {
      stale.push(ref);
    }
  }

  return stale;
}

/**
 * Regex patterns to extract entity/field pairs from RF formulas:
 * - RF.FIELD("entity", id, "field")
 * - RF.LIST("entity", "field")
 * - RF.LOOKUP("entity", "matchField", value, "returnField")
 * - RF.SUM("entity", "field")
 * - RF.AVG("entity", "field")
 */
const RF_FIELD_RE = /RF\.FIELD\(\s*"([^"]+)"\s*,\s*[^,]+\s*,\s*"([^"]+)"/g;
const RF_LIST_RE = /RF\.LIST\(\s*"([^"]+)"\s*,\s*"([^"]+)"/g;
const RF_LOOKUP_RE = /RF\.LOOKUP\(\s*"([^"]+)"\s*,\s*"([^"]+)"\s*,\s*[^,]+\s*,\s*"([^"]+)"/g;
const RF_SUM_RE = /RF\.SUM\(\s*"([^"]+)"\s*,\s*"([^"]+)"/g;
const RF_AVG_RE = /RF\.AVG\(\s*"([^"]+)"\s*,\s*"([^"]+)"/g;
const RF_FILTER_RE = /RF\.FILTER\(\s*"([^"]+)"\s*,\s*"([^"]+)"\s*,\s*"([^"]+)"/g;
const RF_MATCH_RE = /RF\.MATCH\(\s*"([^"]+)"\s*,\s*[^,]+\s*,\s*"([^"]+)"/g;
const RF_MATCHLIST_RE = /RF\.MATCHLIST\(\s*"([^"]+)"\s*,\s*"([^"]+)"/g;
const RF_IDS_RE = /RF\.IDS\(\s*"([^"]+)"/g;
const RF_REPEAT_RE = /RF\.REPEAT\(\s*"([^"]+)"\s*,\s*[^,]+\s*,\s*"([^"]+)"/g;
const RF_REPEATCOUNT_RE = /RF\.REPEATCOUNT\(\s*"([^"]+)"\s*,\s*[^,]+\s*,\s*"([^"]+)"/g;
const RF_REPEATFIELD_RE = /RF\.REPEATFIELD\(\s*"([^"]+)"\s*,\s*[^,]+\s*,\s*"([^"]+)"/g;

function extractFieldReferences(
  snapshot: Record<string, unknown>,
): StaleFieldReference[] {
  const refs = new Map<string, StaleFieldReference>();
  const sheets = (snapshot.sheets ?? {}) as Record<string, Record<string, unknown>>;

  for (const sheetData of Object.values(sheets)) {
    const cellData = (sheetData.cellData ?? {}) as Record<string, Record<string, Record<string, unknown>>>;
    for (const row of Object.values(cellData)) {
      for (const cell of Object.values(row)) {
        const formula = cell.f;
        if (typeof formula !== 'string') continue;
        extractFromFormula(formula, refs);
      }
    }
  }

  return Array.from(refs.values());
}

function addRef(refs: Map<string, StaleFieldReference>, entity: string, rawField: string): void {
  const field = toTopLevelField(rawField);
  const key = `${entity}.${field}`;
  if (!refs.has(key)) refs.set(key, { entity, field });
}

function extractFromFormula(formula: string, refs: Map<string, StaleFieldReference>): void {
  // RF.FIELD — captures entity (group 1) and field (group 2)
  for (const match of formula.matchAll(RF_FIELD_RE)) {
    addRef(refs, match[1], match[2]);
  }

  // RF.LIST — captures entity (group 1) and field (group 2)
  for (const match of formula.matchAll(RF_LIST_RE)) {
    addRef(refs, match[1], match[2]);
  }

  // RF.LOOKUP — captures entity (group 1), matchField (group 2), returnField (group 3)
  for (const match of formula.matchAll(RF_LOOKUP_RE)) {
    addRef(refs, match[1], match[2]);
    addRef(refs, match[1], match[3]);
  }

  // RF.SUM — captures entity (group 1) and field (group 2)
  for (const match of formula.matchAll(RF_SUM_RE)) {
    addRef(refs, match[1], match[2]);
  }

  // RF.AVG — captures entity (group 1) and field (group 2)
  for (const match of formula.matchAll(RF_AVG_RE)) {
    addRef(refs, match[1], match[2]);
  }

  // RF.FILTER — captures entity (group 1), fieldName (group 2), filterField (group 3)
  for (const match of formula.matchAll(RF_FILTER_RE)) {
    addRef(refs, match[1], match[2]);
    addRef(refs, match[1], match[3]);
  }

  // RF.MATCH — captures entity (group 1) and field (group 2)
  for (const match of formula.matchAll(RF_MATCH_RE)) {
    addRef(refs, match[1], match[2]);
  }

  // RF.MATCHLIST — captures entity (group 1) and field (group 2)
  for (const match of formula.matchAll(RF_MATCHLIST_RE)) {
    addRef(refs, match[1], match[2]);
  }

  // RF.IDS — captures entity (group 1), no field (only needs 'id' which is always present)
  for (const match of formula.matchAll(RF_IDS_RE)) {
    addRef(refs, match[1], 'id');
  }

  // RF.REPEAT — captures entity (group 1) and repeaterPath (group 2)
  for (const match of formula.matchAll(RF_REPEAT_RE)) {
    addRef(refs, match[1], match[2]);
  }

  // RF.REPEATCOUNT — captures entity (group 1) and repeaterPath (group 2)
  for (const match of formula.matchAll(RF_REPEATCOUNT_RE)) {
    addRef(refs, match[1], match[2]);
  }

  // RF.REPEATFIELD — captures entity (group 1) and repeaterPath (group 2)
  for (const match of formula.matchAll(RF_REPEATFIELD_RE)) {
    addRef(refs, match[1], match[2]);
  }
}

/**
 * Extracts per-entity field sets from a workbook snapshot's formulas.
 * Returns a Map of entity name → Set of field names used in formulas.
 * Used to construct field-filtered bulk_read requests.
 */
export function extractRequiredFields(
  workbookSnapshot: Record<string, unknown>,
): Map<string, Set<string>> {
  if (!workbookSnapshot || typeof workbookSnapshot !== 'object') {
    return new Map();
  }
  const refs = extractFieldReferences(workbookSnapshot);
  const result = new Map<string, Set<string>>();
  for (const ref of refs) {
    let fields = result.get(ref.entity);
    if (!fields) {
      fields = new Set<string>();
      result.set(ref.entity, fields);
    }
    fields.add(ref.field);
  }
  // Always include 'title' for RF.TITLE (it may not appear in formulas directly)
  for (const [, fields] of result) {
    fields.add('title');
  }
  return result;
}
