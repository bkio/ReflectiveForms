/**
 * Helper to get nested errors from react-hook-form error object
 */
export function getNestedError(
  errors: Record<string, unknown>,
  path: string
): { message?: string } | undefined {
  const parts = path.split('.');
  let current: unknown = errors;

  for (const part of parts) {
    if (!current || typeof current !== 'object') return undefined;
    current = (current as Record<string, unknown>)[part];
  }

  return current as { message?: string } | undefined;
}

/**
 * Generate a unique ID for form elements
 */
export function generateFieldId(path: string): string {
  return `field-${path.replace(/\./g, '-')}`;
}

/**
 * Format error message for display
 */
export function formatErrorMessage(error: { message?: string } | undefined): string | null {
  if (!error?.message) return null;
  return error.message;
}
