import { useFormContext, Controller } from 'react-hook-form';
import { FieldComponentProps } from './types';
import { getNestedError } from '../../lib/formUtils';

export function SelectField({ schema, path }: FieldComponentProps) {
  const { control } = useFormContext();
  const choices = schema.select_options?.choices ?? [];

  return (
    <Controller
      name={path}
      control={control}
      render={({ field, fieldState: { error } }) => (
        <div>
          <select
            {...field}
            className={`
              w-full px-3 py-2 border rounded-md shadow-sm
              focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-blue-500
              ${error ? 'border-red-500' : 'border-gray-300'}
            `}
          >
            {choices.map((choice) => (
              <option key={choice.value} value={choice.value}>
                {choice.label}
              </option>
            ))}
          </select>
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

export function DatePickerField({ schema, path }: FieldComponentProps) {
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
