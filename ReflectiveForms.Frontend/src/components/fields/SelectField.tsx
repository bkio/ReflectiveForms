import { useMemo } from 'react';
import { useFormContext, Controller, useWatch } from 'react-hook-form';
import { FieldComponentProps } from './types';
import { getNestedError } from '../../lib/formUtils';
import { SearchableChoicesSelect, ChoiceOption } from '../form/SearchableChoicesSelect';

/**
 * Evaluate a DynamicChoicesRuntimeAsync JS function against current form values.
 * The JS code expects `window.latest_dynamic_options_input` to be set with
 * the sibling field values and returns an array of "value : label" strings.
 */
function evaluateRuntimeChoices(
  jsFunction: string,
  formFields: Record<string, unknown>
): ChoiceOption[] {
  try {
    // Set the global that the JS function reads
    (window as any).latest_dynamic_options_input = formFields;

    // Wrap the JS in a function that returns the result
    const fn = new Function(`${jsFunction}`);
    const result = fn();

    if (!Array.isArray(result)) return [];

    return result.map((item: string) => {
      const parts = String(item).split(' : ', 2);
      return {
        value: parts[0].trim(),
        label: parts.length > 1 ? parts[1].trim() : parts[0].trim(),
      };
    });
  } catch {
    return [];
  } finally {
    delete (window as any).latest_dynamic_options_input;
  }
}

export function SelectField({ schema, path }: FieldComponentProps) {
  const { control } = useFormContext();
  const staticChoices = schema.select_options?.choices ?? [];
  const jsFunction = schema.select_options?.dynamic_choices_js_function;
  const hasDynamicRuntime = schema.has_dynamic_choices_runtime && !!jsFunction;

  // Watch all form field values to re-evaluate dynamic choices when any value changes
  const formValues = useWatch({ control });
  const fields = (formValues as Record<string, unknown>)?.fields as Record<string, unknown> ?? {};

  const choices: ChoiceOption[] = useMemo(() => {
    if (hasDynamicRuntime) {
      return evaluateRuntimeChoices(jsFunction!, fields);
    }
    return staticChoices;
  }, [hasDynamicRuntime, jsFunction, fields, staticChoices]);

  return (
    <Controller
      name={path}
      control={control}
      render={({ field, fieldState: { error } }) => (
        <div>
          <SearchableChoicesSelect
            choices={choices}
            value={field.value ?? ''}
            onChange={(val) => field.onChange(val)}
            hasError={!!error}
          />
          {error && <p className="mt-1 text-sm text-red-600">{error.message}</p>}
        </div>
      )}
    />
  );
}

export function CheckboxField({ schema, path }: FieldComponentProps) {
  const { register } = useFormContext();

  return (
    <div className="flex items-center">
      <input
        type="checkbox"
        {...register(path)}
        className="h-4 w-4 text-blue-600 focus:ring-blue-500 border-gray-300 rounded"
      />
      <span className="ml-2 text-sm text-gray-600">{schema.label}</span>
    </div>
  );
}

export function NumberField({ schema, path }: FieldComponentProps) {
  const {
    register,
    formState: { errors },
  } = useFormContext();

  const error = getNestedError(errors, path);
  const isRange = schema.type === 'Range' || schema.number_options?.is_range;

  return (
    <div>
      <input
        type={isRange ? 'range' : 'number'}
        {...register(path, { valueAsNumber: true })}
        min={schema.number_options?.min}
        max={schema.number_options?.max}
        step={schema.number_options?.step}
        className={
          isRange
            ? 'w-full'
            : `
              w-full px-3 py-2 border rounded-md shadow-sm
              focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-blue-500
              ${error ? 'border-red-500' : 'border-gray-300'}
            `
        }
      />
      {error && <p className="mt-1 text-sm text-red-600">{error.message as string}</p>}
    </div>
  );
}

export function DatePickerField({ schema: _schema, path }: FieldComponentProps) {
  const {
    register,
    formState: { errors },
  } = useFormContext();

  const error = getNestedError(errors, path);

  return (
    <div>
      <input
        type="date"
        {...register(path)}
        className={`
          w-full px-3 py-2 border rounded-md shadow-sm
          focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-blue-500
          ${error ? 'border-red-500' : 'border-gray-300'}
        `}
      />
      {error && <p className="mt-1 text-sm text-red-600">{error.message as string}</p>}
    </div>
  );
}
