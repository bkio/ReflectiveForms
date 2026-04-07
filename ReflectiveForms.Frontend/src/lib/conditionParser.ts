/**
 * Evaluates display conditions from the schema.
 *
 * Display conditions are strings like:
 * - "field_name == 'value'"
 * - "field_name != 'other_value'"
 * - "nested.field == true"
 *
 * This parser safely evaluates these conditions without using eval().
 */

type ComparisonOperator = '==' | '!=' | '>' | '<' | '>=' | '<=';

interface ParsedCondition {
  fieldPath: string;
  operator: ComparisonOperator;
  value: string | number | boolean;
}

export function evaluateCondition(
  condition: string,
  formValues: Record<string, unknown>
): boolean {
  const parsed = parseCondition(condition);
  if (!parsed) return true; // If we can't parse, show the field

  const fieldValue = getNestedValue(formValues, parsed.fieldPath);
  return compareValues(fieldValue, parsed.operator, parsed.value);
}

function parseCondition(condition: string): ParsedCondition | null {
  // Match patterns like: field_name == 'value' or field.nested == 123
  const regex = /^([\w.]+)\s*(==|!=|>=|<=|>|<)\s*(.+)$/;
  const match = condition.trim().match(regex);

  if (!match) return null;

  const [, fieldPath, operator, rawValue] = match;

  // Parse the value
  let value: string | number | boolean;
  const trimmedValue = rawValue.trim();

  if (trimmedValue === 'true') {
    value = true;
  } else if (trimmedValue === 'false') {
    value = false;
  } else if (trimmedValue === 'null') {
    value = '';
  } else if (/^['"].*['"]$/.test(trimmedValue)) {
    // String value
    value = trimmedValue.slice(1, -1);
  } else if (!isNaN(Number(trimmedValue))) {
    // Number value
    value = Number(trimmedValue);
  } else {
    // Treat as string
    value = trimmedValue;
  }

  return {
    fieldPath,
    operator: operator as ComparisonOperator,
    value,
  };
}

function getNestedValue(obj: Record<string, unknown>, path: string): unknown {
  const parts = path.split('.');
  let current: unknown = obj;

  for (const part of parts) {
    if (current === null || current === undefined) return undefined;
    if (typeof current !== 'object') return undefined;
    current = (current as Record<string, unknown>)[part];
  }

  return current;
}

function compareValues(
  fieldValue: unknown,
  operator: ComparisonOperator,
  expectedValue: string | number | boolean
): boolean {
  // Handle undefined/null field values
  if (fieldValue === undefined || fieldValue === null) {
    fieldValue = '';
  }

  switch (operator) {
    case '==':
      // eslint-disable-next-line eqeqeq
      return fieldValue == expectedValue;
    case '!=':
      // eslint-disable-next-line eqeqeq
      return fieldValue != expectedValue;
    case '>':
      return Number(fieldValue) > Number(expectedValue);
    case '<':
      return Number(fieldValue) < Number(expectedValue);
    case '>=':
      return Number(fieldValue) >= Number(expectedValue);
    case '<=':
      return Number(fieldValue) <= Number(expectedValue);
    default:
      return true;
  }
}

/**
 * Parse multiple conditions joined by && or ||
 * Example: "field1 == 'a' && field2 != 'b'"
 */
export function evaluateCompoundCondition(
  condition: string,
  formValues: Record<string, unknown>
): boolean {
  // Handle OR conditions
  if (condition.includes('||')) {
    const parts = condition.split('||').map((p) => p.trim());
    return parts.some((part) => evaluateCompoundCondition(part, formValues));
  }

  // Handle AND conditions
  if (condition.includes('&&')) {
    const parts = condition.split('&&').map((p) => p.trim());
    return parts.every((part) => evaluateCondition(part, formValues));
  }

  // Single condition
  return evaluateCondition(condition, formValues);
}
