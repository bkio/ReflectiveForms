import type { RfSheetDataStore } from '../hooks/useRfSheetData';
import type { EntitySchema } from '../types/schema';
import { formatFieldValue, getFieldSchema, resolveNestedPath, toTopLevelField } from './rf-sheet-formatters';

/**
 * Registers custom RF formula functions on a Univer Facade API instance.
 * Uses a ref-based pattern: formulas always read from `context.dataStore` and
 * `context.schemas` so data updates don't require dispose/re-register cycles.
 * After updating context, call `univerAPI.getFormula().executeCalculation()` to recalc.
 *
 * Functions registered:
 * - RF.FIELD(entity, id, fieldName) — get a single field value (supports dot-path for groups)
 * - RF.TITLE(entity, id) — shorthand for the title of an entity
 * - RF.LIST(entity, fieldName) — returns all values of a field across entity rows (supports dot-path)
 * - RF.LOOKUP(entity, matchField, matchValue, returnField) — look up a field value
 * - RF.COUNT(entity) — count of rows for an entity
 * - RF.SUM(entity, field) — sum of a numeric field across an entity
 * - RF.AVG(entity, field) — average of a numeric field across an entity
 * - RF.IDS(entity) — returns entity IDs as a spill array
 * - RF.FILTER(entity, fieldName, filterField, filterValue) — filtered list
 * - RF.MATCH(entity, id, field, operator, value) — conditional true/false
 * - RF.MATCHLIST(entity, field, operator, value) — spill array of booleans
 * - RF.REPEAT(entity, id, repeaterPath, subField) — spill array from repeater
 * - RF.REPEATCOUNT(entity, id, repeaterPath) — repeater row count
 * - RF.REPEATFIELD(entity, id, repeaterPath, index, subField) — single repeater value
 */

export interface FormulaRegistration {
  dispose: () => void;
}

/** Mutable context that formulas read from — avoids dispose/re-register on data change. */
export interface FormulaContext {
  dataStore: RfSheetDataStore;
  schemas?: Record<string, EntitySchema>;
}

/* eslint-disable @typescript-eslint/no-explicit-any */
export function registerRfFormulas(
  univerAPI: any,
  context: FormulaContext,
): FormulaRegistration {
  const disposables: Array<{ dispose: () => void }> = [];
  const formulaEngine = univerAPI.getFormula();

  // --- RF.FIELD(entity, id, fieldName) ---
  disposables.push(
    formulaEngine.registerFunction(
      'RF.FIELD',
      (...args: any[]) => {
        const [entity, id, fieldName] = args;
        if (!entity || id == null || !fieldName) return '#VALUE!';
        const fieldStr = String(fieldName);
        const isNested = fieldStr.includes('.') || fieldStr.includes('[');
        const raw = context.dataStore.getEntityField(String(entity), Number(id), isNested ? toTopLevelField(fieldStr) : fieldStr);
        if (isNested) {
          if (typeof raw === 'string' && raw.startsWith('#')) return formatFieldValue(raw, undefined);
          const leaf = resolveNestedPath(raw, fieldStr.substring(toTopLevelField(fieldStr).length).replace(/^\./, ''));
          return formatLeaf(leaf);
        }
        const fieldSchema = getFieldSchema(context.schemas, String(entity), fieldStr);
        return formatFieldValue(raw, fieldSchema);
      },
      'Returns a single field value. Supports dot-paths for groups: =RF.FIELD("event", 5, "venue.address.city")',
    ),
  );

  // --- RF.TITLE(entity, id) ---
  disposables.push(
    formulaEngine.registerFunction(
      'RF.TITLE',
      (...args: any[]) => {
        const [entity, id] = args;
        if (!entity || id == null) return '#VALUE!';
        if (context.dataStore.unauthorizedEntities.has(String(entity))) return 'N/A';
        const rows = context.dataStore.getAllEntityRows(String(entity));
        const row = rows.find((r) => r.id === Number(id));
        if (!row) return '#NOT_FOUND';
        return row.fields['title'] ?? '#NOT_FOUND';
      },
      'Returns the title of an entity instance. Usage: =RF.TITLE("employee", 5)',
    ),
  );

  // --- RF.LIST(entity, fieldName) ---
  disposables.push(
    formulaEngine.registerFunction(
      'RF.LIST',
      (...args: any[]) => {
        const [entity, fieldName] = args;
        if (!entity || !fieldName) return '#VALUE!';
        if (context.dataStore.unauthorizedEntities.has(String(entity))) return 'N/A';
        const rows = context.dataStore.getAllEntityRows(String(entity));
        if (rows.length === 0) return '#NO_DATA';
        const fieldStr = String(fieldName);
        const isNested = fieldStr.includes('.') || fieldStr.includes('[');
        const topLevel = isNested ? toTopLevelField(fieldStr) : fieldStr;
        const subPath = isNested ? fieldStr.substring(topLevel.length).replace(/^\./, '') : '';
        if (isNested) {
          return rows.map((r) => {
            const topVal = r.fields[topLevel];
            const leaf = subPath ? resolveNestedPath(topVal, subPath) : topVal;
            return [formatLeaf(leaf)];
          });
        }
        const fieldSchema = getFieldSchema(context.schemas, String(entity), fieldStr);
        return rows.map((r) => [formatFieldValue(r.fields[fieldStr] ?? '#FIELD_REMOVED', fieldSchema)]);
      },
      'Returns all values of a field as a spill array. Supports dot-paths: =RF.LIST("event", "venue.address.city")',
    ),
  );

  // --- RF.LOOKUP(entity, matchField, matchValue, returnField) ---
  disposables.push(
    formulaEngine.registerFunction(
      'RF.LOOKUP',
      (...args: any[]) => {
        const [entity, matchField, matchValue, returnField] = args;
        if (!entity || !matchField || !returnField) return '#VALUE!';
        if (context.dataStore.unauthorizedEntities.has(String(entity))) return 'N/A';
        const rows = context.dataStore.getAllEntityRows(String(entity));
        const match = rows.find((r) => {
          const val = r.fields[String(matchField)];
          return String(val) === String(matchValue);
        });
        if (!match) return '#NOT_FOUND';
        const fieldSchema = getFieldSchema(context.schemas, String(entity), String(returnField));
        return formatFieldValue(match.fields[String(returnField)] ?? '#FIELD_REMOVED', fieldSchema);
      },
      'Looks up a field value by matching another field. Usage: =RF.LOOKUP("department", "id", 3, "name")',
    ),
  );

  // --- RF.COUNT(entity) ---
  disposables.push(
    formulaEngine.registerFunction(
      'RF.COUNT',
      (...args: any[]) => {
        const [entity] = args;
        if (!entity) return '#VALUE!';
        if (context.dataStore.unauthorizedEntities.has(String(entity))) return 'N/A';
        return context.dataStore.getAllEntityRows(String(entity)).length;
      },
      'Returns the number of rows for an entity. Usage: =RF.COUNT("employee")',
    ),
  );

  // --- RF.SUM(entity, field) ---
  disposables.push(
    formulaEngine.registerFunction(
      'RF.SUM',
      (...args: any[]) => {
        const [entity, field] = args;
        if (!entity || !field) return '#VALUE!';
        if (context.dataStore.unauthorizedEntities.has(String(entity))) return 'N/A';
        const rows = context.dataStore.getAllEntityRows(String(entity));
        return rows.reduce((sum, r) => {
          const val = Number(r.fields[String(field)]);
          return sum + (isNaN(val) ? 0 : val);
        }, 0);
      },
      'Sums a numeric field across all rows of an entity. Usage: =RF.SUM("employee", "salary")',
    ),
  );

  // --- RF.AVG(entity, field) ---
  disposables.push(
    formulaEngine.registerFunction(
      'RF.AVG',
      (...args: any[]) => {
        const [entity, field] = args;
        if (!entity || !field) return '#VALUE!';
        if (context.dataStore.unauthorizedEntities.has(String(entity))) return 'N/A';
        const rows = context.dataStore.getAllEntityRows(String(entity));
        if (rows.length === 0) return 0;
        const sum = rows.reduce((acc, r) => {
          const val = Number(r.fields[String(field)]);
          return acc + (isNaN(val) ? 0 : val);
        }, 0);
        return sum / rows.length;
      },
      'Averages a numeric field across all rows of an entity. Usage: =RF.AVG("employee", "salary")',
    ),
  );

  // --- RF.IDS(entity) ---
  disposables.push(
    formulaEngine.registerFunction(
      'RF.IDS',
      (...args: any[]) => {
        const [entity] = args;
        if (!entity) return '#VALUE!';
        if (context.dataStore.unauthorizedEntities.has(String(entity))) return 'N/A';
        const rows = context.dataStore.getAllEntityRows(String(entity));
        if (rows.length === 0) return '#NO_DATA';
        return rows.map((r) => [r.id]);
      },
      'Returns all entity IDs as a spill array. Usage: =RF.IDS("employee")',
    ),
  );

  // --- RF.FILTER(entity, fieldName, filterField, filterValue) ---
  disposables.push(
    formulaEngine.registerFunction(
      'RF.FILTER',
      (...args: any[]) => {
        const [entity, fieldName, filterField, filterValue] = args;
        if (!entity || !fieldName || !filterField) return '#VALUE!';
        if (context.dataStore.unauthorizedEntities.has(String(entity))) return 'N/A';
        const rows = context.dataStore.getAllEntityRows(String(entity));
        const filtered = rows.filter((r) => {
          const val = r.fields[String(filterField)];
          return String(val) === String(filterValue);
        });
        if (filtered.length === 0) return '#NO_DATA';
        const fieldSchema = getFieldSchema(context.schemas, String(entity), String(fieldName));
        return filtered.map((r) => [formatFieldValue(r.fields[String(fieldName)] ?? '#FIELD_REMOVED', fieldSchema)]);
      },
      'Returns filtered values of a field as a spill array. Usage: =RF.FILTER("employee", "name", "is_active", "true")',
    ),
  );

  // --- RF.MATCH(entity, id, field, operator, value) ---
  disposables.push(
    formulaEngine.registerFunction(
      'RF.MATCH',
      (...args: any[]) => {
        const [entity, id, field, operator, value] = args;
        if (!entity || id == null || !field || !operator) return '#VALUE!';
        if (context.dataStore.unauthorizedEntities.has(String(entity))) return false;
        const raw = context.dataStore.getEntityField(String(entity), Number(id), String(field));
        if (typeof raw === 'string' && raw.startsWith('#')) return false;
        return evaluateCondition(raw, String(operator), value);
      },
      'Returns true/false for conditional formatting. Usage: =RF.MATCH("employee", A2, "is_remote", "=", true)',
    ),
  );

  // --- RF.MATCHLIST(entity, field, operator, value) ---
  disposables.push(
    formulaEngine.registerFunction(
      'RF.MATCHLIST',
      (...args: any[]) => {
        const [entity, field, operator, value] = args;
        if (!entity || !field || !operator) return '#VALUE!';
        if (context.dataStore.unauthorizedEntities.has(String(entity))) return 'N/A';
        const rows = context.dataStore.getAllEntityRows(String(entity));
        if (rows.length === 0) return '#NO_DATA';
        return rows.map((r) => [evaluateCondition(r.fields[String(field)], String(operator), value)]);
      },
      'Returns a spill array of true/false for each row. Usage: =RF.MATCHLIST("employee", "salary", ">", 50000)',
    ),
  );

  // --- RF.REPEAT(entity, id, repeaterPath, subField) ---
  disposables.push(
    formulaEngine.registerFunction(
      'RF.REPEAT',
      (...args: any[]) => {
        const [entity, id, repeaterPath, subField] = args;
        if (!entity || id == null || !repeaterPath || !subField) return '#VALUE!';
        if (context.dataStore.unauthorizedEntities.has(String(entity))) return 'N/A';
        const resolved = resolveRepeaterArray(context.dataStore, String(entity), Number(id), String(repeaterPath));
        if (typeof resolved === 'string') return resolved; // error sentinel
        const arr = resolved;
        if (arr.length === 0) return '#NO_DATA';
        const subStr = String(subField);
        return arr.map((el) => {
          const leaf = resolveNestedPath(el, subStr);
          return [formatLeaf(leaf === undefined ? '#FIELD_REMOVED' : leaf)];
        });
      },
      'Returns a repeater sub-field as a spill array. Supports [N] and [*] in path. Usage: =RF.REPEAT("objective", 1, "key_results", "key_result")',
    ),
  );

  // --- RF.REPEATCOUNT(entity, id, repeaterPath) ---
  disposables.push(
    formulaEngine.registerFunction(
      'RF.REPEATCOUNT',
      (...args: any[]) => {
        const [entity, id, repeaterPath] = args;
        if (!entity || id == null || !repeaterPath) return '#VALUE!';
        if (context.dataStore.unauthorizedEntities.has(String(entity))) return 'N/A';
        const resolved = resolveRepeaterArray(context.dataStore, String(entity), Number(id), String(repeaterPath));
        if (typeof resolved === 'string') {
          if (resolved === '#NO_DATA') return 0;
          return resolved;
        }
        return resolved.length;
      },
      'Returns the number of rows in a repeater. Usage: =RF.REPEATCOUNT("objective", 1, "key_results")',
    ),
  );

  // --- RF.REPEATFIELD(entity, id, repeaterPath, index, subField) ---
  disposables.push(
    formulaEngine.registerFunction(
      'RF.REPEATFIELD',
      (...args: any[]) => {
        const [entity, id, repeaterPath, index, subField] = args;
        if (!entity || id == null || !repeaterPath || index == null || !subField) return '#VALUE!';
        if (context.dataStore.unauthorizedEntities.has(String(entity))) return 'N/A';
        const idx = Number(index);
        if (isNaN(idx)) return '#VALUE!';
        if (idx < 0) return '#VALUE!';
        const flooredIdx = Math.floor(idx);
        const resolved = resolveRepeaterArray(context.dataStore, String(entity), Number(id), String(repeaterPath));
        if (typeof resolved === 'string') return resolved; // error sentinel
        if (flooredIdx >= resolved.length) return '#NOT_FOUND';
        const element = resolved[flooredIdx];
        const leaf = resolveNestedPath(element, String(subField));
        return formatLeaf(leaf === undefined ? '#FIELD_REMOVED' : leaf);
      },
      'Returns a single value from a repeater at a specific index. Usage: =RF.REPEATFIELD("objective", 1, "key_results", 0, "key_result")',
    ),
  );

  return {
    dispose: () => {
      for (const d of disposables) {
        d.dispose();
      }
    },
  };
}

function evaluateCondition(fieldValue: unknown, operator: string, compareValue: unknown): boolean {
  const strField = String(fieldValue ?? '');
  const strCompare = String(compareValue ?? '');
  const numField = Number(fieldValue);
  const numCompare = Number(compareValue);
  const bothNumeric = !isNaN(numField) && !isNaN(numCompare);

  switch (operator) {
    case '=':
    case '==':
      return strField === strCompare;
    case '!=':
    case '<>':
      return strField !== strCompare;
    case '>':
      return bothNumeric ? numField > numCompare : strField > strCompare;
    case '>=':
      return bothNumeric ? numField >= numCompare : strField >= strCompare;
    case '<':
      return bothNumeric ? numField < numCompare : strField < strCompare;
    case '<=':
      return bothNumeric ? numField <= numCompare : strField <= strCompare;
    case 'contains':
      return strField.toLowerCase().includes(strCompare.toLowerCase());
    default:
      return false;
  }
}

/**
 * Format a leaf value resolved from a nested path.
 * Uses formatFieldValue with undefined schema so Group/Repeater type
 * doesn't intercept — the leaf is already the inner value.
 * Objects/arrays that are still unresolved produce [complex].
 */
function formatLeaf(value: unknown): string | number | boolean {
  return formatFieldValue(value, undefined);
}

/**
 * Resolves a repeater path to an array from a specific entity row.
 * Returns the array, or an error sentinel string.
 */
function resolveRepeaterArray(
  dataStore: RfSheetDataStore,
  entity: string,
  id: number,
  repeaterPath: string,
): unknown[] | string {
  const topLevel = toTopLevelField(repeaterPath);
  const raw = dataStore.getEntityField(entity, id, topLevel);
  if (typeof raw === 'string' && raw.startsWith('#')) return raw;
  let arr: unknown;
  if (repeaterPath === topLevel) {
    arr = raw;
  } else {
    const subPath = repeaterPath.substring(topLevel.length).replace(/^\./, '');
    arr = resolveNestedPath(raw, subPath);
  }
  if (arr === null || arr === undefined) return '#NO_DATA';
  if (!Array.isArray(arr)) return '#VALUE!';
  return arr;
}
