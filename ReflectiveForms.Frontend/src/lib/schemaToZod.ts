import { z, ZodTypeAny } from 'zod';
import { FieldSchema, EntitySchema } from '../types/schema';

/**
 * Converts a JSON schema from the backend into a Zod validation schema.
 * This allows the frontend to perform the same validation as the backend.
 */
export function schemaToZod(entitySchema: EntitySchema): z.ZodObject<Record<string, ZodTypeAny>> {
  const shape: Record<string, ZodTypeAny> = {};

  // Title is always required
  shape.title = z.object({
    rendered: z.string().min(1, 'Title is required').max(256, 'Title must be 256 characters or less'),
  });

  // Build proper field-level validation from the entity schema.
  shape.fields = z.object(buildFieldsShape(entitySchema.fields));

  // Optional entity features
  if (entitySchema.features.has_parent_child) {
    shape.parent = z.number().optional();
  }

  if (entitySchema.features.has_author) {
    shape.author = z.number().optional();
  }

  if (entitySchema.features.has_tags) {
    shape.tags = z.array(z.number()).optional();
  }

  if (entitySchema.features.has_categories) {
    shape.categories = z.array(z.number()).optional();
  }

  return z.object(shape);
}

function buildFieldsShape(fields: FieldSchema[], parentHasCondition = false): Record<string, ZodTypeAny> {
  const shape: Record<string, ZodTypeAny> = {};

  for (const field of fields) {
    shape[field.name] = fieldToZod(field, parentHasCondition);
  }

  return shape;
}

function fieldToZod(field: FieldSchema, parentHasCondition = false): ZodTypeAny {
  let schema: ZodTypeAny;

  // Fields controlled by display conditions (own or inherited from parent group)
  // may be hidden, so treat them as optional in client-side validation.
  // The backend enforces mandatory rules with full context.
  const hasCondition = !!field.display_condition || parentHasCondition;
  const isRequired = field.required && !hasCondition;

  switch (field.type) {
    case 'Text':
    case 'TextArea':
    case 'WysiwygEditor':
    case 'Email':
    case 'Url':
      schema = z.string();
      if (isRequired) {
        schema = (schema as z.ZodString).min(1, `${field.label} is required`);
      }
      if (field.type === 'Email') {
        if (isRequired) {
          schema = (schema as z.ZodString).email('Invalid email address');
        }
      }
      if (field.type === 'Url') {
        if (isRequired) {
          schema = (schema as z.ZodString).url('Invalid URL');
        }
      }
      if (field.text_options?.max_length) {
        schema = (schema as z.ZodString).max(
          field.text_options.max_length,
          `Maximum ${field.text_options.max_length} characters`
        );
      }
      break;

    case 'Number':
    case 'Range':
      schema = z.number();
      if (field.number_options?.min !== undefined) {
        schema = (schema as z.ZodNumber).min(field.number_options.min);
      }
      if (field.number_options?.max !== undefined) {
        schema = (schema as z.ZodNumber).max(field.number_options.max);
      }
      break;

    case 'Checkbox':
      schema = z.boolean();
      break;

    case 'Select':
      if (field.select_options?.choices?.length) {
        const values = field.select_options.choices.map((c) => c.value);
        // Allow empty string for optional selects (the backend validates choices)
        schema = z.enum([...values, ''] as unknown as [string, ...string[]]);
      } else {
        schema = z.string();
      }
      break;

    case 'DatePicker':
      schema = z.string();
      if (isRequired) {
        schema = (schema as z.ZodString).min(1, `${field.label} is required`);
      }
      break;

    case 'Relation':
      // Allow -1 as a valid "no selection" value (backend interprets this as null)
      schema = z.number();
      if (isRequired && !field.relation_options?.is_relation_entity_not_exists_ok) {
        schema = (schema as z.ZodNumber).positive('Please select a valid option');
      }
      break;

    case 'Group':
      if (field.group_options?.child_schema) {
        const childShape = buildFieldsShape(field.group_options.child_schema, hasCondition);
        schema = z.object(childShape);
      } else {
        schema = z.object({});
      }
      break;

    case 'Repeater':
      if (field.repeater_options?.item_schema) {
        const itemShape = buildFieldsShape(field.repeater_options.item_schema, hasCondition);
        let arraySchema = z.array(z.object(itemShape));

        if (field.repeater_options.min_items !== undefined) {
          arraySchema = arraySchema.min(
            field.repeater_options.min_items,
            `At least ${field.repeater_options.min_items} items required`
          );
        }
        if (field.repeater_options.max_items !== undefined) {
          arraySchema = arraySchema.max(
            field.repeater_options.max_items,
            `Maximum ${field.repeater_options.max_items} items allowed`
          );
        }
        schema = arraySchema;
      } else {
        schema = z.array(z.object({}));
      }
      break;

    case 'MediaSourceBase64':
      schema = z.string();
      break;

    default:
      schema = z.unknown();
  }

  // Make optional if not required
  if (!isRequired) {
    schema = schema.optional();
  }

  return schema;
}

/**
 * Generate default values based on schema
 */
export function generateDefaults(entitySchema: EntitySchema): Record<string, unknown> {
  const defaults: Record<string, unknown> = {
    id: -1,
    title: { rendered: '' },
    fields: generateFieldDefaults(entitySchema.fields),
  };

  if (entitySchema.features.has_tags) {
    defaults.tags = [];
  }
  if (entitySchema.features.has_categories) {
    defaults.categories = [];
  }
  if (entitySchema.features.has_parent_child) {
    defaults.parent = -1;
  }
  if (entitySchema.features.has_author) {
    defaults.author = -1;
  }

  return defaults;
}

function generateFieldDefaults(fields: FieldSchema[]): Record<string, unknown> {
  const defaults: Record<string, unknown> = {};

  for (const field of fields) {
    defaults[field.name] = getFieldDefault(field);
  }

  return defaults;
}

function getFieldDefault(field: FieldSchema): unknown {
  // Use provided default if available
  if (field.default_value !== undefined && field.default_value !== null) {
    // Convert yyyyMMdd to yyyy-MM-dd for DatePicker so HTML date input works
    if (field.type === 'DatePicker' && typeof field.default_value === 'string' && /^\d{8}$/.test(field.default_value)) {
      const s = field.default_value;
      return `${s.slice(0, 4)}-${s.slice(4, 6)}-${s.slice(6, 8)}`;
    }
    return field.default_value;
  }

  switch (field.type) {
    case 'Text':
    case 'TextArea':
    case 'WysiwygEditor':
    case 'Email':
    case 'Url':
    case 'DatePicker':
    case 'MediaSourceBase64':
      return '';

    case 'Number':
    case 'Range':
      return field.number_options?.min ?? 0;

    case 'Checkbox':
      return false;

    case 'Select':
      return field.select_options?.choices?.[0]?.value ?? '';

    case 'Relation':
      return -1;

    case 'Group':
      if (field.group_options?.child_schema) {
        return generateFieldDefaults(field.group_options.child_schema);
      }
      return {};

    case 'Repeater':
      if (field.repeater_options?.min_items && field.repeater_options.min_items > 0) {
        const itemDefaults = field.repeater_options.item_schema
          ? generateFieldDefaults(field.repeater_options.item_schema)
          : {};
        return Array(field.repeater_options.min_items).fill(null).map(() => ({ ...itemDefaults }));
      }
      return [];

    default:
      return null;
  }
}
