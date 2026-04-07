import { useFormContext } from 'react-hook-form';
import { FieldComponentProps } from './types';
import { getNestedError } from '../../lib/formUtils';

export function TextField({ schema, path }: FieldComponentProps) {
  const {
    register,
    formState: { errors },
  } = useFormContext();

  const error = getNestedError(errors, path);
  const inputType = schema.type === 'Email' ? 'email' : schema.type === 'Url' ? 'url' : 'text';

  return (
    <div>
      <input
        type={inputType}
        {...register(path)}
        placeholder={schema.text_options?.placeholder}
        maxLength={schema.text_options?.max_length}
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

export function TextAreaField({ schema, path }: FieldComponentProps) {
  const {
    register,
    formState: { errors },
  } = useFormContext();

  const error = getNestedError(errors, path);

  return (
    <div>
      <textarea
        {...register(path)}
        placeholder={schema.text_options?.placeholder}
        rows={5}
        className={`
          w-full px-3 py-2 border rounded-md shadow-sm resize-y
          focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-blue-500
          ${error ? 'border-red-500' : 'border-gray-300'}
        `}
      />
      {error && <p className="mt-1 text-sm text-red-600">{error.message as string}</p>}
    </div>
  );
}
