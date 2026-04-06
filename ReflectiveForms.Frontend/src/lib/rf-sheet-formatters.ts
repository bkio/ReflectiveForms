import type { FieldSchema, EntitySchema } from '../types/schema';

/**
 * Formats a raw field value for spreadsheet display based on the field's schema type.
 *
 * - Checkbox → ✓ / ✗
 * - DatePicker → locale-formatted date string
 * - Select → resolved human-readable label (or raw value if no match)
 * - WysiwygEditor → HTML tags stripped
 * - Email / Url → returned as-is (string)
 * - Number / Range → returned as number
 * - Relation → resolved to comma-separated IDs (title resolution requires separate lookup)
 * - All others → string coercion
 */
export function formatFieldValue(
  value: unknown,
  fieldSchema: FieldSchema | undefined,
): string | number | boolean {
  if (value === null || value === undefined) return '';

  // Permission-denied sentinel → display as N/A
  if (value === '#NO_ACCESS') return 'N/A';

  // Other error sentinel values pass through unchanged
  if (typeof value === 'string' && value.startsWith('#')) {
    return value;
  }

  if (!fieldSchema) {
    return coerceToDisplayValue(value);
  }

  switch (fieldSchema.type) {
    case 'Checkbox':
      return formatCheckbox(value);
    case 'DatePicker':
      return formatDate(value, fieldSchema.date_options?.format);
    case 'Select':
      return formatSelect(value, fieldSchema);
    case 'WysiwygEditor':
      return stripHtml(value);
    case 'Number':
    case 'Range':
      return formatNumber(value);
    case 'Email':
    case 'Url':
    case 'Text':
    case 'TextArea':
      return String(value);
    case 'Relation':
      return formatRelation(value);
    case 'MediaSourceBase64':
      return '[media]';
    case 'Group':
    case 'Repeater':
      return '[complex]';
    default:
      return coerceToDisplayValue(value);
  }
}

function formatCheckbox(value: unknown): string {
  if (value === true || value === 'true' || value === 1 || value === '1') return '✓';
  if (value === false || value === 'false' || value === 0 || value === '0') return '✗';
  return String(value);
}

function formatDate(value: unknown, _format?: string): string {
  if (!value) return '';
  const str = String(value);
  const d = new Date(str);
  if (isNaN(d.getTime())) return str;
  // Use locale-aware formatting
  return d.toLocaleDateString('en-US', {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
  });
}

function formatSelect(value: unknown, fieldSchema: FieldSchema): string {
  const choices = fieldSchema.select_options?.choices;
  if (!choices || choices.length === 0) return String(value ?? '');

  // Handle multiple select (array of values)
  if (Array.isArray(value)) {
    return value
      .map((v) => {
        const choice = choices.find((c) => c.value === String(v));
        return choice ? choice.label : String(v);
      })
      .join(', ');
  }

  // Single select
  const choice = choices.find((c) => c.value === String(value));
  return choice ? choice.label : String(value ?? '');
}

function stripHtml(value: unknown): string {
  if (typeof value !== 'string') return String(value ?? '');
  // Remove HTML tags and decode common entities
  return value
    .replace(/<[^>]*>/g, '')
    .replace(/&amp;/g, '&')
    .replace(/&lt;/g, '<')
    .replace(/&gt;/g, '>')
    .replace(/&quot;/g, '"')
    .replace(/&#39;/g, "'")
    .replace(/&nbsp;/g, ' ')
    .trim();
}

function formatNumber(value: unknown): number | string {
  if (typeof value === 'number') return value;
  const num = Number(value);
  return isNaN(num) ? String(value) : num;
}

function formatRelation(value: unknown): string {
  if (Array.isArray(value)) {
    return value.map(String).join(', ');
  }
  return String(value ?? '');
}

function coerceToDisplayValue(value: unknown): string | number | boolean {
  if (typeof value === 'string' || typeof value === 'number' || typeof value === 'boolean') {
    return value;
  }
  // Objects and arrays that reach here are unresolved nested structures
  if (typeof value === 'object' && value !== null) {
    return '[complex]';
  }
  return JSON.stringify(value);
}

/**
 * Gets the FieldSchema for a given entity/field from the schemas map.
 * For dot-notation paths like "venue.address.city", only the top-level
 * segment "venue" is matched against the schema.
 */
export function getFieldSchema(
  schemas: Record<string, EntitySchema> | undefined,
  entityName: string,
  fieldName: string,
): FieldSchema | undefined {
  if (!schemas) return undefined;
  const entitySchema = schemas[entityName];
  if (!entitySchema) return undefined;
  const topLevel = toTopLevelField(fieldName);
  return entitySchema.fields.find((f) => f.name === topLevel);
}

/**
 * Extracts the top-level field name from a potentially nested path.
 * "venue.address.city" → "venue"
 * "sections[0].questions" → "sections"
 * "sections[*].questions[*].choices" → "sections"
 * "name" → "name"
 */
export function toTopLevelField(path: string): string {
  return path.split('.')[0].split('[')[0];
}

/**
 * Resolves a dot/bracket/wildcard path into a nested object.
 *
 * Supported syntax:
 * - Dot notation: "a.b.c" → obj.a.b.c
 * - Bracket index: "items[0].name" → obj.items[0].name
 * - Wildcard: "items[*].name" → flattened array of obj.items[i].name for all i
 * - Nested wildcards: "a[*].b[*].c" → flattened across all levels
 *
 * Returns undefined if any segment is missing/null/non-object.
 * Returns an array when wildcards are used.
 */
export function resolveNestedPath(obj: unknown, path: string): unknown {
  return resolvePath(obj, tokenizePath(path));
}

/** Tokenize a path string into segments. */
function tokenizePath(path: string): PathToken[] {
  const tokens: PathToken[] = [];
  // Split on dots, but keep bracket expressions attached to the preceding segment
  // e.g. "sections[0].questions[*].text" → ["sections", "[0]", "questions", "[*]", "text"]
  const re = /([^.[\]]+)|\[(\d+|\*)\]/g;
  let match: RegExpExecArray | null;
  while ((match = re.exec(path)) !== null) {
    if (match[1] !== undefined) {
      tokens.push({ type: 'key', value: match[1] });
    } else if (match[2] === '*') {
      tokens.push({ type: 'wildcard' });
    } else {
      tokens.push({ type: 'index', value: Number(match[2]) });
    }
  }
  return tokens;
}

interface PathTokenKey { type: 'key'; value: string; }
interface PathTokenIndex { type: 'index'; value: number; }
interface PathTokenWildcard { type: 'wildcard'; }
type PathToken = PathTokenKey | PathTokenIndex | PathTokenWildcard;

function resolvePath(current: unknown, tokens: PathToken[]): unknown {
  for (let i = 0; i < tokens.length; i++) {
    const token = tokens[i];

    if (token.type === 'key') {
      if (current === null || current === undefined || typeof current !== 'object') return undefined;
      current = (current as Record<string, unknown>)[token.value];
    } else if (token.type === 'index') {
      if (!Array.isArray(current)) return undefined;
      if (token.value < 0 || token.value >= current.length) return undefined;
      current = current[token.value];
    } else if (token.type === 'wildcard') {
      if (!Array.isArray(current)) return undefined;
      const remaining = tokens.slice(i + 1);
      if (remaining.length === 0) {
        // Wildcard at the end — just return the array itself
        return current;
      }
      // Recursively resolve the remaining path for each element, then flatten
      const results: unknown[] = [];
      for (const element of current) {
        const sub = resolvePath(element, remaining);
        if (Array.isArray(sub)) {
          results.push(...sub);
        } else {
          results.push(sub);
        }
      }
      return results;
    }
  }
  return current;
}
