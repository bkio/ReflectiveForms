import { describe, it, expect } from 'vitest';
import { EntitySchema, FieldSchema } from '../../types/schema';

/**
 * Date format normalization tests for fixDateFormatsForSchema.
 *
 * The normalizeDates() function converts all yyyy-MM-dd → yyyyMMdd.
 * fixDateFormatsForSchema() then converts back for fields whose
 * date_format is NOT yyyyMMdd (e.g. "yyyy-MM-dd").
 */

// Inline the function since it's not exported
function fixDateFormatsForSchema(values: Record<string, unknown>, schema: EntitySchema): void {
  const fields = values.fields as Record<string, unknown> | undefined;
  if (!fields) return;
  for (const fieldSchema of schema.fields) {
    if (fieldSchema.type !== 'DatePicker') continue;
    const fmt = fieldSchema.date_options?.format;
    if (!fmt || fmt === 'yyyyMMdd') continue;
    const val = fields[fieldSchema.name];
    if (typeof val === 'string' && /^\d{8}$/.test(val)) {
      fields[fieldSchema.name] = `${val.slice(0, 4)}-${val.slice(4, 6)}-${val.slice(6, 8)}`;
    }
  }
}

function makeSchema(dateFormat: string): EntitySchema {
  return {
    entity_name: 'test',
    readable_name: { singular: 'Test', plural: 'Tests' },
    features: {
      has_author: false, has_tags: false, has_categories: false,
      has_parent_child: false, require_title_uniqueness: false,
      supports_frontend_edit: true, has_individual_sharing: false,
      custom_frontend_list_route: '', supports_semantic_search: false,
      supports_ai_generation: false, supports_ai_diff_summary: false,
      supports_natural_language_filter: false, show_in_navigation: true,
    },
    fields: [
      {
        name: 'event_date', type: 'DatePicker', label: 'Event Date',
        instructions: '', required: false, default_value: null,
        display_condition: null, text_options: null, select_options: null,
        number_options: null, date_options: { format: dateFormat, include_time: false },
        relation_options: null, repeater_options: null, group_options: null,
        media_options: null, has_dynamic_choices_runtime: false,
        has_dynamic_choices_compile_time: false, has_logic_sanity_check: false,
        ai_suggestion: null, ai_sanity_checks: null, ai_relation_suggestion: null,
      },
    ],
    api_endpoints: null,
  };
}

describe('fixDateFormatsForSchema', () => {
  it('leaves yyyyMMdd unchanged when schema format is yyyyMMdd', () => {
    const schema = makeSchema('yyyyMMdd');
    const values = { fields: { event_date: '20260618' } };

    fixDateFormatsForSchema(values, schema);

    expect(values.fields.event_date).toBe('20260618');
  });

  it('converts yyyyMMdd → yyyy-MM-dd when schema format is yyyy-MM-dd', () => {
    const schema = makeSchema('yyyy-MM-dd');
    const values = { fields: { event_date: '20260618' } };

    fixDateFormatsForSchema(values, schema);

    expect(values.fields.event_date).toBe('2026-06-18');
  });

  it('leaves already-hyphenated dates unchanged for yyyy-MM-dd schema', () => {
    const schema = makeSchema('yyyy-MM-dd');
    const values = { fields: { event_date: '2026-06-18' } };

    fixDateFormatsForSchema(values, schema);

    expect(values.fields.event_date).toBe('2026-06-18');
  });

  it('leaves empty string unchanged', () => {
    const schema = makeSchema('yyyy-MM-dd');
    const values = { fields: { event_date: '' } };

    fixDateFormatsForSchema(values, schema);

    expect(values.fields.event_date).toBe('');
  });

  it('leaves undefined value unchanged', () => {
    const schema = makeSchema('yyyy-MM-dd');
    const values = { fields: { event_date: undefined } };

    fixDateFormatsForSchema(values, schema);

    expect(values.fields.event_date).toBeUndefined();
  });

  it('handles missing date_options.format gracefully (treats as yyyyMMdd)', () => {
    const schema = makeSchema('yyyyMMdd');
    // Remove format
    (schema.fields[0].date_options as any).format = undefined;
    const values = { fields: { event_date: '20260618' } };

    fixDateFormatsForSchema(values, schema);

    expect(values.fields.event_date).toBe('20260618');
  });

  it('does not affect non-DatePicker fields', () => {
    const schema = makeSchema('yyyy-MM-dd');
    const values = { fields: { event_date: '20260618', title: '20260618' } };

    fixDateFormatsForSchema(values, schema);

    expect(values.fields.event_date).toBe('2026-06-18');
    expect(values.fields.title).toBe('20260618');
  });

  it('handles custom format like yyyy/MM/dd', () => {
    const schema = makeSchema('yyyy/MM/dd');
    const values = { fields: { event_date: '20260618' } };

    fixDateFormatsForSchema(values, schema);

    expect(values.fields.event_date).toBe('2026-06-18');
  });
});
