import React from 'react';
import { useFormContext, useWatch } from 'react-hook-form';
import { FieldSchema } from '../../types/schema';
import { evaluateCompoundCondition } from '../../lib/conditionParser';
import { sanitizeHtml } from '../../lib/sanitize';
import { FieldComponentProps } from './types';

// Import all field components
import { TextField, TextAreaField } from './TextField';
import { SelectField, CheckboxField, NumberField, DatePickerField } from './SelectField';
import { RelationField } from './RelationField';
import { GroupField } from './GroupField';
import { RepeaterField } from './RepeaterField';
import { MediaField } from './MediaField';
import { WysiwygField } from './WysiwygField';

// Re-export for backwards compatibility
export type { FieldComponentProps } from './types';

// Field registry - maps field types to components
const fieldRegistry: Record<string, React.ComponentType<FieldComponentProps>> = {
  Text: TextField,
  TextArea: TextAreaField,
  Email: TextField,
  Url: TextField,
  WysiwygEditor: WysiwygField,
  Number: NumberField,
  Range: NumberField,
  Select: SelectField,
  Checkbox: CheckboxField,
  DatePicker: DatePickerField,
  Relation: RelationField,
  Group: GroupField,
  Repeater: RepeaterField,
  MediaSourceBase64: MediaField,
};

interface FormFieldProps {
  fieldSchema: FieldSchema;
  basePath?: string;
  depth?: number;
}

export function FormField({ fieldSchema, basePath = 'fields', depth = 0 }: FormFieldProps) {
  const { control } = useFormContext();
  const formValues = useWatch({ control });

  // Evaluate display condition
  const isVisible = fieldSchema.display_condition
    ? evaluateCompoundCondition(fieldSchema.display_condition, formValues)
    : true;

  if (!isVisible) return null;

  const FieldComponent = fieldRegistry[fieldSchema.type];
  if (!FieldComponent) {
    console.warn(`Unknown field type: ${fieldSchema.type}`);
    return null;
  }

  const fieldPath = basePath ? `${basePath}.${fieldSchema.name}` : fieldSchema.name;

  return (
    <div className={`field-wrapper field-type-${fieldSchema.type.toLowerCase()} ${depth > 0 ? 'ml-4' : ''}`}>
      <div className="bg-white rounded-lg shadow-sm border border-gray-200 p-4 mb-4">
        {/* Field header */}
        <div className="mb-2">
          <label className="block text-sm font-medium text-gray-700">
            {fieldSchema.label}
            {fieldSchema.required && <span className="text-red-500 ml-1">*</span>}
          </label>
          {fieldSchema.instructions && (
            <p
              className="mt-1 text-sm text-gray-500"
              dangerouslySetInnerHTML={{ __html: sanitizeHtml(fieldSchema.instructions) }}
            />
          )}
        </div>

        {/* Field content */}
        <FieldComponent schema={fieldSchema} path={fieldPath} depth={depth} />
      </div>
    </div>
  );
}

// Re-export for external use
export { fieldRegistry };
