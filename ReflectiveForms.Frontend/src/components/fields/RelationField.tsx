import { useFormContext, Controller } from 'react-hook-form';
import { RefreshCw } from 'lucide-react';
import { useEntityList } from '../../hooks/useEntity';
import { FieldComponentProps } from './types';

export function RelationField({ schema, path }: FieldComponentProps) {
  const { control } = useFormContext();
  const relationEntityName = schema.relation_options?.relation_entity_name ?? '';

  // Fetch related entities
  const { data: entities, isLoading, error, refetch, isFetching } = useEntityList(relationEntityName);

  if (isLoading) {
    return (
      <div className="animate-pulse">
        <div className="h-10 bg-gray-200 rounded"></div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="text-red-500 text-sm">
        Failed to load {relationEntityName} options
      </div>
    );
  }

  const options = entities ?? [];

  return (
    <Controller
      name={path}
      control={control}
      render={({ field, fieldState: { error: fieldError } }) => (
        <div>
          <div className="flex gap-2">
            <select
              value={field.value ?? -1}
              onChange={(e) => field.onChange(Number(e.target.value))}
              className={`
                flex-1 px-3 py-2 border rounded-md shadow-sm
                focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-blue-500
                ${fieldError ? 'border-red-500' : 'border-gray-300'}
              `}
            >
              <option value={-1}>-- Select --</option>
              {options.map((entity) => (
                <option key={entity.id} value={entity.id}>
                  {entity.title ?? entity.name ?? `ID: ${entity.id}`}
                </option>
              ))}
            </select>
            <button
              type="button"
              onClick={() => refetch()}
              disabled={isFetching}
              className={`px-3 py-2 border border-gray-300 rounded-md text-gray-700 hover:bg-gray-50 ${isFetching ? 'opacity-50' : ''}`}
              title="Refresh list"
            >
              <RefreshCw className={`w-4 h-4 ${isFetching ? 'animate-spin' : ''}`} />
            </button>
          </div>
          {fieldError && (
            <p className="mt-1 text-sm text-red-600">{fieldError.message}</p>
          )}
        </div>
      )}
    />
  );
}
