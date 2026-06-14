import { describe, it, expect } from 'vitest';
import { generateDefaults } from '../lib/schemaToZod';
import { EntitySchema, FieldSchema } from '../types/schema';

const createMockSchema = (fields: FieldSchema[]): EntitySchema => ({
  entity_name: 'TestEntity',
  readable_name: { singular: 'Test Entity', plural: 'Test Entities' },
  features: {
    has_author: false,
    has_tags: false,
    has_categories: false,
    has_parent_child: false,
    require_title_uniqueness: false,
    supports_frontend_edit: true,
    show_in_navigation: true,
    has_individual_sharing: false,
    supports_semantic_search: false,
    supports_ai_generation: false,
    supports_ai_diff_summary: false,
    supports_natural_language_filter: false,
  },
  fields,
  api_endpoints: {
    crud: '/rf/api/crud',
    sanity_check: '/rf/api/sanity_check',
    entity_lock: '/rf/api/entity_lock',
    media: '/rf/api/media',
  },
  schema_version: '1.0.0',
});

function makeField(overrides: Partial<FieldSchema>): FieldSchema {
  return {
    name: 'test_field',
    type: 'Text',
    label: 'Test Field',
    required: false,
    has_dynamic_choices_runtime: false,
    has_dynamic_choices_compile_time: false,
    has_logic_sanity_check: false,
    ...overrides,
  };
}

describe('DynamicDefaultValue via schema defaults', () => {
  it('should use default_value from schema for Text field', () => {
    const schema = createMockSchema([
      makeField({ name: 'greeting', type: 'Text', default_value: 'Hello World' }),
    ]);
    const defaults = generateDefaults(schema);
    const fields = defaults.fields as Record<string, unknown>;
    expect(fields.greeting).toBe('Hello World');
  });

  it('should use dynamic default_value for DatePicker field', () => {
    const schema = createMockSchema([
      makeField({ name: 'start_date', type: 'DatePicker', default_value: '20260329' }),
    ]);
    const defaults = generateDefaults(schema);
    const fields = defaults.fields as Record<string, unknown>;
    expect(fields.start_date).toBe('2026-03-29');
  });

  it('should use dynamic default_value for Number field', () => {
    const schema = createMockSchema([
      makeField({
        name: 'quantity',
        type: 'Number',
        default_value: 42,
        number_options: { min: 0, max: 100, is_range: false },
      }),
    ]);
    const defaults = generateDefaults(schema);
    const fields = defaults.fields as Record<string, unknown>;
    expect(fields.quantity).toBe(42);
  });

  it('should use dynamic default_value for Select field', () => {
    const schema = createMockSchema([
      makeField({
        name: 'status',
        type: 'Select',
        default_value: 'active',
        select_options: {
          choices: [
            { value: 'draft', label: 'Draft' },
            { value: 'active', label: 'Active' },
          ],
          allow_multiple: false,
        },
      }),
    ]);
    const defaults = generateDefaults(schema);
    const fields = defaults.fields as Record<string, unknown>;
    expect(fields.status).toBe('active');
  });

  it('should use dynamic default_value for Checkbox field', () => {
    const schema = createMockSchema([
      makeField({ name: 'is_active', type: 'Checkbox', default_value: true }),
    ]);
    const defaults = generateDefaults(schema);
    const fields = defaults.fields as Record<string, unknown>;
    expect(fields.is_active).toBe(true);
  });

  it('should fall back to empty string when no default_value for Text', () => {
    const schema = createMockSchema([
      makeField({ name: 'empty_text', type: 'Text' }),
    ]);
    const defaults = generateDefaults(schema);
    const fields = defaults.fields as Record<string, unknown>;
    expect(fields.empty_text).toBe('');
  });

  it('should fall back to 0 when no default_value for Number', () => {
    const schema = createMockSchema([
      makeField({ name: 'empty_num', type: 'Number' }),
    ]);
    const defaults = generateDefaults(schema);
    const fields = defaults.fields as Record<string, unknown>;
    expect(fields.empty_num).toBe(0);
  });

  it('should handle multiple fields with mixed defaults', () => {
    const schema = createMockSchema([
      makeField({ name: 'start_date', type: 'DatePicker', default_value: '20260329' }),
      makeField({ name: 'end_date', type: 'DatePicker', default_value: '20260330' }),
      makeField({ name: 'title_text', type: 'Text' }),
      makeField({ name: 'count', type: 'Number', default_value: 5 }),
    ]);
    const defaults = generateDefaults(schema);
    const fields = defaults.fields as Record<string, unknown>;
    expect(fields.start_date).toBe('2026-03-29');
    expect(fields.end_date).toBe('2026-03-30');
    expect(fields.title_text).toBe('');
    expect(fields.count).toBe(5);
  });

  it('should prefer default_value over type-specific fallback for Number', () => {
    const schema = createMockSchema([
      makeField({
        name: 'price',
        type: 'Number',
        default_value: 99.99,
        number_options: { min: 10, max: 1000, is_range: false },
      }),
    ]);
    const defaults = generateDefaults(schema);
    const fields = defaults.fields as Record<string, unknown>;
    // default_value takes precedence over min
    expect(fields.price).toBe(99.99);
  });

  it('should work with Repeater containing items with defaults', () => {
    const schema = createMockSchema([
      makeField({
        name: 'items',
        type: 'Repeater',
        repeater_options: {
          item_schema: [
            makeField({ name: 'name', type: 'Text', default_value: 'Default Name' }),
            makeField({ name: 'qty', type: 'Number', default_value: 1 }),
          ],
          min_items: 1,
          max_items: 5,
          add_button_label: 'Add Item',
          use_accordion: false,
          render_style: 'Full' as const,
        },
      }),
    ]);
    const defaults = generateDefaults(schema);
    const fields = defaults.fields as Record<string, unknown>;
    const items = fields.items as Array<Record<string, unknown>>;
    expect(items).toHaveLength(1);
    expect(items[0].name).toBe('Default Name');
    expect(items[0].qty).toBe(1);
  });

  it('should work with Group containing fields with defaults', () => {
    const schema = createMockSchema([
      makeField({
        name: 'address',
        type: 'Group',
        group_options: {
          child_schema: [
            makeField({ name: 'city', type: 'Text', default_value: 'Istanbul' }),
            makeField({ name: 'zip', type: 'Text' }),
          ],
          render_style: 'Full' as const,
        },
      }),
    ]);
    const defaults = generateDefaults(schema);
    const fields = defaults.fields as Record<string, unknown>;
    const address = fields.address as Record<string, unknown>;
    expect(address.city).toBe('Istanbul');
    expect(address.zip).toBe('');
  });
});
