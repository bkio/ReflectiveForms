import { EntitySchema } from '../types/schema';

// Re-export from useEntity for convenience
export { useSchema, useAllSchemas } from './useEntity';

/**
 * Get available field types from schema
 */
export function getFieldTypes(schema: EntitySchema): string[] {
  return [...new Set(schema.fields.map(f => f.type))];
}

/**
 * Find a field by name in schema (including nested fields)
 */
export function findFieldInSchema(
  schema: EntitySchema,
  fieldName: string
): EntitySchema['fields'][0] | undefined {
  const findInFields = (fields: EntitySchema['fields']): EntitySchema['fields'][0] | undefined => {
    for (const field of fields) {
      if (field.name === fieldName) return field;

      // Check nested fields
      if (field.group_options?.child_schema) {
        const found = findInFields(field.group_options.child_schema);
        if (found) return found;
      }
      if (field.repeater_options?.item_schema) {
        const found = findInFields(field.repeater_options.item_schema);
        if (found) return found;
      }
    }
    return undefined;
  };

  return findInFields(schema.fields);
}

/**
 * Get all field paths in schema (for debugging/validation)
 */
export function getAllFieldPaths(schema: EntitySchema): string[] {
  const paths: string[] = [];

  const collectPaths = (fields: EntitySchema['fields'], prefix = '') => {
    for (const field of fields) {
      const path = prefix ? `${prefix}.${field.name}` : field.name;
      paths.push(path);

      if (field.group_options?.child_schema) {
        collectPaths(field.group_options.child_schema, path);
      }
      if (field.repeater_options?.item_schema) {
        collectPaths(field.repeater_options.item_schema, `${path}[]`);
      }
    }
  };

  collectPaths(schema.fields, 'fields');
  return paths;
}
