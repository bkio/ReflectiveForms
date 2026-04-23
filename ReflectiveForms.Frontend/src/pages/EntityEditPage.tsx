import { useParams, useSearchParams, Navigate, useNavigate, Link, useLocation } from 'react-router-dom';
import { useEffect } from 'react';
import { useSchema, useEntity, useCapabilities, useEntityHistory } from '../hooks/useEntity';
import { DynamicForm } from '../components/form/DynamicForm';
import type { EntityData } from '../types/schema';
import type { FieldSchema } from '../types/schema';
import { GitCompare } from 'lucide-react';
import { useAiAssistantOptional } from '../lib/AiAssistantContext';

export function EntityEditPage() {
  const { entityName } = useParams<{ entityName: string }>();
  const [searchParams] = useSearchParams();
  const navigate = useNavigate();
  const location = useLocation();
  const idParam = searchParams.get('id');

  // Parse ID: 'new' = create, number = edit, 'clone_from_X' = clone
  let entityId: number | undefined;
  let cloneFromId: number | undefined;

  if (idParam === 'new') {
    entityId = undefined;
  } else if (idParam?.startsWith('clone_from_')) {
    entityId = undefined;
    const parsed = parseInt(idParam.replace('clone_from_', ''), 10);
    cloneFromId = Number.isNaN(parsed) ? undefined : parsed;
  } else if (idParam) {
    const parsed = parseInt(idParam, 10);
    entityId = Number.isNaN(parsed) ? undefined : parsed;
  }

  // Fetch schema
  const {
    data: schema,
    isLoading: schemaLoading,
    error: schemaError,
  } = useSchema(entityName ?? '');

  const { data: capabilities } = useCapabilities();

  // Fetch entity data (for edit/clone mode)
  const sourceId = entityId ?? cloneFromId;
  const {
    data: entityData,
    isLoading: entityLoading,
    error: entityError,
  } = useEntity(entityName ?? '', sourceId);

  // Fetch revision history for edit mode only (not new/clone)
  const { data: historyData } = useEntityHistory(entityName ?? '', entityId);
  const hasRevisions = (historyData?.revisions_count ?? 0) > 0;

  // Push context to AI assistant on page load / entity change
  const assistant = useAiAssistantOptional();
  useEffect(() => {
    assistant?.setContext({
      current_page: entityId ? 'entity-edit' : 'entity-create',
      entity_type: entityName,
      entity_id: entityId,
    });
  }, [entityName, entityId, assistant]);

  // Loading state
  if (schemaLoading) {
    return (
      <div className="flex items-center justify-center min-h-[400px]">
        <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-blue-600"></div>
      </div>
    );
  }

  // Error state (schema only — entity errors are checked after capability guard)
  if (schemaError) {
    return (
      <div className="flex items-center justify-center min-h-[400px]">
        <div className="bg-red-50 text-red-600 p-6 rounded-lg">
          <h2 className="text-lg font-semibold mb-2">Error</h2>
          <p>{schemaError.message}</p>
        </div>
      </div>
    );
  }

  if (!schema) {
    return (
      <div className="flex items-center justify-center min-h-[400px]">
        <div className="text-gray-500">Schema not found</div>
      </div>
    );
  }

  // Redirect to view page when editing is not supported for this entity type
  if (!schema.features.supports_frontend_edit) {
    const viewPath = idParam && idParam !== 'new'
      ? `/entities-view/${entityName}?id=${entityId ?? idParam}`
      : `/entities/${entityName}`;
    return <Navigate to={viewPath} replace />;
  }

  // Redirect when user lacks the required capability (checked before entity load error)
  const caps = entityName ? capabilities?.[entityName] : undefined;
  if (caps) {
    const isCreateAction = !entityId; // new or clone
    if (isCreateAction && !caps.can_create) {
      return <Navigate to={`/entities/${entityName}`} replace />;
    }
    if (!isCreateAction && !caps.can_update) {
      return <Navigate to={`/entities-view/${entityName}?id=${entityId}`} replace />;
    }
  }

  // Now check entity loading/error (after capability guard)
  if (entityLoading) {
    return (
      <div className="flex items-center justify-center min-h-[400px]">
        <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-blue-600"></div>
      </div>
    );
  }

  if (entityError) {
    return (
      <div className="flex items-center justify-center min-h-[400px]">
        <div className="bg-red-50 text-red-600 p-6 rounded-lg">
          <h2 className="text-lg font-semibold mb-2">Error</h2>
          <p>{entityError.message}</p>
        </div>
      </div>
    );
  }

  // Prepare initial data
  let initialData = entityData ?? undefined;

  // Convert yyyyMMdd date strings to yyyy-MM-dd for HTML date inputs in edit/clone mode
  if (initialData?.fields && schema) {
    initialData = {
      ...initialData,
      fields: normalizeDateFields(initialData.fields as Record<string, unknown>, schema.fields),
    };
  }

  // For AI-generated create: use pre-populated fields from navigation state
  const aiPrefill = (location.state as Record<string, unknown> | null)?.aiPrefill as
    | { title?: string; fields?: Record<string, unknown> }
    | undefined;
  if (!entityId && !cloneFromId && aiPrefill) {
    initialData = {
      id: -1,
      title: aiPrefill.title ? { rendered: aiPrefill.title } : undefined,
      fields: (aiPrefill.fields ?? {}) as Record<string, unknown>,
    } as EntityData;
  }

  // Redirect to view page when editing a system-managed entity (root user, owner role, etc.)
  if (entityId && entityData?.is_system_managed) {
    return <Navigate to={`/entities-view/${entityName}?id=${entityId}`} replace />;
  }

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
    <div>
      <div className="max-w-4xl mx-auto py-8 px-4">
        {/* Header */}
        <div className="mb-8 flex items-start justify-between">
          <div>
            <h1 className="text-2xl font-bold text-gray-900">{pageTitle}</h1>
            <p className="mt-1 text-gray-500">
              {entityId ? `Editing ID: ${entityId}` : 'Creating new entry'}
            </p>
          </div>
          {entityId && hasRevisions && (
            <Link
              to={`/entities-revisions/${entityName}?id=${entityId}`}
              className="flex items-center gap-2 px-4 py-2 bg-gray-100 text-gray-700 rounded-md hover:bg-gray-200 transition-colors"
              title="Compare Revisions"
              data-testid="compare-revisions-button"
            >
              <GitCompare className="w-4 h-4" />
              Compare Revisions
            </Link>
          )}
        </div>

        {/* Form */}
        <DynamicForm
          key={`${entityId ?? 'new'}-${entityData ? 'loaded' : 'pending'}`}
          schema={schema}
          initialData={initialData}
          entityId={entityId}
          onSuccess={(data) => {
            // Navigate to edit mode with the new entity ID
            if (!entityId && data.id) {
              navigate(`/entities-admin/${entityName}?id=${data.id}`, { replace: true });
            }
          }}
        />
      </div>
    </div>
  );
}

function normalizeDateFields(
  fields: Record<string, unknown>,
  fieldSchemas: FieldSchema[],
): Record<string, unknown> {
  const result = { ...fields };
  for (const fs of fieldSchemas) {
    const val = result[fs.name];
    if (val == null) continue;

    if (fs.type === 'DatePicker' && typeof val === 'string' && /^\d{8}$/.test(val)) {
      result[fs.name] = `${val.slice(0, 4)}-${val.slice(4, 6)}-${val.slice(6, 8)}`;
    } else if (fs.type === 'Group' && fs.group_options?.child_schema && typeof val === 'object' && !Array.isArray(val)) {
      result[fs.name] = normalizeDateFields(val as Record<string, unknown>, fs.group_options.child_schema);
    } else if (fs.type === 'Repeater' && fs.repeater_options?.item_schema && Array.isArray(val)) {
      result[fs.name] = val.map((item) =>
        typeof item === 'object' && item != null
          ? normalizeDateFields(item as Record<string, unknown>, fs.repeater_options!.item_schema)
          : item,
      );
    }
  }
  return result;
}
