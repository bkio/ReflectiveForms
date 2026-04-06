import { describe, it, expect } from 'vitest';
import { formatFieldValue, getFieldSchema, resolveNestedPath, toTopLevelField } from '../../lib/rf-sheet-formatters';
import type { FieldSchema, EntitySchema } from '../../types/schema';

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

describe('formatFieldValue', () => {
  // ── Nullish handling ──────────────────────────────────────────────────

  it('returns empty string for null', () => {
    expect(formatFieldValue(null, undefined)).toBe('');
  });

  it('returns empty string for undefined', () => {
    expect(formatFieldValue(undefined, undefined)).toBe('');
  });

  // ── Permission sentinel ───────────────────────────────────────────────

  it('converts #NO_ACCESS to N/A', () => {
    expect(formatFieldValue('#NO_ACCESS', undefined)).toBe('N/A');
  });

  it('passes through other error sentinels unchanged', () => {
    expect(formatFieldValue('#NOT_FOUND', undefined)).toBe('#NOT_FOUND');
    expect(formatFieldValue('#NO_DATA', undefined)).toBe('#NO_DATA');
    expect(formatFieldValue('#FIELD_REMOVED', undefined)).toBe('#FIELD_REMOVED');
    expect(formatFieldValue('#VALUE!', undefined)).toBe('#VALUE!');
  });

  // ── Without schema (fallback) ─────────────────────────────────────────

  it('returns primitives as-is without schema', () => {
    expect(formatFieldValue('hello', undefined)).toBe('hello');
    expect(formatFieldValue(42, undefined)).toBe(42);
    expect(formatFieldValue(true, undefined)).toBe(true);
  });

  it('returns [complex] for objects without schema', () => {
    expect(formatFieldValue({ a: 1 }, undefined)).toBe('[complex]');
  });

  // ── Checkbox ──────────────────────────────────────────────────────────

  it('formats true checkbox as ✓', () => {
    const schema = fieldOf({ type: 'Checkbox' });
    expect(formatFieldValue(true, schema)).toBe('✓');
    expect(formatFieldValue('true', schema)).toBe('✓');
    expect(formatFieldValue(1, schema)).toBe('✓');
  });

  it('formats false checkbox as ✗', () => {
    const schema = fieldOf({ type: 'Checkbox' });
    expect(formatFieldValue(false, schema)).toBe('✗');
    expect(formatFieldValue('false', schema)).toBe('✗');
    expect(formatFieldValue(0, schema)).toBe('✗');
  });

  // ── DatePicker ────────────────────────────────────────────────────────

  it('formats date string', () => {
    const schema = fieldOf({ type: 'DatePicker' });
    const result = formatFieldValue('2024-03-15T10:30:00Z', schema);
    expect(result).toContain('2024');
    expect(result).toContain('Mar');
    expect(result).toContain('15');
  });

  it('returns raw string for invalid date', () => {
    const schema = fieldOf({ type: 'DatePicker' });
    expect(formatFieldValue('not-a-date', schema)).toBe('not-a-date');
  });

  // ── Select ────────────────────────────────────────────────────────────

  it('resolves select value to label', () => {
    const schema = fieldOf({
      type: 'Select',
      select_options: {
        allow_multiple: false,
        choices: [
          { value: 'active', label: 'Active' },
          { value: 'inactive', label: 'Inactive' },
        ],
      },
    });
    expect(formatFieldValue('active', schema)).toBe('Active');
    expect(formatFieldValue('inactive', schema)).toBe('Inactive');
  });

  it('returns raw value when no matching select choice', () => {
    const schema = fieldOf({
      type: 'Select',
      select_options: {
        allow_multiple: false,
        choices: [{ value: 'a', label: 'Option A' }],
      },
    });
    expect(formatFieldValue('unknown', schema)).toBe('unknown');
  });

  it('resolves multiple select values', () => {
    const schema = fieldOf({
      type: 'Select',
      select_options: {
        allow_multiple: true,
        choices: [
          { value: 'red', label: 'Red' },
          { value: 'blue', label: 'Blue' },
        ],
      },
    });
    expect(formatFieldValue(['red', 'blue'], schema)).toBe('Red, Blue');
  });

  it('returns raw value when select has no choices', () => {
    const schema = fieldOf({
      type: 'Select',
      select_options: { allow_multiple: false, choices: [] },
    });
    expect(formatFieldValue('val', schema)).toBe('val');
  });

  // ── WysiwygEditor ─────────────────────────────────────────────────────

  it('strips HTML tags', () => {
    const schema = fieldOf({ type: 'WysiwygEditor' });
    expect(formatFieldValue('<p>Hello <b>World</b></p>', schema)).toBe('Hello World');
  });

  it('decodes HTML entities', () => {
    const schema = fieldOf({ type: 'WysiwygEditor' });
    expect(formatFieldValue('A &amp; B &lt; C', schema)).toBe('A & B < C');
  });

  it('handles &nbsp; entities', () => {
    const schema = fieldOf({ type: 'WysiwygEditor' });
    expect(formatFieldValue('Hello&nbsp;World', schema)).toBe('Hello World');
  });

  // ── Number / Range ────────────────────────────────────────────────────

  it('returns number values as numbers', () => {
    const schema = fieldOf({ type: 'Number' });
    expect(formatFieldValue(42, schema)).toBe(42);
    expect(formatFieldValue('100', schema)).toBe(100);
  });

  it('returns NaN-like values as strings for Number type', () => {
    const schema = fieldOf({ type: 'Number' });
    expect(formatFieldValue('not-a-number', schema)).toBe('not-a-number');
  });

  // ── Email / Url / Text / TextArea ─────────────────────────────────────

  it('returns string types as strings', () => {
    expect(formatFieldValue('test@example.com', fieldOf({ type: 'Email' }))).toBe('test@example.com');
    expect(formatFieldValue('https://example.com', fieldOf({ type: 'Url' }))).toBe('https://example.com');
    expect(formatFieldValue('hello', fieldOf({ type: 'Text' }))).toBe('hello');
    expect(formatFieldValue('multiline\ntext', fieldOf({ type: 'TextArea' }))).toBe('multiline\ntext');
  });

  // ── Relation ──────────────────────────────────────────────────────────

  it('formats single relation', () => {
    const schema = fieldOf({ type: 'Relation' });
    expect(formatFieldValue(5, schema)).toBe('5');
  });

  it('formats multiple relations as comma-separated', () => {
    const schema = fieldOf({ type: 'Relation' });
    expect(formatFieldValue([1, 2, 3], schema)).toBe('1, 2, 3');
  });

  // ── Special types ─────────────────────────────────────────────────────

  it('shows [media] for MediaSourceBase64', () => {
    const schema = fieldOf({ type: 'MediaSourceBase64' });
    expect(formatFieldValue('base64data', schema)).toBe('[media]');
  });

  it('shows [complex] for Group and Repeater', () => {
    expect(formatFieldValue({}, fieldOf({ type: 'Group' }))).toBe('[complex]');
    expect(formatFieldValue([], fieldOf({ type: 'Repeater' }))).toBe('[complex]');
  });
});

describe('getFieldSchema', () => {
  const schemas: Record<string, EntitySchema> = {
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
      fields: [
        fieldOf({ name: 'name', type: 'Text', label: 'Name' }),
        fieldOf({ name: 'email', type: 'Email', label: 'Email' }),
        fieldOf({
          name: 'status',
          type: 'Select',
          label: 'Status',
          select_options: {
            allow_multiple: false,
            choices: [{ value: 'active', label: 'Active' }],
          },
        }),
      ],
      api_endpoints: { crud: '', sanity_check: '', entity_lock: '', media: '' },
      schema_version: '1.0',
    },
  };

  it('returns the field schema for a known entity and field', () => {
    const fs = getFieldSchema(schemas, 'employee', 'email');
    expect(fs?.type).toBe('Email');
  });

  it('returns undefined for unknown entity', () => {
    expect(getFieldSchema(schemas, 'unknown', 'field')).toBeUndefined();
  });

  it('returns undefined for unknown field', () => {
    expect(getFieldSchema(schemas, 'employee', 'nonexistent')).toBeUndefined();
  });

  it('returns undefined when schemas are undefined', () => {
    expect(getFieldSchema(undefined, 'employee', 'name')).toBeUndefined();
  });

  it('returns top-level schema for dot-path field name', () => {
    const fs = getFieldSchema(schemas, 'employee', 'name.sub.deep');
    expect(fs?.type).toBe('Text');
  });

  it('returns top-level schema for bracket-path field name', () => {
    const fs = getFieldSchema(schemas, 'employee', 'name[0].sub');
    expect(fs?.type).toBe('Text');
  });
});

// ── toTopLevelField ─────────────────────────────────────────────────────

describe('toTopLevelField', () => {
  it('returns plain field name as-is', () => {
    expect(toTopLevelField('name')).toBe('name');
  });

  it('strips dot-path to first segment', () => {
    expect(toTopLevelField('venue.address.city')).toBe('venue');
  });

  it('strips bracket notation', () => {
    expect(toTopLevelField('sections[0].questions')).toBe('sections');
  });

  it('strips wildcard notation', () => {
    expect(toTopLevelField('sections[*].questions[*].choices')).toBe('sections');
  });

  it('handles bracket before dot', () => {
    expect(toTopLevelField('items[3]')).toBe('items');
  });
});

// ── resolveNestedPath ───────────────────────────────────────────────────

describe('resolveNestedPath', () => {
  // ── Dot notation ────────────────────────────────────────────────────

  it('resolves single-level key', () => {
    expect(resolveNestedPath({ a: 1 }, 'a')).toBe(1);
  });

  it('resolves two-level dot path', () => {
    expect(resolveNestedPath({ a: { b: 2 } }, 'a.b')).toBe(2);
  });

  it('resolves three-level dot path (group-in-group)', () => {
    const obj = { venue: { venue_address: { city: 'NYC' } } };
    expect(resolveNestedPath(obj, 'venue.venue_address.city')).toBe('NYC');
  });

  it('returns undefined for missing intermediate key', () => {
    expect(resolveNestedPath({ a: {} }, 'a.b.c')).toBeUndefined();
  });

  it('returns undefined for null intermediate', () => {
    expect(resolveNestedPath({ a: null }, 'a.b')).toBeUndefined();
  });

  it('returns undefined for undefined root', () => {
    expect(resolveNestedPath(undefined, 'a')).toBeUndefined();
  });

  it('returns undefined for null root', () => {
    expect(resolveNestedPath(null, 'a')).toBeUndefined();
  });

  it('returns undefined for primitive intermediate', () => {
    expect(resolveNestedPath({ a: 'str' }, 'a.b')).toBeUndefined();
  });

  it('returns the object itself for a group path without sub-field', () => {
    const obj = { group: { x: 1 } };
    expect(resolveNestedPath(obj, 'group')).toEqual({ x: 1 });
  });

  // ── Bracket notation ──────────────────────────────────────────────────

  it('resolves array index', () => {
    expect(resolveNestedPath({ items: ['a', 'b', 'c'] }, 'items[1]')).toBe('b');
  });

  it('resolves array of objects with sub-field', () => {
    const obj = { items: [{ name: 'A' }, { name: 'B' }] };
    expect(resolveNestedPath(obj, 'items[0].name')).toBe('A');
    expect(resolveNestedPath(obj, 'items[1].name')).toBe('B');
  });

  it('returns undefined for out-of-bounds index', () => {
    expect(resolveNestedPath({ items: ['a'] }, 'items[5]')).toBeUndefined();
  });

  it('returns undefined for negative index', () => {
    // Negative indices are parsed as NaN by the tokenizer (not matching \\d+)
    // so they become invalid tokens — resolveNestedPath returns undefined
    expect(resolveNestedPath({ items: ['a'] }, 'items[-1]')).toBeUndefined();
  });

  it('returns undefined for non-numeric bracket content', () => {
    expect(resolveNestedPath({ items: ['a'] }, 'items[abc]')).toBeUndefined();
  });

  it('returns undefined when indexing into non-array', () => {
    expect(resolveNestedPath({ items: 'not-array' }, 'items[0]')).toBeUndefined();
  });

  // ── Wildcard notation ─────────────────────────────────────────────────

  it('expands all elements with wildcard', () => {
    const obj = { items: [{ n: 1 }, { n: 2 }, { n: 3 }] };
    expect(resolveNestedPath(obj, 'items[*].n')).toEqual([1, 2, 3]);
  });

  it('returns empty array for empty source array with wildcard', () => {
    expect(resolveNestedPath({ items: [] }, 'items[*].n')).toEqual([]);
  });

  it('returns undefined if field before wildcard is not array', () => {
    expect(resolveNestedPath({ items: 'str' }, 'items[*].n')).toBeUndefined();
  });

  it('returns undefined if field before wildcard is null', () => {
    expect(resolveNestedPath({ items: null }, 'items[*].n')).toBeUndefined();
  });

  it('handles nested wildcard (two levels)', () => {
    const obj = {
      a: [
        { b: [{ c: 1 }, { c: 2 }] },
        { b: [{ c: 3 }] },
      ],
    };
    expect(resolveNestedPath(obj, 'a[*].b[*].c')).toEqual([1, 2, 3]);
  });

  it('handles triple-nested wildcard (three levels)', () => {
    const obj = {
      sections: [
        { questions: [
          { choices: [{ text: 'A' }, { text: 'B' }] },
          { choices: [] },
        ]},
        { questions: [
          { choices: [{ text: 'C' }, { text: 'D' }, { text: 'E' }] },
        ]},
      ],
    };
    expect(resolveNestedPath(obj, 'sections[*].questions[*].choices[*].text'))
      .toEqual(['A', 'B', 'C', 'D', 'E']);
  });

  it('handles wildcard with missing sub-field in some elements', () => {
    const obj = { items: [{ n: 1 }, {}, { n: 3 }] };
    expect(resolveNestedPath(obj, 'items[*].n')).toEqual([1, undefined, 3]);
  });

  it('handles wildcard at end of path (returns array contents)', () => {
    const obj = { items: [1, 2, 3] };
    expect(resolveNestedPath(obj, 'items[*]')).toEqual([1, 2, 3]);
  });

  // ── Mixed bracket + wildcard ──────────────────────────────────────────

  it('indexes first then wildcards rest', () => {
    const obj = {
      sections: [
        { questions: [{ t: 'A' }, { t: 'B' }] },
        { questions: [{ t: 'C' }] },
      ],
    };
    expect(resolveNestedPath(obj, 'sections[0].questions[*].t')).toEqual(['A', 'B']);
    expect(resolveNestedPath(obj, 'sections[1].questions[*].t')).toEqual(['C']);
  });

  it('wildcards first then indexes element', () => {
    const obj = {
      sections: [
        { questions: [{ t: 'A' }, { t: 'B' }] },
        { questions: [{ t: 'C' }] },
      ],
    };
    // [*] expands sections, then [0] takes first question from each
    expect(resolveNestedPath(obj, 'sections[*].questions[0].t')).toEqual(['A', 'C']);
  });

  // ── Combination: group-in-repeater ────────────────────────────────────

  it('resolves group inside repeater row', () => {
    const obj = {
      contacts: [
        { name: 'Alice', address: { city: 'NYC', country: 'US' } },
        { name: 'Bob', address: { city: 'London', country: 'GB' } },
      ],
    };
    expect(resolveNestedPath(obj, 'contacts[0].address.city')).toBe('NYC');
    expect(resolveNestedPath(obj, 'contacts[1].address.country')).toBe('GB');
  });

  it('wildcards with group-in-repeater', () => {
    const obj = {
      contacts: [
        { address: { city: 'NYC' } },
        { address: { city: 'London' } },
      ],
    };
    expect(resolveNestedPath(obj, 'contacts[*].address.city')).toEqual(['NYC', 'London']);
  });
});

// ── formatFieldValue with nested structures ─────────────────────────────

describe('formatFieldValue — nested structure handling', () => {
  it('returns [complex] for plain object without schema', () => {
    expect(formatFieldValue({ a: 1 }, undefined)).toBe('[complex]');
  });

  it('returns [complex] for array without schema', () => {
    expect(formatFieldValue([1, 2], undefined)).toBe('[complex]');
  });

  it('returns [complex] for nested object without schema', () => {
    expect(formatFieldValue({ x: { y: 1 } }, undefined)).toBe('[complex]');
  });

  it('returns primitive leaf values normally without schema', () => {
    expect(formatFieldValue('hello', undefined)).toBe('hello');
    expect(formatFieldValue(42, undefined)).toBe(42);
    expect(formatFieldValue(true, undefined)).toBe(true);
  });

  it('returns empty string for undefined value', () => {
    expect(formatFieldValue(undefined, undefined)).toBe('');
  });

  it('returns empty string for null value', () => {
    expect(formatFieldValue(null, undefined)).toBe('');
  });
});
