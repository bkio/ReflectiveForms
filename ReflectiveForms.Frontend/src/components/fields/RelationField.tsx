import { useFormContext, Controller } from 'react-hook-form';
import { FieldComponentProps } from './types';
import { SearchableSelect } from '../form/SearchableSelect';

export function RelationField({ schema, path }: FieldComponentProps) {
  const { control } = useFormContext();
  const relationEntityName = schema.relation_options?.relation_entity_name ?? '';

  return (
    <Controller
      name={path}
      control={control}
      render={({ field, fieldState: { error: fieldError } }) => (
        <div>
          <SearchableSelect
            entityName={relationEntityName}
            value={field.value ?? -1}
            onChange={(val) => field.onChange(val)}
          />
          {fieldError && (
            <p className="mt-1 text-sm text-red-600">{fieldError.message}</p>
          )}
        </div>
      )}
    />
  );
}
