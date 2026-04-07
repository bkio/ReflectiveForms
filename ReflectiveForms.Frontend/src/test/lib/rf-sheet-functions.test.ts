import { describe, it, expect, vi } from 'vitest';
import { registerRfFormulas } from '../../lib/rf-sheet-functions';
import type { RfSheetDataStore } from '../../hooks/useRfSheetData';
import type { EntitySchema, FieldSchema } from '../../types/schema';

function createMockDataStore(overrides?: Partial<RfSheetDataStore>): RfSheetDataStore {
  const entityData = new Map<string, Map<number, Record<string, unknown>>>();

  // Default: employee entity with 3 rows
  const employeeRows = new Map<number, Record<string, unknown>>();
  employeeRows.set(1, { name: 'Alice', salary: 60000, department_id: 10, title: 'Senior Dev' });
  employeeRows.set(2, { name: 'Bob', salary: 45000, department_id: 10, title: 'Junior Dev' });
  employeeRows.set(3, { name: 'Charlie', salary: 80000, department_id: 20, title: 'Lead Dev' });
  entityData.set('employee', employeeRows);

  // department entity
  const deptRows = new Map<number, Record<string, unknown>>();
  deptRows.set(10, { dept_name: 'Engineering', budget: 500000 });
  deptRows.set(20, { dept_name: 'Design', budget: 200000 });
  entityData.set('department', deptRows);

  // objective entity — has group + repeater
  const objectiveRows = new Map<number, Record<string, unknown>>();
  objectiveRows.set(1, {
    title: 'Grow Revenue',
    creator_comment: { author: 1, comment: 'Good work' },
    key_results: [
      { key_result: 'Increase revenue', achieved: true, key_result_comments: [{ author: 1, comment: 'On track' }] },
      { key_result: 'Reduce churn', achieved: false, key_result_comments: [] },
    ],
    null_repeater: null,
    string_field: 'not-an-array',
  });
  objectiveRows.set(2, {
    title: 'Improve Quality',
    creator_comment: null,
    key_results: [],
  });
  entityData.set('objective', objectiveRows);

  // event entity — has group-in-group
  const eventRows = new Map<number, Record<string, unknown>>();
  eventRows.set(1, {
    title: 'Tech Conference',
    venue: { venue_name: 'Conv Center', venue_address: { city: 'NYC', country: 'US' } },
  });
  eventRows.set(2, {
    title: 'Workshop',
    venue: { venue_name: 'Studio', venue_address: { city: 'London', country: 'GB' } },
  });
  entityData.set('event', eventRows);

  // survey entity — has 3-level repeater nesting
  const surveyRows = new Map<number, Record<string, unknown>>();
  surveyRows.set(1, {
    title: 'Customer Survey',
    sections: [
      {
        section_title: 'Demographics',
        questions: [
          { question_text: 'Name?', choices: [{ choice_text: 'A' }, { choice_text: 'B' }] },
          { question_text: 'Age?', choices: [] },
        ],
      },
      {
        section_title: 'Feedback',
        questions: [
          { question_text: 'Rating?', choices: [{ choice_text: '1' }, { choice_text: '2' }, { choice_text: '3' }] },
        ],
      },
    ],
  });
  entityData.set('survey', surveyRows);

  return {
    entityData,
    unauthorizedEntities: new Set(overrides?.unauthorizedEntities ?? []),
    isLoading: false,
    error: null,
    refresh: vi.fn(),
    getEntityField: (entity, id, field) => {
      if ((overrides?.unauthorizedEntities ?? new Set()).has(entity)) return '#NO_ACCESS';
      const rows = entityData.get(entity);
      if (!rows) return '#NO_DATA';
      const row = rows.get(id);
      if (!row) return '#NOT_FOUND';
      if (!(field in row)) return '#FIELD_REMOVED';
      return row[field];
    },
    getAllEntityRows: (entity) => {
      const rows = entityData.get(entity);
      if (!rows) return [];
      return Array.from(rows.entries()).map(([id, fields]) => ({ id, fields }));
    },
    ...overrides,
  };
}

function createMockUniverAPI() {
  const registeredFunctions = new Map<string, { fn: (...args: unknown[]) => unknown; dispose: () => void }>();

  return {
    getFormula: () => ({
      registerFunction: (name: string, fn: (...args: unknown[]) => unknown, _desc: string) => {
        const disposable = { dispose: vi.fn() };
        registeredFunctions.set(name, { fn, dispose: disposable.dispose });
        return disposable;
      },
    }),
    /** Test helper to call a registered formula function */
    callFunction: (name: string, ...args: unknown[]) => {
      const entry = registeredFunctions.get(name);
      if (!entry) throw new Error(`Function ${name} not registered`);
      return entry.fn(...args);
    },
    /** Test helper to check if function is registered */
    hasFunction: (name: string) => registeredFunctions.has(name),
    /** Test helper to get all registered function names */
    getFunctionNames: () => Array.from(registeredFunctions.keys()),
  };
}

describe('registerRfFormulas', () => {
  // ── Registration ───────────────────────────────────────────────────────

  it('registers all 14 RF functions', () => {
    const api = createMockUniverAPI();
    const store = createMockDataStore();

    registerRfFormulas(api, { dataStore: store });

    expect(api.hasFunction('RF.FIELD')).toBe(true);
    expect(api.hasFunction('RF.TITLE')).toBe(true);
    expect(api.hasFunction('RF.LIST')).toBe(true);
    expect(api.hasFunction('RF.LOOKUP')).toBe(true);
    expect(api.hasFunction('RF.COUNT')).toBe(true);
    expect(api.hasFunction('RF.SUM')).toBe(true);
    expect(api.hasFunction('RF.AVG')).toBe(true);
    expect(api.hasFunction('RF.IDS')).toBe(true);
    expect(api.hasFunction('RF.FILTER')).toBe(true);
    expect(api.hasFunction('RF.MATCH')).toBe(true);
    expect(api.hasFunction('RF.MATCHLIST')).toBe(true);
    expect(api.hasFunction('RF.REPEAT')).toBe(true);
    expect(api.hasFunction('RF.REPEATCOUNT')).toBe(true);
    expect(api.hasFunction('RF.REPEATFIELD')).toBe(true);
    expect(api.getFunctionNames()).toHaveLength(14);
  });

  it('dispose() cleans up all registered functions', () => {
    const api = createMockUniverAPI();
    const store = createMockDataStore();

    const registration = registerRfFormulas(api, { dataStore: store });
    registration.dispose();
  });

  // ── RF.FIELD ───────────────────────────────────────────────────────────

  describe('RF.FIELD', () => {
    it('returns a field value for a specific entity and id', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore();
      registerRfFormulas(api, { dataStore: store });

      expect(api.callFunction('RF.FIELD', 'employee', 1, 'name')).toBe('Alice');
      expect(api.callFunction('RF.FIELD', 'employee', 2, 'salary')).toBe(45000);
    });

    it('returns N/A for unauthorized entity', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore({ unauthorizedEntities: new Set(['secret']) });
      registerRfFormulas(api, { dataStore: store });

      expect(api.callFunction('RF.FIELD', 'secret', 1, 'data')).toBe('N/A');
    });

    it('returns #NOT_FOUND for missing id', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore();
      registerRfFormulas(api, { dataStore: store });

      expect(api.callFunction('RF.FIELD', 'employee', 999, 'name')).toBe('#NOT_FOUND');
    });

    it('returns #FIELD_REMOVED for missing field', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore();
      registerRfFormulas(api, { dataStore: store });

      expect(api.callFunction('RF.FIELD', 'employee', 1, 'nonexistent')).toBe('#FIELD_REMOVED');
    });

    it('returns #VALUE! for missing arguments', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore();
      registerRfFormulas(api, { dataStore: store });

      expect(api.callFunction('RF.FIELD')).toBe('#VALUE!');
      expect(api.callFunction('RF.FIELD', 'employee')).toBe('#VALUE!');
    });
  });

  // ── RF.TITLE ───────────────────────────────────────────────────────────

  describe('RF.TITLE', () => {
    it('returns the title field of an entity', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore();
      registerRfFormulas(api, { dataStore: store });

      expect(api.callFunction('RF.TITLE', 'employee', 1)).toBe('Senior Dev');
    });

    it('returns #NOT_FOUND for missing entity id', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore();
      registerRfFormulas(api, { dataStore: store });

      expect(api.callFunction('RF.TITLE', 'employee', 999)).toBe('#NOT_FOUND');
    });

    it('returns #VALUE! for missing arguments', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore();
      registerRfFormulas(api, { dataStore: store });

      expect(api.callFunction('RF.TITLE')).toBe('#VALUE!');
    });
  });

  // ── RF.LIST ────────────────────────────────────────────────────────────

  describe('RF.LIST', () => {
    it('returns all values of a field as a spill array', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore();
      registerRfFormulas(api, { dataStore: store });

      const result = api.callFunction('RF.LIST', 'employee', 'name');
      expect(result).toEqual([['Alice'], ['Bob'], ['Charlie']]);
    });

    it('returns N/A for unauthorized entity', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore({ unauthorizedEntities: new Set(['secret']) });
      registerRfFormulas(api, { dataStore: store });

      expect(api.callFunction('RF.LIST', 'secret', 'data')).toBe('N/A');
    });

    it('returns #NO_DATA for empty entity', () => {
      const api = createMockUniverAPI();
      const entityData = new Map();
      entityData.set('empty_entity', new Map());
      const store = createMockDataStore();
      store.getAllEntityRows = (entity: string) => {
        if (entity === 'empty_entity') return [];
        return createMockDataStore().getAllEntityRows(entity);
      };
      registerRfFormulas(api, { dataStore: store });

      expect(api.callFunction('RF.LIST', 'empty_entity', 'name')).toBe('#NO_DATA');
    });

    it('returns #FIELD_REMOVED for missing field in rows', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore();
      registerRfFormulas(api, { dataStore: store });

      const result = api.callFunction('RF.LIST', 'employee', 'nonexistent') as unknown[][];
      expect(result).toEqual([['#FIELD_REMOVED'], ['#FIELD_REMOVED'], ['#FIELD_REMOVED']]);
    });
  });

  // ── RF.LOOKUP ──────────────────────────────────────────────────────────

  describe('RF.LOOKUP', () => {
    it('finds a row by matching field and returns another field', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore();
      registerRfFormulas(api, { dataStore: store });

      expect(api.callFunction('RF.LOOKUP', 'department', 'dept_name', 'Engineering', 'budget')).toBe(500000);
    });

    it('returns #NOT_FOUND when no match', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore();
      registerRfFormulas(api, { dataStore: store });

      expect(api.callFunction('RF.LOOKUP', 'department', 'dept_name', 'Marketing', 'budget')).toBe('#NOT_FOUND');
    });

    it('returns N/A for unauthorized entity', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore({ unauthorizedEntities: new Set(['secret']) });
      registerRfFormulas(api, { dataStore: store });

      expect(api.callFunction('RF.LOOKUP', 'secret', 'id', '1', 'data')).toBe('N/A');
    });
  });

  // ── RF.COUNT ───────────────────────────────────────────────────────────

  describe('RF.COUNT', () => {
    it('returns count of rows', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore();
      registerRfFormulas(api, { dataStore: store });

      expect(api.callFunction('RF.COUNT', 'employee')).toBe(3);
      expect(api.callFunction('RF.COUNT', 'department')).toBe(2);
    });

    it('returns 0 for entity with no rows', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore();
      store.getAllEntityRows = (entity: string) => {
        if (entity === 'empty') return [];
        return createMockDataStore().getAllEntityRows(entity);
      };
      registerRfFormulas(api, { dataStore: store });

      expect(api.callFunction('RF.COUNT', 'empty')).toBe(0);
    });

    it('returns N/A for unauthorized entity', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore({ unauthorizedEntities: new Set(['secret']) });
      registerRfFormulas(api, { dataStore: store });

      expect(api.callFunction('RF.COUNT', 'secret')).toBe('N/A');
    });
  });

  // ── RF.SUM ─────────────────────────────────────────────────────────────

  describe('RF.SUM', () => {
    it('sums numeric field values', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore();
      registerRfFormulas(api, { dataStore: store });

      // 60000 + 45000 + 80000 = 185000
      expect(api.callFunction('RF.SUM', 'employee', 'salary')).toBe(185000);
    });

    it('skips non-numeric values', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore();
      registerRfFormulas(api, { dataStore: store });

      // name field is a string → NaN → treated as 0
      expect(api.callFunction('RF.SUM', 'employee', 'name')).toBe(0);
    });

    it('returns N/A for unauthorized entity', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore({ unauthorizedEntities: new Set(['secret']) });
      registerRfFormulas(api, { dataStore: store });

      expect(api.callFunction('RF.SUM', 'secret', 'amount')).toBe('N/A');
    });
  });

  // ── RF.AVG ─────────────────────────────────────────────────────────────

  describe('RF.AVG', () => {
    it('averages numeric field values', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore();
      registerRfFormulas(api, { dataStore: store });

      // (60000 + 45000 + 80000) / 3 ≈ 61666.67
      const avg = api.callFunction('RF.AVG', 'employee', 'salary') as number;
      expect(avg).toBeCloseTo(61666.67, 0);
    });

    it('returns 0 for entity with no rows', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore();
      store.getAllEntityRows = (entity: string) => {
        if (entity === 'empty') return [];
        return createMockDataStore().getAllEntityRows(entity);
      };
      registerRfFormulas(api, { dataStore: store });

      expect(api.callFunction('RF.AVG', 'empty', 'salary')).toBe(0);
    });

    it('returns N/A for unauthorized entity', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore({ unauthorizedEntities: new Set(['secret']) });
      registerRfFormulas(api, { dataStore: store });

      expect(api.callFunction('RF.AVG', 'secret', 'amount')).toBe('N/A');
    });
  });

  // ── Type-aware formatting (Phase 3) ────────────────────────────────────

  describe('type-aware formatting with schemas', () => {
    function fieldOf(overrides: Partial<FieldSchema>): FieldSchema {
      return {
        name: 'test',
        type: 'Text',
        label: 'Test',
        required: false,
        has_dynamic_choices_runtime: false,
        has_dynamic_choices_compile_time: false,
        has_logic_sanity_check: false,
        ...overrides,
      };
    }

    function createSchemas(): Record<string, EntitySchema> {
      return {
        employee: {
          entity_name: 'employee',
          readable_name: { singular: 'Employee', plural: 'Employees' },
          features: {
            has_author: false, has_tags: false, has_categories: false,
            has_parent_child: false, require_title_uniqueness: false, supports_frontend_edit: true,
          },
          fields: [
            fieldOf({ name: 'name', type: 'Text', label: 'Name' }),
            fieldOf({ name: 'salary', type: 'Number', label: 'Salary' }),
            fieldOf({ name: 'active', type: 'Checkbox', label: 'Active' }),
            fieldOf({
              name: 'status',
              type: 'Select',
              label: 'Status',
              select_options: {
                allow_multiple: false,
                choices: [
                  { value: 'full_time', label: 'Full Time' },
                  { value: 'part_time', label: 'Part Time' },
                ],
              },
            }),
          ],
          api_endpoints: { crud: '', sanity_check: '', entity_lock: '', media: '' },
          schema_version: '1.0',
        },
      };
    }

    it('RF.FIELD formats checkbox values as ✓/✗', () => {
      const api = createMockUniverAPI();
      const entityData = new Map<string, Map<number, Record<string, unknown>>>();
      const rows = new Map<number, Record<string, unknown>>();
      rows.set(1, { active: true });
      rows.set(2, { active: false });
      entityData.set('employee', rows);

      const store = createMockDataStore();
      store.entityData = entityData;
      store.getEntityField = (entity, id, field) => {
        const r = entityData.get(entity)?.get(id);
        if (!r) return '#NOT_FOUND';
        if (!(field in r)) return '#FIELD_REMOVED';
        return r[field];
      };
      store.getAllEntityRows = (entity) => {
        const r = entityData.get(entity);
        if (!r) return [];
        return Array.from(r.entries()).map(([id, fields]) => ({ id, fields }));
      };

      const schemas = createSchemas();
      registerRfFormulas(api, { dataStore: store, schemas });

      expect(api.callFunction('RF.FIELD', 'employee', 1, 'active')).toBe('✓');
      expect(api.callFunction('RF.FIELD', 'employee', 2, 'active')).toBe('✗');
    });

    it('RF.FIELD resolves select values to labels', () => {
      const api = createMockUniverAPI();
      const entityData = new Map<string, Map<number, Record<string, unknown>>>();
      const rows = new Map<number, Record<string, unknown>>();
      rows.set(1, { status: 'full_time' });
      entityData.set('employee', rows);

      const store = createMockDataStore();
      store.entityData = entityData;
      store.getEntityField = (entity, id, field) => {
        const r = entityData.get(entity)?.get(id);
        if (!r) return '#NOT_FOUND';
        if (!(field in r)) return '#FIELD_REMOVED';
        return r[field];
      };

      const schemas = createSchemas();
      registerRfFormulas(api, { dataStore: store, schemas });

      expect(api.callFunction('RF.FIELD', 'employee', 1, 'status')).toBe('Full Time');
    });

    it('RF.LIST formats values using schema types', () => {
      const api = createMockUniverAPI();
      const entityData = new Map<string, Map<number, Record<string, unknown>>>();
      const rows = new Map<number, Record<string, unknown>>();
      rows.set(1, { active: true });
      rows.set(2, { active: false });
      entityData.set('employee', rows);

      const store = createMockDataStore();
      store.entityData = entityData;
      store.getAllEntityRows = (entity) => {
        const r = entityData.get(entity);
        if (!r) return [];
        return Array.from(r.entries()).map(([id, fields]) => ({ id, fields }));
      };

      const schemas = createSchemas();
      registerRfFormulas(api, { dataStore: store, schemas });

      expect(api.callFunction('RF.LIST', 'employee', 'active')).toEqual([['✓'], ['✗']]);
    });

    it('RF.LOOKUP formats return value using schema type', () => {
      const api = createMockUniverAPI();
      const entityData = new Map<string, Map<number, Record<string, unknown>>>();
      const rows = new Map<number, Record<string, unknown>>();
      rows.set(1, { name: 'Alice', status: 'part_time' });
      entityData.set('employee', rows);

      const store = createMockDataStore();
      store.entityData = entityData;
      store.getAllEntityRows = (entity) => {
        const r = entityData.get(entity);
        if (!r) return [];
        return Array.from(r.entries()).map(([id, fields]) => ({ id, fields }));
      };

      const schemas = createSchemas();
      registerRfFormulas(api, { dataStore: store, schemas });

      expect(api.callFunction('RF.LOOKUP', 'employee', 'name', 'Alice', 'status')).toBe('Part Time');
    });
  });

  // ── N/A propagation (Phase 3) ──────────────────────────────────────────

  describe('N/A permission propagation', () => {
    it('RF.TITLE returns N/A for unauthorized entity', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore({ unauthorizedEntities: new Set(['secret']) });
      registerRfFormulas(api, { dataStore: store });

      expect(api.callFunction('RF.TITLE', 'secret', 1)).toBe('N/A');
    });

    it('RF.FIELD returns N/A for unauthorized entity via formatFieldValue', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore({ unauthorizedEntities: new Set(['secret']) });
      registerRfFormulas(api, { dataStore: store });

      // getEntityField returns #NO_ACCESS, formatFieldValue converts to N/A
      expect(api.callFunction('RF.FIELD', 'secret', 1, 'data')).toBe('N/A');
    });
  });

  // ── Phase 4: RF.IDS ───────────────────────────────────────────────────

  describe('RF.IDS', () => {
    it('returns all entity IDs as a spill array', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore();
      registerRfFormulas(api, { dataStore: store });

      const result = api.callFunction('RF.IDS', 'employee');
      expect(result).toEqual([[1], [2], [3]]);
    });

    it('returns N/A for unauthorized entity', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore({ unauthorizedEntities: new Set(['secret']) });
      registerRfFormulas(api, { dataStore: store });

      expect(api.callFunction('RF.IDS', 'secret')).toBe('N/A');
    });

    it('returns #NO_DATA for empty entity', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore();
      store.getAllEntityRows = (entity: string) => {
        if (entity === 'empty') return [];
        return createMockDataStore().getAllEntityRows(entity);
      };
      registerRfFormulas(api, { dataStore: store });

      expect(api.callFunction('RF.IDS', 'empty')).toBe('#NO_DATA');
    });

    it('returns #VALUE! for missing arguments', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore();
      registerRfFormulas(api, { dataStore: store });

      expect(api.callFunction('RF.IDS')).toBe('#VALUE!');
    });
  });

  // ── Phase 4: RF.FILTER ────────────────────────────────────────────────

  describe('RF.FILTER', () => {
    it('returns filtered rows matching a condition', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore();
      registerRfFormulas(api, { dataStore: store });

      // Filter employees in department 10
      const result = api.callFunction('RF.FILTER', 'employee', 'name', 'department_id', '10');
      expect(result).toEqual([['Alice'], ['Bob']]);
    });

    it('returns #NO_DATA when no rows match', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore();
      registerRfFormulas(api, { dataStore: store });

      expect(api.callFunction('RF.FILTER', 'employee', 'name', 'department_id', '999')).toBe('#NO_DATA');
    });

    it('returns N/A for unauthorized entity', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore({ unauthorizedEntities: new Set(['secret']) });
      registerRfFormulas(api, { dataStore: store });

      expect(api.callFunction('RF.FILTER', 'secret', 'name', 'field', 'value')).toBe('N/A');
    });

    it('returns #VALUE! for missing arguments', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore();
      registerRfFormulas(api, { dataStore: store });

      expect(api.callFunction('RF.FILTER', 'employee', 'name')).toBe('#VALUE!');
    });
  });

  // ── Phase 4: RF.MATCH ─────────────────────────────────────────────────

  describe('RF.MATCH', () => {
    it('returns true for equality match', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore();
      registerRfFormulas(api, { dataStore: store });

      expect(api.callFunction('RF.MATCH', 'employee', 1, 'name', '=', 'Alice')).toBe(true);
    });

    it('returns false for inequality', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore();
      registerRfFormulas(api, { dataStore: store });

      expect(api.callFunction('RF.MATCH', 'employee', 1, 'name', '=', 'Bob')).toBe(false);
    });

    it('supports > operator for numbers', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore();
      registerRfFormulas(api, { dataStore: store });

      expect(api.callFunction('RF.MATCH', 'employee', 1, 'salary', '>', 50000)).toBe(true);
      expect(api.callFunction('RF.MATCH', 'employee', 2, 'salary', '>', 50000)).toBe(false);
    });

    it('supports < operator', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore();
      registerRfFormulas(api, { dataStore: store });

      expect(api.callFunction('RF.MATCH', 'employee', 2, 'salary', '<', 50000)).toBe(true);
    });

    it('supports >= and <= operators', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore();
      registerRfFormulas(api, { dataStore: store });

      expect(api.callFunction('RF.MATCH', 'employee', 1, 'salary', '>=', 60000)).toBe(true);
      expect(api.callFunction('RF.MATCH', 'employee', 1, 'salary', '<=', 60000)).toBe(true);
    });

    it('supports != operator', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore();
      registerRfFormulas(api, { dataStore: store });

      expect(api.callFunction('RF.MATCH', 'employee', 1, 'name', '!=', 'Bob')).toBe(true);
    });

    it('supports contains operator', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore();
      registerRfFormulas(api, { dataStore: store });

      expect(api.callFunction('RF.MATCH', 'employee', 1, 'name', 'contains', 'lic')).toBe(true);
      expect(api.callFunction('RF.MATCH', 'employee', 1, 'name', 'contains', 'xyz')).toBe(false);
    });

    it('returns false for unauthorized entity', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore({ unauthorizedEntities: new Set(['secret']) });
      registerRfFormulas(api, { dataStore: store });

      expect(api.callFunction('RF.MATCH', 'secret', 1, 'field', '=', 'value')).toBe(false);
    });

    it('returns false for error sentinel values', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore();
      registerRfFormulas(api, { dataStore: store });

      expect(api.callFunction('RF.MATCH', 'employee', 999, 'name', '=', 'Alice')).toBe(false);
    });
  });

  // ── Phase 4: RF.MATCHLIST ─────────────────────────────────────────────

  describe('RF.MATCHLIST', () => {
    it('returns a spill array of booleans', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore();
      registerRfFormulas(api, { dataStore: store });

      const result = api.callFunction('RF.MATCHLIST', 'employee', 'salary', '>', 50000);
      expect(result).toEqual([[true], [false], [true]]);
    });

    it('returns N/A for unauthorized entity', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore({ unauthorizedEntities: new Set(['secret']) });
      registerRfFormulas(api, { dataStore: store });

      expect(api.callFunction('RF.MATCHLIST', 'secret', 'field', '=', 'value')).toBe('N/A');
    });

    it('returns #NO_DATA for empty entity', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore();
      store.getAllEntityRows = (entity: string) => {
        if (entity === 'empty') return [];
        return createMockDataStore().getAllEntityRows(entity);
      };
      registerRfFormulas(api, { dataStore: store });

      expect(api.callFunction('RF.MATCHLIST', 'empty', 'field', '=', 'value')).toBe('#NO_DATA');
    });
  });

  // ── RF.FIELD with dot-paths (groups) ──────────────────────────────────

  describe('RF.FIELD — dot-path (groups)', () => {
    it('returns group sub-field via dot-path', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore();
      registerRfFormulas(api, { dataStore: store });

      expect(api.callFunction('RF.FIELD', 'objective', 1, 'creator_comment.comment')).toBe('Good work');
    });

    it('returns group-in-group field via deep dot-path', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore();
      registerRfFormulas(api, { dataStore: store });

      expect(api.callFunction('RF.FIELD', 'event', 1, 'venue.venue_address.city')).toBe('NYC');
      expect(api.callFunction('RF.FIELD', 'event', 1, 'venue.venue_address.country')).toBe('US');
    });

    it('returns [complex] for partial group path (no sub-field)', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore();
      registerRfFormulas(api, { dataStore: store });

      expect(api.callFunction('RF.FIELD', 'objective', 1, 'creator_comment')).toBe('[complex]');
    });

    it('returns empty string for null group via dot-path', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore();
      registerRfFormulas(api, { dataStore: store });

      // objective id=2 has creator_comment: null
      expect(api.callFunction('RF.FIELD', 'objective', 2, 'creator_comment.comment')).toBe('');
    });

    it('returns empty string for missing sub-key in group', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore();
      registerRfFormulas(api, { dataStore: store });

      expect(api.callFunction('RF.FIELD', 'event', 1, 'venue.venue_address.nonexistent')).toBe('');
    });

    it('flat field behavior is unchanged', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore();
      registerRfFormulas(api, { dataStore: store });

      expect(api.callFunction('RF.FIELD', 'employee', 1, 'name')).toBe('Alice');
    });

    it('returns error sentinel for missing top-level field in dot-path', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore();
      registerRfFormulas(api, { dataStore: store });

      expect(api.callFunction('RF.FIELD', 'employee', 1, 'nonexistent.sub')).toBe('#FIELD_REMOVED');
    });
  });

  // ── RF.LIST with dot-paths ────────────────────────────────────────────

  describe('RF.LIST — dot-path (groups)', () => {
    it('returns list of group sub-field values across rows', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore();
      registerRfFormulas(api, { dataStore: store });

      const result = api.callFunction('RF.LIST', 'event', 'venue.venue_address.city');
      expect(result).toEqual([['NYC'], ['London']]);
    });

    it('returns [complex] per row for partial group path', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore();
      registerRfFormulas(api, { dataStore: store });

      const result = api.callFunction('RF.LIST', 'event', 'venue');
      expect(result).toEqual([['[complex]'], ['[complex]']]);
    });

    it('flat field behavior is unchanged', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore();
      registerRfFormulas(api, { dataStore: store });

      expect(api.callFunction('RF.LIST', 'employee', 'name')).toEqual([['Alice'], ['Bob'], ['Charlie']]);
    });
  });

  // ── RF.REPEAT ─────────────────────────────────────────────────────────

  describe('RF.REPEAT', () => {
    it('returns spill array of repeater sub-field', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore();
      registerRfFormulas(api, { dataStore: store });

      expect(api.callFunction('RF.REPEAT', 'objective', 1, 'key_results', 'key_result'))
        .toEqual([['Increase revenue'], ['Reduce churn']]);
    });

    it('returns spill array of boolean sub-field', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore();
      registerRfFormulas(api, { dataStore: store });

      expect(api.callFunction('RF.REPEAT', 'objective', 1, 'key_results', 'achieved'))
        .toEqual([[true], [false]]);
    });

    it('accesses nested repeater via bracket index', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore();
      registerRfFormulas(api, { dataStore: store });

      expect(api.callFunction('RF.REPEAT', 'survey', 1, 'sections[0].questions', 'question_text'))
        .toEqual([['Name?'], ['Age?']]);
    });

    it('accesses second section via bracket index', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore();
      registerRfFormulas(api, { dataStore: store });

      expect(api.callFunction('RF.REPEAT', 'survey', 1, 'sections[1].questions', 'question_text'))
        .toEqual([['Rating?']]);
    });

    it('expands all with wildcard', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore();
      registerRfFormulas(api, { dataStore: store });

      expect(api.callFunction('RF.REPEAT', 'survey', 1, 'sections[*].questions', 'question_text'))
        .toEqual([['Name?'], ['Age?'], ['Rating?']]);
    });

    it('handles 3-level wildcard (sections → questions → choices)', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore();
      registerRfFormulas(api, { dataStore: store });

      expect(api.callFunction('RF.REPEAT', 'survey', 1, 'sections[*].questions[*].choices', 'choice_text'))
        .toEqual([['A'], ['B'], ['1'], ['2'], ['3']]);
    });

    it('returns [complex] when sub-field resolves to object', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore();
      registerRfFormulas(api, { dataStore: store });

      // key_result_comments is an array (object) → [complex]
      const result = api.callFunction('RF.REPEAT', 'objective', 1, 'key_results', 'key_result_comments') as unknown[][];
      expect(result[0][0]).toBe('[complex]');
      expect(result[1][0]).toBe('[complex]');
    });

    it('returns #FIELD_REMOVED for missing sub-field', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore();
      registerRfFormulas(api, { dataStore: store });

      const result = api.callFunction('RF.REPEAT', 'objective', 1, 'key_results', 'nonexistent') as unknown[][];
      expect(result[0][0]).toBe('#FIELD_REMOVED');
      expect(result[1][0]).toBe('#FIELD_REMOVED');
    });

    it('returns N/A for unauthorized entity', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore({ unauthorizedEntities: new Set(['secret']) });
      registerRfFormulas(api, { dataStore: store });

      expect(api.callFunction('RF.REPEAT', 'secret', 1, 'items', 'name')).toBe('N/A');
    });

    it('returns #NOT_FOUND for missing entity id', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore();
      registerRfFormulas(api, { dataStore: store });

      expect(api.callFunction('RF.REPEAT', 'objective', 999, 'key_results', 'key_result')).toBe('#NOT_FOUND');
    });

    it('returns #NO_DATA for null repeater', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore();
      registerRfFormulas(api, { dataStore: store });

      expect(api.callFunction('RF.REPEAT', 'objective', 1, 'null_repeater', 'sub')).toBe('#NO_DATA');
    });

    it('returns #NO_DATA for empty repeater array', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore();
      registerRfFormulas(api, { dataStore: store });

      // objective id=2 has key_results: []
      expect(api.callFunction('RF.REPEAT', 'objective', 2, 'key_results', 'key_result')).toBe('#NO_DATA');
    });

    it('returns #VALUE! for non-array repeater field', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore();
      registerRfFormulas(api, { dataStore: store });

      expect(api.callFunction('RF.REPEAT', 'objective', 1, 'string_field', 'sub')).toBe('#VALUE!');
    });

    it('returns #VALUE! for missing arguments', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore();
      registerRfFormulas(api, { dataStore: store });

      expect(api.callFunction('RF.REPEAT', 'objective', 1, 'key_results')).toBe('#VALUE!');
      expect(api.callFunction('RF.REPEAT', 'objective', 1)).toBe('#VALUE!');
      expect(api.callFunction('RF.REPEAT')).toBe('#VALUE!');
    });

    it('returns #FIELD_REMOVED for missing top-level field', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore();
      registerRfFormulas(api, { dataStore: store });

      expect(api.callFunction('RF.REPEAT', 'objective', 1, 'nonexistent', 'sub')).toBe('#FIELD_REMOVED');
    });

    it('handles bracket out of bounds in path → #NO_DATA', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore();
      registerRfFormulas(api, { dataStore: store });

      // sections[99] → undefined → non-array → #NO_DATA
      expect(api.callFunction('RF.REPEAT', 'survey', 1, 'sections[99].questions', 'question_text')).toBe('#NO_DATA');
    });
  });

  // ── RF.REPEATCOUNT ────────────────────────────────────────────────────

  describe('RF.REPEATCOUNT', () => {
    it('returns count of repeater rows', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore();
      registerRfFormulas(api, { dataStore: store });

      expect(api.callFunction('RF.REPEATCOUNT', 'objective', 1, 'key_results')).toBe(2);
    });

    it('returns 0 for null repeater', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore();
      registerRfFormulas(api, { dataStore: store });

      expect(api.callFunction('RF.REPEATCOUNT', 'objective', 1, 'null_repeater')).toBe(0);
    });

    it('returns 0 for empty repeater array', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore();
      registerRfFormulas(api, { dataStore: store });

      expect(api.callFunction('RF.REPEATCOUNT', 'objective', 2, 'key_results')).toBe(0);
    });

    it('returns #VALUE! for non-array field', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore();
      registerRfFormulas(api, { dataStore: store });

      expect(api.callFunction('RF.REPEATCOUNT', 'objective', 1, 'string_field')).toBe('#VALUE!');
    });

    it('returns N/A for unauthorized entity', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore({ unauthorizedEntities: new Set(['secret']) });
      registerRfFormulas(api, { dataStore: store });

      expect(api.callFunction('RF.REPEATCOUNT', 'secret', 1, 'items')).toBe('N/A');
    });

    it('returns #NOT_FOUND for missing entity id', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore();
      registerRfFormulas(api, { dataStore: store });

      expect(api.callFunction('RF.REPEATCOUNT', 'objective', 999, 'key_results')).toBe('#NOT_FOUND');
    });

    it('returns #VALUE! for missing arguments', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore();
      registerRfFormulas(api, { dataStore: store });

      expect(api.callFunction('RF.REPEATCOUNT', 'objective', 1)).toBe('#VALUE!');
      expect(api.callFunction('RF.REPEATCOUNT')).toBe('#VALUE!');
    });

    it('counts nested repeater via bracket path', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore();
      registerRfFormulas(api, { dataStore: store });

      expect(api.callFunction('RF.REPEATCOUNT', 'survey', 1, 'sections[0].questions')).toBe(2);
      expect(api.callFunction('RF.REPEATCOUNT', 'survey', 1, 'sections[1].questions')).toBe(1);
    });

    it('counts flattened via wildcard', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore();
      registerRfFormulas(api, { dataStore: store });

      // 2 questions in section 0 + 1 in section 1 = 3
      expect(api.callFunction('RF.REPEATCOUNT', 'survey', 1, 'sections[*].questions')).toBe(3);
    });
  });

  // ── RF.REPEATFIELD ────────────────────────────────────────────────────

  describe('RF.REPEATFIELD', () => {
    it('returns single value at index', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore();
      registerRfFormulas(api, { dataStore: store });

      expect(api.callFunction('RF.REPEATFIELD', 'objective', 1, 'key_results', 0, 'key_result')).toBe('Increase revenue');
    });

    it('returns value at second index', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore();
      registerRfFormulas(api, { dataStore: store });

      expect(api.callFunction('RF.REPEATFIELD', 'objective', 1, 'key_results', 1, 'achieved')).toBe(false);
    });

    it('returns #NOT_FOUND for out-of-bounds index', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore();
      registerRfFormulas(api, { dataStore: store });

      expect(api.callFunction('RF.REPEATFIELD', 'objective', 1, 'key_results', 99, 'key_result')).toBe('#NOT_FOUND');
    });

    it('returns #VALUE! for negative index', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore();
      registerRfFormulas(api, { dataStore: store });

      expect(api.callFunction('RF.REPEATFIELD', 'objective', 1, 'key_results', -1, 'key_result')).toBe('#VALUE!');
    });

    it('floors float index', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore();
      registerRfFormulas(api, { dataStore: store });

      // 0.9 floors to 0 → first element
      expect(api.callFunction('RF.REPEATFIELD', 'objective', 1, 'key_results', 0.9, 'key_result')).toBe('Increase revenue');
      // 1.7 floors to 1 → second element
      expect(api.callFunction('RF.REPEATFIELD', 'objective', 1, 'key_results', 1.7, 'key_result')).toBe('Reduce churn');
    });

    it('returns #VALUE! for NaN index', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore();
      registerRfFormulas(api, { dataStore: store });

      expect(api.callFunction('RF.REPEATFIELD', 'objective', 1, 'key_results', 'abc', 'key_result')).toBe('#VALUE!');
    });

    it('returns N/A for unauthorized entity', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore({ unauthorizedEntities: new Set(['secret']) });
      registerRfFormulas(api, { dataStore: store });

      expect(api.callFunction('RF.REPEATFIELD', 'secret', 1, 'items', 0, 'name')).toBe('N/A');
    });

    it('returns #NOT_FOUND for missing entity id', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore();
      registerRfFormulas(api, { dataStore: store });

      expect(api.callFunction('RF.REPEATFIELD', 'objective', 999, 'key_results', 0, 'key_result')).toBe('#NOT_FOUND');
    });

    it('returns #VALUE! for missing arguments', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore();
      registerRfFormulas(api, { dataStore: store });

      expect(api.callFunction('RF.REPEATFIELD', 'objective', 1, 'key_results', 0)).toBe('#VALUE!');
      expect(api.callFunction('RF.REPEATFIELD')).toBe('#VALUE!');
    });

    it('accesses nested repeater via bracket path', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore();
      registerRfFormulas(api, { dataStore: store });

      expect(api.callFunction('RF.REPEATFIELD', 'survey', 1, 'sections[0].questions', 0, 'question_text')).toBe('Name?');
      expect(api.callFunction('RF.REPEATFIELD', 'survey', 1, 'sections[0].questions', 1, 'question_text')).toBe('Age?');
    });

    it('returns #FIELD_REMOVED for missing sub-field', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore();
      registerRfFormulas(api, { dataStore: store });

      expect(api.callFunction('RF.REPEATFIELD', 'objective', 1, 'key_results', 0, 'nonexistent')).toBe('#FIELD_REMOVED');
    });

    it('returns [complex] when sub-field resolves to object', () => {
      const api = createMockUniverAPI();
      const store = createMockDataStore();
      registerRfFormulas(api, { dataStore: store });

      expect(api.callFunction('RF.REPEATFIELD', 'objective', 1, 'key_results', 0, 'key_result_comments')).toBe('[complex]');
    });
  });
});
