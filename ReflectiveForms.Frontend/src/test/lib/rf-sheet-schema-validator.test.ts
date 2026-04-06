import { describe, it, expect } from 'vitest';
import { detectStaleFields, extractRequiredFields } from '../../lib/rf-sheet-schema-validator';
import type { EntitySchema, FieldSchema } from '../../types/schema';

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

function makeSchemas(fields: string[]): Record<string, EntitySchema> {
  return {
    employee: {
      entity_name: 'employee',
      readable_name: { singular: 'Employee', plural: 'Employees' },
      features: {
        has_author: false,
        has_tags: false,
        has_categories: false,
        has_parent_child: false,
        require_title_uniqueness: false,
        supports_frontend_edit: true,
      },
      fields: fields.map((name) => fieldOf({ name })),
      api_endpoints: { crud: '', sanity_check: '', entity_lock: '', media: '' },
      schema_version: '1.0',
    },
  };
}

function makeSnapshot(formulas: string[]): Record<string, unknown> {
  const cellData: Record<number, Record<number, { f: string }>> = {};
  formulas.forEach((f, i) => {
    cellData[i] = { 0: { f } };
  });
  return {
    sheets: {
      sheet1: { name: 'Sheet1', cellData },
    },
  };
}

describe('detectStaleFields', () => {
  it('returns empty array when all referenced fields exist', () => {
    const schemas = makeSchemas(['name', 'email']);
    const snapshot = makeSnapshot([
      '=RF.LIST("employee", "name")',
      '=RF.FIELD("employee", 1, "email")',
    ]);
    expect(detectStaleFields(snapshot, schemas)).toEqual([]);
  });

  it('detects missing RF.LIST field', () => {
    const schemas = makeSchemas(['name']);
    const snapshot = makeSnapshot(['=RF.LIST("employee", "deleted_field")']);
    const stale = detectStaleFields(snapshot, schemas);
    expect(stale).toEqual([{ entity: 'employee', field: 'deleted_field' }]);
  });

  it('detects missing RF.FIELD field', () => {
    const schemas = makeSchemas(['name']);
    const snapshot = makeSnapshot(['=RF.FIELD("employee", 1, "removed")']);
    const stale = detectStaleFields(snapshot, schemas);
    expect(stale).toEqual([{ entity: 'employee', field: 'removed' }]);
  });

  it('detects missing RF.SUM field', () => {
    const schemas = makeSchemas(['name']);
    const snapshot = makeSnapshot(['=RF.SUM("employee", "old_salary")']);
    const stale = detectStaleFields(snapshot, schemas);
    expect(stale).toEqual([{ entity: 'employee', field: 'old_salary' }]);
  });

  it('detects missing RF.AVG field', () => {
    const schemas = makeSchemas(['name']);
    const snapshot = makeSnapshot(['=RF.AVG("employee", "old_metric")']);
    const stale = detectStaleFields(snapshot, schemas);
    expect(stale).toEqual([{ entity: 'employee', field: 'old_metric' }]);
  });

  it('detects both matchField and returnField from RF.LOOKUP', () => {
    const schemas = makeSchemas(['name']);
    const snapshot = makeSnapshot(['=RF.LOOKUP("employee", "dept_id", 1, "dept_name")']);
    const stale = detectStaleFields(snapshot, schemas);
    expect(stale).toHaveLength(2);
    expect(stale).toContainEqual({ entity: 'employee', field: 'dept_id' });
    expect(stale).toContainEqual({ entity: 'employee', field: 'dept_name' });
  });

  it('skips "id" and "title" as built-in fields', () => {
    const schemas = makeSchemas(['name']);
    const snapshot = makeSnapshot([
      '=RF.FIELD("employee", 1, "id")',
      '=RF.FIELD("employee", 1, "title")',
    ]);
    expect(detectStaleFields(snapshot, schemas)).toEqual([]);
  });

  it('skips unknown entities (not flagged as stale)', () => {
    const schemas = makeSchemas(['name']);
    const snapshot = makeSnapshot(['=RF.LIST("unknown_entity", "field")']);
    expect(detectStaleFields(snapshot, schemas)).toEqual([]);
  });

  it('deduplicates field references', () => {
    const schemas = makeSchemas(['name']);
    const snapshot = makeSnapshot([
      '=RF.LIST("employee", "removed")',
      '=RF.FIELD("employee", 1, "removed")',
    ]);
    const stale = detectStaleFields(snapshot, schemas);
    expect(stale).toHaveLength(1);
    expect(stale[0]).toEqual({ entity: 'employee', field: 'removed' });
  });

  it('handles empty snapshot gracefully', () => {
    const schemas = makeSchemas(['name']);
    expect(detectStaleFields({}, schemas)).toEqual([]);
  });

  it('handles multiple sheets', () => {
    const schemas = makeSchemas(['name']);
    const snapshot = {
      sheets: {
        sheet1: { cellData: { 0: { 0: { f: '=RF.LIST("employee", "deleted1")' } } } },
        sheet2: { cellData: { 0: { 0: { f: '=RF.LIST("employee", "deleted2")' } } } },
      },
    };
    const stale = detectStaleFields(snapshot, schemas);
    expect(stale).toHaveLength(2);
  });

  it('ignores cells without formulas', () => {
    const schemas = makeSchemas(['name']);
    const snapshot = {
      sheets: {
        sheet1: {
          cellData: {
            0: { 0: { v: 'just a value' } },
            1: { 0: { f: '=RF.LIST("employee", "name")' } },
          },
        },
      },
    };
    expect(detectStaleFields(snapshot, schemas)).toEqual([]);
  });
});

// ── extractRequiredFields ──────────────────────────────────────────────

describe('extractRequiredFields', () => {
  it('extracts fields from RF.LIST formulas', () => {
    const snapshot = {
      sheets: {
        sheet1: {
          cellData: {
            0: { 0: { f: '=RF.LIST("employee", "name")' } },
            1: { 0: { f: '=RF.LIST("employee", "salary")' } },
          },
        },
      },
    };
    const result = extractRequiredFields(snapshot);
    expect(result.get('employee')).toBeDefined();
    const fields = result.get('employee')!;
    expect(fields.has('name')).toBe(true);
    expect(fields.has('salary')).toBe(true);
    expect(fields.has('title')).toBe(true); // always included
  });

  it('extracts fields from RF.FIELD formulas', () => {
    const snapshot = {
      sheets: {
        sheet1: {
          cellData: {
            0: { 0: { f: '=RF.FIELD("department", 1, "dept_name")' } },
          },
        },
      },
    };
    const result = extractRequiredFields(snapshot);
    expect(result.get('department')!.has('dept_name')).toBe(true);
  });

  it('extracts fields from RF.SUM and RF.AVG', () => {
    const snapshot = {
      sheets: {
        sheet1: {
          cellData: {
            0: { 0: { f: '=RF.SUM("employee", "salary")' } },
            1: { 0: { f: '=RF.AVG("employee", "rating")' } },
          },
        },
      },
    };
    const result = extractRequiredFields(snapshot);
    const fields = result.get('employee')!;
    expect(fields.has('salary')).toBe(true);
    expect(fields.has('rating')).toBe(true);
  });

  it('extracts fields from RF.LOOKUP formulas', () => {
    const snapshot = {
      sheets: {
        sheet1: {
          cellData: {
            0: { 0: { f: '=RF.LOOKUP("employee", "department_id", 10, "name")' } },
          },
        },
      },
    };
    const result = extractRequiredFields(snapshot);
    const fields = result.get('employee')!;
    expect(fields.has('department_id')).toBe(true);
    expect(fields.has('name')).toBe(true);
  });

  it('extracts fields from RF.FILTER formulas', () => {
    const snapshot = {
      sheets: {
        sheet1: {
          cellData: {
            0: { 0: { f: '=RF.FILTER("employee", "name", "department_id", 10)' } },
          },
        },
      },
    };
    const result = extractRequiredFields(snapshot);
    const fields = result.get('employee')!;
    expect(fields.has('name')).toBe(true);
    expect(fields.has('department_id')).toBe(true);
  });

  it('extracts fields from RF.MATCH formulas', () => {
    const snapshot = {
      sheets: {
        sheet1: {
          cellData: {
            0: { 0: { f: '=RF.MATCH("employee", 1, "salary", ">", 50000)' } },
          },
        },
      },
    };
    const result = extractRequiredFields(snapshot);
    expect(result.get('employee')!.has('salary')).toBe(true);
  });

  it('extracts fields from RF.MATCHLIST formulas', () => {
    const snapshot = {
      sheets: {
        sheet1: {
          cellData: {
            0: { 0: { f: '=RF.MATCHLIST("employee", "salary", ">", 50000)' } },
          },
        },
      },
    };
    const result = extractRequiredFields(snapshot);
    expect(result.get('employee')!.has('salary')).toBe(true);
  });

  it('extracts entity names from RF.IDS and RF.COUNT without fields', () => {
    const snapshot = {
      sheets: {
        sheet1: {
          cellData: {
            0: { 0: { f: '=RF.IDS("employee")' } },
            1: { 0: { f: '=RF.COUNT("employee")' } },
          },
        },
      },
    };
    const result = extractRequiredFields(snapshot);
    // Should still register the entity with at least "title"
    expect(result.get('employee')).toBeDefined();
    expect(result.get('employee')!.has('title')).toBe(true);
  });

  it('handles multiple entities across sheets', () => {
    const snapshot = {
      sheets: {
        sheet1: {
          cellData: {
            0: { 0: { f: '=RF.LIST("employee", "name")' } },
          },
        },
        sheet2: {
          cellData: {
            0: { 0: { f: '=RF.LIST("department", "dept_name")' } },
          },
        },
      },
    };
    const result = extractRequiredFields(snapshot);
    expect(result.size).toBe(2);
    expect(result.get('employee')!.has('name')).toBe(true);
    expect(result.get('department')!.has('dept_name')).toBe(true);
  });

  it('returns empty map for snapshot with no formulas', () => {
    const snapshot = {
      sheets: {
        sheet1: {
          cellData: {
            0: { 0: { v: 'just text' } },
          },
        },
      },
    };
    const result = extractRequiredFields(snapshot);
    expect(result.size).toBe(0);
  });

  it('returns empty map for empty snapshot', () => {
    expect(extractRequiredFields({})).toEqual(new Map());
    expect(extractRequiredFields(null as unknown)).toEqual(new Map());
    expect(extractRequiredFields(undefined as unknown)).toEqual(new Map());
  });

  it('deduplicates fields across multiple cells', () => {
    const snapshot = {
      sheets: {
        sheet1: {
          cellData: {
            0: { 0: { f: '=RF.LIST("employee", "name")' } },
            1: { 0: { f: '=RF.LIST("employee", "name")' } },
            2: { 0: { f: '=RF.SUM("employee", "name")' } },
          },
        },
      },
    };
    const result = extractRequiredFields(snapshot);
    // name + title
    expect(result.get('employee')!.size).toBe(2);
  });

  // ── Dot-path normalization ──────────────────────────────────────────

  it('normalizes dot-path in RF.FIELD to top-level field', () => {
    const snapshot = makeSnapshot(['=RF.FIELD("event", 1, "venue.venue_address.city")']);
    const result = extractRequiredFields(snapshot);
    expect(result.get('event')!.has('venue')).toBe(true);
    // Should NOT have the full path as a field
    expect(result.get('event')!.has('venue.venue_address.city')).toBe(false);
  });

  it('normalizes dot-path in RF.LIST to top-level field', () => {
    const snapshot = makeSnapshot(['=RF.LIST("event", "venue.address.city")']);
    const result = extractRequiredFields(snapshot);
    expect(result.get('event')!.has('venue')).toBe(true);
  });

  it('normalizes bracket-path in RF.FIELD to top-level field', () => {
    const snapshot = makeSnapshot(['=RF.FIELD("survey", 1, "sections[0].questions")']);
    const result = extractRequiredFields(snapshot);
    expect(result.get('survey')!.has('sections')).toBe(true);
  });

  // ── RF.REPEAT extraction ──────────────────────────────────────────────

  it('extracts field from RF.REPEAT', () => {
    const snapshot = makeSnapshot(['=RF.REPEAT("objective", 1, "key_results", "key_result")']);
    const result = extractRequiredFields(snapshot);
    expect(result.get('objective')!.has('key_results')).toBe(true);
  });

  it('extracts field from RF.REPEATCOUNT', () => {
    const snapshot = makeSnapshot(['=RF.REPEATCOUNT("objective", 1, "key_results")']);
    const result = extractRequiredFields(snapshot);
    expect(result.get('objective')!.has('key_results')).toBe(true);
  });

  it('extracts field from RF.REPEATFIELD', () => {
    const snapshot = makeSnapshot(['=RF.REPEATFIELD("objective", 1, "key_results", 0, "key_result")']);
    const result = extractRequiredFields(snapshot);
    expect(result.get('objective')!.has('key_results')).toBe(true);
  });

  it('normalizes bracket-path in RF.REPEAT to top-level', () => {
    const snapshot = makeSnapshot(['=RF.REPEAT("survey", 1, "sections[0].questions", "question_text")']);
    const result = extractRequiredFields(snapshot);
    expect(result.get('survey')!.has('sections')).toBe(true);
    expect(result.get('survey')!.has('sections[0].questions')).toBe(false);
  });

  it('normalizes wildcard-path in RF.REPEAT to top-level', () => {
    const snapshot = makeSnapshot(['=RF.REPEAT("survey", 1, "sections[*].questions", "question_text")']);
    const result = extractRequiredFields(snapshot);
    expect(result.get('survey')!.has('sections')).toBe(true);
  });

  it('deduplicates across old and new function types', () => {
    const snapshot = makeSnapshot([
      '=RF.FIELD("objective", 1, "key_results[0].key_result")',
      '=RF.REPEAT("objective", 1, "key_results", "key_result")',
    ]);
    const result = extractRequiredFields(snapshot);
    // key_results + title (auto-added)
    expect(result.get('objective')!.has('key_results')).toBe(true);
    expect(result.get('objective')!.has('title')).toBe(true);
  });

  it('handles multiple new function types in same formula', () => {
    const snapshot = {
      sheets: {
        sheet1: {
          cellData: {
            0: { 0: { f: '=RF.REPEAT("survey", 1, "sections", "section_title")' } },
            1: { 0: { f: '=RF.REPEATCOUNT("survey", 1, "sections")' } },
            2: { 0: { f: '=RF.REPEATFIELD("survey", 1, "sections", 0, "section_title")' } },
          },
        },
      },
    };
    const result = extractRequiredFields(snapshot);
    expect(result.get('survey')!.has('sections')).toBe(true);
    expect(result.get('survey')!.has('title')).toBe(true);
  });

  it('existing flat field extraction is unchanged', () => {
    const snapshot = makeSnapshot([
      '=RF.LIST("employee", "name")',
      '=RF.SUM("employee", "salary")',
    ]);
    const result = extractRequiredFields(snapshot);
    expect(result.get('employee')!.has('name')).toBe(true);
    expect(result.get('employee')!.has('salary')).toBe(true);
    expect(result.get('employee')!.has('title')).toBe(true);
  });
});

// ── detectStaleFields with paths ──────────────────────────────────────

describe('detectStaleFields — path normalization', () => {
  it('dot-path with existing top-level field is not stale', () => {
    const schemas = makeSchemas(['venue', 'name']);
    const snapshot = makeSnapshot(['=RF.FIELD("employee", 1, "venue.address.city")']);
    // "venue" exists in schema → not stale
    expect(detectStaleFields(snapshot, schemas)).toEqual([]);
  });

  it('dot-path with missing top-level field is stale', () => {
    const schemas = makeSchemas(['name']);
    const snapshot = makeSnapshot(['=RF.FIELD("employee", 1, "removed_group.sub")']);
    const stale = detectStaleFields(snapshot, schemas);
    expect(stale).toEqual([{ entity: 'employee', field: 'removed_group' }]);
  });

  it('bracket-path normalizes to top-level for stale check', () => {
    const schemas = makeSchemas(['name']);
    const snapshot = makeSnapshot(['=RF.REPEAT("employee", 1, "sections[0].questions", "text")']);
    const stale = detectStaleFields(snapshot, schemas);
    expect(stale).toEqual([{ entity: 'employee', field: 'sections' }]);
  });

  it('RF.REPEAT with existing field is not stale', () => {
    const schemas = makeSchemas(['key_results']);
    const snapshot = makeSnapshot(['=RF.REPEAT("employee", 1, "key_results", "key_result")']);
    expect(detectStaleFields(snapshot, schemas)).toEqual([]);
  });

  it('RF.REPEATCOUNT with missing field is stale', () => {
    const schemas = makeSchemas(['name']);
    const snapshot = makeSnapshot(['=RF.REPEATCOUNT("employee", 1, "removed_repeater")']);
    const stale = detectStaleFields(snapshot, schemas);
    expect(stale).toEqual([{ entity: 'employee', field: 'removed_repeater' }]);
  });

  it('RF.REPEATFIELD with existing field is not stale', () => {
    const schemas = makeSchemas(['key_results']);
    const snapshot = makeSnapshot(['=RF.REPEATFIELD("employee", 1, "key_results", 0, "key_result")']);
    expect(detectStaleFields(snapshot, schemas)).toEqual([]);
  });
});
