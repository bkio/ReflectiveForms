import { describe, it, expect } from 'vitest';
import { schemaToZod } from '../lib/schemaToZod';
import { EntitySchema, FieldSchema } from '../types/schema';

const createMockSchema = (fields: FieldSchema[]): EntitySchema => ({
  entity_name: 'TestEntity',
  readable_name: {
    singular: 'Test Entity',
    plural: 'Test Entities',
  },
  features: {
    has_author: false,
    has_tags: false,
    has_categories: false,
    has_parent_child: false,
    require_title_uniqueness: false,
  },
  fields,
});

describe('schemaToZod', () => {
  it('should create a schema with required title', () => {
    const entitySchema = createMockSchema([]);
    const zodSchema = schemaToZod(entitySchema);

    const result = zodSchema.safeParse({
      title: { rendered: '' },
      fields: {},
    });

    expect(result.success).toBe(false);
  });

  it('should validate a valid title', () => {
    const entitySchema = createMockSchema([]);
    const zodSchema = schemaToZod(entitySchema);

    const result = zodSchema.safeParse({
      title: { rendered: 'Valid Title' },
      fields: {},
    });

    expect(result.success).toBe(true);
  });

  it('should validate text fields', () => {
    const entitySchema = createMockSchema([
      {
        name: 'description',
        type: 'Text',
        label: 'Description',
        required: true,
      } as FieldSchema,
    ]);

    const zodSchema = schemaToZod(entitySchema);

    // Valid
    const validResult = zodSchema.safeParse({
      title: { rendered: 'Test' },
      fields: { description: 'A description' },
    });
    expect(validResult.success).toBe(true);

    // Invalid - empty required field
    const invalidResult = zodSchema.safeParse({
      title: { rendered: 'Test' },
      fields: { description: '' },
    });
    expect(invalidResult.success).toBe(false);
  });

  it('should validate number fields with min/max', () => {
    const entitySchema = createMockSchema([
      {
        name: 'age',
        type: 'Number',
        label: 'Age',
        required: true,
        number_options: {
          min: 0,
          max: 150,
        },
      } as FieldSchema,
    ]);

    const zodSchema = schemaToZod(entitySchema);

    // Valid
    const validResult = zodSchema.safeParse({
      title: { rendered: 'Test' },
      fields: { age: 25 },
    });
    expect(validResult.success).toBe(true);

    // Invalid - below min
    const belowMinResult = zodSchema.safeParse({
      title: { rendered: 'Test' },
      fields: { age: -1 },
    });
    expect(belowMinResult.success).toBe(false);

    // Invalid - above max
    const aboveMaxResult = zodSchema.safeParse({
      title: { rendered: 'Test' },
      fields: { age: 200 },
    });
    expect(aboveMaxResult.success).toBe(false);
  });

  it('should validate checkbox fields', () => {
    const entitySchema = createMockSchema([
      {
        name: 'accepted',
        type: 'Checkbox',
        label: 'Accept Terms',
        required: false,
      } as FieldSchema,
    ]);

    const zodSchema = schemaToZod(entitySchema);

    const result = zodSchema.safeParse({
      title: { rendered: 'Test' },
      fields: { accepted: true },
    });
    expect(result.success).toBe(true);
  });

  it('should validate select fields with choices', () => {
    const entitySchema = createMockSchema([
      {
        name: 'status',
        type: 'Select',
        label: 'Status',
        required: true,
        select_options: {
          choices: [
            { value: 'active', label: 'Active' },
            { value: 'inactive', label: 'Inactive' },
          ],
        },
      } as FieldSchema,
    ]);

    const zodSchema = schemaToZod(entitySchema);

    // Valid
    const validResult = zodSchema.safeParse({
      title: { rendered: 'Test' },
      fields: { status: 'active' },
    });
    expect(validResult.success).toBe(true);

    // Invalid - not in choices
    const invalidResult = zodSchema.safeParse({
      title: { rendered: 'Test' },
      fields: { status: 'unknown' },
    });
    expect(invalidResult.success).toBe(false);
  });

  it('should include parent field when has_parent_child is true', () => {
    const entitySchema: EntitySchema = {
      ...createMockSchema([]),
      features: {
        ...createMockSchema([]).features,
        has_parent_child: true,
      },
    };

    const zodSchema = schemaToZod(entitySchema);

    const result = zodSchema.safeParse({
      title: { rendered: 'Test' },
      fields: {},
      parent: 123,
    });
    expect(result.success).toBe(true);
  });

  it('should include tags field when has_tags is true', () => {
    const entitySchema: EntitySchema = {
      ...createMockSchema([]),
      features: {
        ...createMockSchema([]).features,
        has_tags: true,
      },
    };

    const zodSchema = schemaToZod(entitySchema);

    const result = zodSchema.safeParse({
      title: { rendered: 'Test' },
      fields: {},
      tags: [1, 2, 3],
    });
    expect(result.success).toBe(true);
  });
});
