import { useParams, useSearchParams } from 'react-router-dom';
import { useSchema, useEntity } from '../hooks/useEntity';
import { DynamicForm } from '../components/form/DynamicForm';

export function EntityEditPage() {
  const { entityName } = useParams<{ entityName: string }>();
  const [searchParams] = useSearchParams();
  const idParam = searchParams.get('id');

  // Parse ID: 'new' = create, number = edit, 'clone_from_X' = clone
  let entityId: number | undefined;
  let cloneFromId: number | undefined;

  if (idParam === 'new') {
    entityId = undefined;
  } else if (idParam?.startsWith('clone_from_')) {
    entityId = undefined;
    cloneFromId = parseInt(idParam.replace('clone_from_', ''), 10);
  } else if (idParam) {
    entityId = parseInt(idParam, 10);
  }

  // Fetch schema
  const {
    data: schema,
    isLoading: schemaLoading,
    error: schemaError,
  } = useSchema(entityName ?? '');

  // Fetch entity data (for edit/clone mode)
  const sourceId = entityId ?? cloneFromId;
  const {
    data: entityData,
    isLoading: entityLoading,
    error: entityError,
  } = useEntity(entityName ?? '', sourceId);

  // Loading state
  if (schemaLoading || entityLoading) {
    return (
      <div className="min-h-screen bg-gray-50 flex items-center justify-center">
        <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-blue-600"></div>
      </div>
    );
  }

  // Error state
  if (schemaError || entityError) {
    return (
      <div className="min-h-screen bg-gray-50 flex items-center justify-center">
        <div className="bg-red-50 text-red-600 p-6 rounded-lg">
          <h2 className="text-lg font-semibold mb-2">Error</h2>
          <p>{(schemaError || entityError)?.message}</p>
        </div>
      </div>
    );
  }

  if (!schema) {
    return (
      <div className="min-h-screen bg-gray-50 flex items-center justify-center">
        <div className="text-gray-500">Schema not found</div>
      </div>
    );
  }

  // Prepare initial data
  let initialData = entityData ?? undefined;

  // For clone mode, reset ID
  if (cloneFromId && initialData) {
    initialData = {
      ...initialData,
      id: -1,
    };
  }

  const pageTitle = entityId
    ? `Edit ${schema.readable_name.singular}`
    : cloneFromId
    ? `Clone ${schema.readable_name.singular}`
    : `New ${schema.readable_name.singular}`;

  return (
    <div className="min-h-screen bg-gray-50">
      <div className="max-w-4xl mx-auto py-8 px-4">
        {/* Header */}
        <div className="mb-8">
          <h1 className="text-2xl font-bold text-gray-900">{pageTitle}</h1>
          <p className="mt-1 text-gray-500">
            {entityId ? `Editing ID: ${entityId}` : 'Creating new entry'}
          </p>
        </div>

        {/* Form */}
        <DynamicForm
          schema={schema}
          initialData={initialData}
          entityId={entityId}
          onSuccess={(data) => {
            // Navigate to edit page with new ID
            if (!entityId && data.id) {
              window.history.replaceState(
                null,
                '',
                `?type=${entityName}&id=${data.id}`
              );
            }
          }}
        />
      </div>
    </div>
  );
}
