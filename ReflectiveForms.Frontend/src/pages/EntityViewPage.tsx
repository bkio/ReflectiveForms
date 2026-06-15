import { useMemo, useState, useCallback, useEffect } from 'react';
import { useParams, useSearchParams, Link } from 'react-router-dom';
import { useSchema, useEntity, useEntityList, useCapabilities, useEntityHistory } from '../hooks/useEntity';
import { useAiAssistantOptional } from '../lib/AiAssistantContext';
import { FieldSchema, EntitySchema, PeekEntity, GroupRenderStyle } from '../types/schema';
import { sanitizeWysiwygHtml } from '../lib/sanitize';
import { evaluateCompoundCondition } from '../lib/conditionParser';
import { ArrowLeft, Edit, Tag, FolderTree, User, GitBranch, GitCompare, Radio, Lock } from 'lucide-react';
import { useLiveUpdates } from '../hooks/useLiveUpdates';
import { useQuery } from '@tanstack/react-query';
import { fetchLockStatus } from '../api/client';

/** Recursively collect all unique relation entity names from the field schema tree. */
function collectRelationEntityNames(fields: FieldSchema[]): string[] {
  const names = new Set<string>();
  function walk(fieldList: FieldSchema[]) {
    for (const f of fieldList) {
      if (f.type === 'Relation' && f.relation_options?.relation_entity_name) {
        names.add(f.relation_options.relation_entity_name);
      }
      if (f.group_options?.child_schema) walk(f.group_options.child_schema);
      if (f.repeater_options?.item_schema) walk(f.repeater_options.item_schema);
    }
  }
  walk(fields);
  return Array.from(names);
}

const gridClassMap: Record<GroupRenderStyle, string> = {
  Full: 'grid grid-cols-1 gap-4',
  Grid2: 'grid grid-cols-1 md:grid-cols-2 gap-4',
  Grid3: 'grid grid-cols-1 md:grid-cols-3 gap-4',
  Grid4: 'grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4',
  Grid6: 'grid grid-cols-2 md:grid-cols-3 xl:grid-cols-6 gap-4',
};

export function EntityViewPage() {
  const { entityName } = useParams<{ entityName: string }>();
  const [searchParams] = useSearchParams();
  const idParam = searchParams.get('id');
  const parsedId = idParam ? parseInt(idParam, 10) : NaN;
  const entityId = Number.isNaN(parsedId) ? undefined : parsedId;

  const { data: schema, isLoading: schemaLoading, error: schemaError } = useSchema(entityName ?? '');
  const { data: entityData, isLoading: entityLoading, error: entityError } = useEntity(entityName ?? '', entityId);
  const { data: capabilities } = useCapabilities();

  // Push context to AI assistant
  const assistant = useAiAssistantOptional();
  useEffect(() => {
    assistant?.setContext({ current_page: 'entity-view', entity_type: entityName, entity_id: entityId });
  }, [assistant, entityName, entityId]);

  // Poll lock status so the Edit button is hidden while the entity is being edited
  const { data: lockData } = useQuery({
    queryKey: ['entity-lock-status', entityName, entityId],
    queryFn: async () => {
      const res = await fetchLockStatus(entityName!, entityId!);
      return res.data ?? null;
    },
    enabled: !!entityName && entityId !== undefined,
    refetchInterval: 10_000,
    refetchIntervalInBackground: false,
  });
  const isLocked = lockData != null;

  // Live updates: receive real-time changes from the editing user
  const [liveData, setLiveData] = useState<Record<string, unknown> | null>(null);
  const handleLiveUpdate = useCallback((data: Record<string, unknown>) => {
    setLiveData(data);
  }, []);
  const { status: liveStatus } = useLiveUpdates({
    entityName: entityName ?? '',
    entityId,
    role: 'viewer',
    onUpdate: handleLiveUpdate,
    enabled: !!entityName && entityId !== undefined,
  });

  // Collect all unique relation entity names from the schema
  const relationEntityNames = useMemo(
    () => (schema ? collectRelationEntityNames(schema.fields) : []),
    [schema],
  );

  // Fetch relation entity lists — hooks must always be called in the same order,
  // so we use a fixed-size array padded to a stable length.
  const MAX_RELATION_ENTITIES = 10;
  const relationQueries = Array.from({ length: MAX_RELATION_ENTITIES }, (_, i) =>
    // eslint-disable-next-line react-hooks/rules-of-hooks
    useEntityList(relationEntityNames[i] ?? ''),
  );

  const relationLookup = useMemo(() => {
    const lookup: Record<string, Record<number, string>> = {};
    for (let i = 0; i < relationEntityNames.length && i < MAX_RELATION_ENTITIES; i++) {
      const name = relationEntityNames[i];
      const data = relationQueries[i].data;
      if (data) {
        lookup[name] = {};
        for (const e of data) {
          lookup[name][e.id] = e.title ?? e.name ?? `#${e.id}`;
        }
      }
    }
    return lookup;
  }, [relationEntityNames, ...relationQueries.map(q => q.data)]);

  // Fetch related entity lists for metadata display (conditional on features)
  const { data: usersList } = useEntityList(schema?.features.has_author ? 'users' : '');
  const { data: tagsList } = useEntityList(schema?.features.has_tags ? 'tags' : '');
  const { data: categoriesList } = useEntityList(schema?.features.has_categories ? 'categories' : '');
  const { data: parentList } = useEntityList(schema?.features.has_parent_child ? (entityName ?? '') : '');

  const { data: historyData } = useEntityHistory(entityName ?? '', entityId);
  const hasRevisions = (historyData?.revisions_count ?? 0) > 0;

  if (schemaLoading || entityLoading) {
    return (
      <div className="flex items-center justify-center min-h-[400px]">
        <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-blue-600"></div>
      </div>
    );
  }

  if (schemaError || entityError) {
    return (
      <div className="flex items-center justify-center min-h-[400px]">
        <div className="bg-red-50 text-red-600 p-6 rounded-lg">
          <h2 className="text-lg font-semibold mb-2">Error</h2>
          <p>{(schemaError || entityError)?.message}</p>
        </div>
      </div>
    );
  }

  if (!schema || !entityData) {
    return (
      <div className="flex items-center justify-center min-h-[400px]">
        <div className="text-gray-500">Entity not found</div>
      </div>
    );
  }

  const title = liveData?.title as { rendered?: string } | undefined ?? entityData.title;
  const fields = (liveData?.fields ?? entityData.fields ?? {}) as Record<string, unknown>;
  const isLive = liveStatus === 'connected' && liveData !== null;

  return (
    <div>
      <div className="max-w-4xl mx-auto py-8 px-4">
        {/* Header */}
        <div className="mb-8 flex items-start justify-between">
          <div>
            <div className="flex items-center gap-3 mb-2">
              <Link
                to={`/entities/${entityName}`}
                className="text-gray-500 hover:text-gray-700 transition-colors"
                title="Back to list"
              >
                <ArrowLeft className="w-5 h-5" />
              </Link>
              <h1 className="text-2xl font-bold text-gray-900 dark:text-gray-100">
                {title?.rendered || 'Untitled'}
              </h1>
            </div>
            <p className="ml-8 text-sm text-gray-500">
              {schema.readable_name.singular} — ID: {entityId}
              {isLive && (
                <span className="ml-3 inline-flex items-center gap-1 text-green-600" data-testid="live-indicator">
                  <Radio className="w-3.5 h-3.5 animate-pulse" />
                  Live
                </span>
              )}
            </p>
          </div>
          <div className="flex items-center gap-2">
            {hasRevisions && (
              <Link
                to={`/entities-revisions/${entityName}?id=${entityId}`}
                className="flex items-center gap-2 px-4 py-2 bg-gray-100 dark:bg-gray-700 text-gray-700 dark:text-gray-200 rounded-md hover:bg-gray-200 dark:hover:bg-gray-600 transition-colors"
                title="Compare Revisions"
                data-testid="compare-revisions-button"
              >
                <GitCompare className="w-4 h-4" />
                Compare Revisions
              </Link>
            )}
            {isLocked && (
              <span
                className="flex items-center gap-2 px-4 py-2 text-sm text-amber-700 dark:text-amber-300 bg-amber-50 dark:bg-amber-900/30 border border-amber-200 dark:border-amber-800 rounded-md"
                data-testid="lock-indicator"
              >
                <Lock className="w-4 h-4" />
                Being edited by {lockData?.locked_by_user_name ?? 'another user'}
              </span>
            )}
            {schema.features.supports_frontend_edit && !entityData?.is_system_managed && !isLocked && (capabilities?.[entityName!]?.can_update ?? true) && (
              <Link
                to={`/entities-admin/${entityName}?id=${entityId}`}
                className="flex items-center gap-2 px-4 py-2 bg-blue-600 text-white rounded-md hover:bg-blue-700 transition-colors"
                title="Edit"
              >
                <Edit className="w-4 h-4" />
                Edit
              </Link>
            )}
          </div>
        </div>

        {/* Metadata section */}
        <MetadataSection
          schema={schema}
          entityData={entityData}
          usersList={usersList}
          tagsList={tagsList}
          categoriesList={categoriesList}
          parentList={parentList}
          entityName={entityName!}
        />

        {/* Fields */}
        <div className="space-y-4">
          {schema.fields.map((fieldSchema) => (
            <ReadOnlyField
              key={fieldSchema.name}
              fieldSchema={fieldSchema}
              value={fields[fieldSchema.name]}
              allFields={fields}
              schema={schema}
              relationLookup={relationLookup}
            />
          ))}
        </div>
      </div>
    </div>
  );
}

interface ReadOnlyFieldProps {
  fieldSchema: FieldSchema;
  value: unknown;
  allFields: Record<string, unknown>;
  schema: EntitySchema;
  depth?: number;
  relationLookup?: Record<string, Record<number, string>>;
}

function ReadOnlyField({ fieldSchema, value, allFields, schema, depth = 0, relationLookup = {} }: ReadOnlyFieldProps) {
  // Evaluate display condition
  if (fieldSchema.display_condition) {
    if (!evaluateCompoundCondition(fieldSchema.display_condition, allFields)) {
      return null;
    }
  }

  const isTopLevel = depth === 0;

  return (
    <div className={`field-view field-type-${fieldSchema.type.toLowerCase()}`}>
      {isTopLevel ? (
        <div className="bg-white dark:bg-gray-800 rounded-lg shadow-sm border border-gray-200 dark:border-gray-700 p-4">
          <div className="mb-1">
            <span className="text-sm font-medium text-gray-500">
              {fieldSchema.label}
            </span>
          </div>
          <ReadOnlyValue fieldSchema={fieldSchema} value={value} allFields={allFields} schema={schema} depth={depth} relationLookup={relationLookup} />
        </div>
      ) : (
        <div className="mb-2">
          <div className="mb-0.5">
            <span className="text-xs font-medium text-gray-500">
              {fieldSchema.label}
            </span>
          </div>
          <ReadOnlyValue fieldSchema={fieldSchema} value={value} allFields={allFields} schema={schema} depth={depth} relationLookup={relationLookup} />
        </div>
      )}
    </div>
  );
}

interface ReadOnlyValueProps {
  fieldSchema: FieldSchema;
  value: unknown;
  allFields: Record<string, unknown>;
  schema: EntitySchema;
  depth?: number;
  relationLookup?: Record<string, Record<number, string>>;
}

function ReadOnlyValue({ fieldSchema, value, schema, depth = 0, relationLookup = {} }: ReadOnlyValueProps) {
  const type = fieldSchema.type;

  // Empty/null value
  if (value === null || value === undefined || value === '') {
    return <span className="text-gray-400 italic text-sm">Not set</span>;
  }

  switch (type) {
    case 'Text':
    case 'Email':
    case 'Url':
      if (type === 'Url' && typeof value === 'string' && value) {
        return (
          <a href={value} target="_blank" rel="noopener noreferrer" className="text-blue-600 hover:underline break-all">
            {value}
          </a>
        );
      }
      if (type === 'Email' && typeof value === 'string' && value) {
        return (
          <a href={`mailto:${value}`} className="text-blue-600 hover:underline">
            {value}
          </a>
        );
      }
      return <span className="text-gray-900 dark:text-gray-100">{String(value)}</span>;

    case 'TextArea':
      return <p className="text-gray-900 dark:text-gray-100 whitespace-pre-wrap">{String(value)}</p>;

    case 'WysiwygEditor':
      return (
        <div
          className="prose prose-sm max-w-none text-gray-900 dark:text-gray-100 dark:prose-invert"
          dangerouslySetInnerHTML={{ __html: sanitizeWysiwygHtml(String(value)) }}
        />
      );

    case 'Number':
    case 'Range':
      return <span className="text-gray-900 dark:text-gray-100 font-mono">{String(value)}</span>;

    case 'Checkbox':
      return (
        <span className={`inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium ${value ? 'bg-green-100 dark:bg-green-900/30 text-green-800 dark:text-green-300' : 'bg-gray-100 dark:bg-gray-700 text-gray-800 dark:text-gray-300'}`}>
          {value ? 'Yes' : 'No'}
        </span>
      );

    case 'DatePicker': {
      const dateStr = String(value);
      // Format yyyyMMdd to yyyy-MM-dd
      if (/^\d{8}$/.test(dateStr)) {
        return <span className="text-gray-900 dark:text-gray-100">{`${dateStr.slice(0, 4)}-${dateStr.slice(4, 6)}-${dateStr.slice(6, 8)}`}</span>;
      }
      return <span className="text-gray-900 dark:text-gray-100">{dateStr}</span>;
    }

    case 'Select': {
      const strVal = String(value);
      const choice = fieldSchema.select_options?.choices?.find(c => c.value === strVal);
      return (
        <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-blue-100 dark:bg-blue-900/30 text-blue-800 dark:text-blue-300">
          {choice?.label ?? strVal}
        </span>
      );
    }

    case 'Relation': {
      const relVal = Number(value);
      if (relVal <= 0) return <span className="text-gray-400 italic text-sm">None</span>;
      const relEntityName = fieldSchema.relation_options?.relation_entity_name ?? '';
      const resolvedName = relationLookup[relEntityName]?.[relVal];
      if (resolvedName) {
        return (
          <Link
            to={`/entities-view/${relEntityName}?id=${relVal}`}
            className="text-blue-600 hover:underline"
          >
            {resolvedName}
          </Link>
        );
      }
      return <span className="text-gray-900 dark:text-gray-100">ID: {relVal}</span>;
    }

    case 'MediaSourceBase64': {
      const mediaVal = String(value);
      if (!mediaVal) return <span className="text-gray-400 italic text-sm">No media</span>;
      return (
        <div className="mt-1">
          <img
            src={mediaVal.startsWith('data:') ? mediaVal : mediaVal}
            alt={fieldSchema.label}
            className="max-w-xs max-h-48 object-contain rounded border border-gray-200 dark:border-gray-700"
          />
        </div>
      );
    }

    case 'Group': {
      const groupVal = (value && typeof value === 'object') ? value as Record<string, unknown> : {};
      const childFields = fieldSchema.group_options?.child_schema ?? [];
      if (childFields.length === 0) return <span className="text-gray-400 italic text-sm">Empty</span>;
      const renderStyle = fieldSchema.group_options?.render_style ?? 'Full';
      const gridClass = gridClassMap[renderStyle] ?? gridClassMap.Full;
      return (
        <div className={`${gridClass} mt-2`}>
          {childFields.map(child => (
            <ReadOnlyField
              key={child.name}
              fieldSchema={child}
              value={groupVal[child.name]}
              allFields={groupVal}
              schema={schema}
              depth={depth + 1}
              relationLookup={relationLookup}
            />
          ))}
        </div>
      );
    }

    case 'Repeater': {
      const items = Array.isArray(value) ? value : [];
      const itemFields = fieldSchema.repeater_options?.item_schema ?? [];
      if (items.length === 0) return <span className="text-gray-400 italic text-sm">No items</span>;
      const renderStyle = fieldSchema.repeater_options?.render_style ?? 'Full';
      const itemGridClass = gridClassMap[renderStyle] ?? gridClassMap.Full;
      return (
        <div className="space-y-3 mt-2">
          {items.map((item, idx) => {
            const itemObj = (item && typeof item === 'object') ? item as Record<string, unknown> : {};
            return (
              <div key={idx} className="border border-gray-200 dark:border-gray-700 rounded-lg overflow-hidden">
                <div className="bg-gray-100 dark:bg-gray-700 px-3 py-2 border-b border-gray-200 dark:border-gray-700">
                  <span className="text-xs font-semibold text-gray-600 dark:text-gray-300 uppercase tracking-wider">
                    {fieldSchema.label} #{idx + 1}
                  </span>
                </div>
                <div className={`p-3 ${itemGridClass}`}>
                  {itemFields.map(child => (
                    <ReadOnlyField
                      key={child.name}
                      fieldSchema={child}
                      value={itemObj[child.name]}
                      allFields={itemObj}
                      schema={schema}
                      depth={depth + 1}
                      relationLookup={relationLookup}
                    />
                  ))}
                </div>
              </div>
            );
          })}
        </div>
      );
    }

    default:
      return <span className="text-gray-900 dark:text-gray-100">{JSON.stringify(value)}</span>;
  }
}

function resolveEntityName(list: PeekEntity[] | undefined, id: number): string {
  if (!list) return `#${id}`;
  const entity = list.find(e => e.id === id);
  return entity?.title ?? entity?.name ?? `#${id}`;
}

interface MetadataSectionProps {
  schema: EntitySchema;
  entityData: { author?: number; tags?: number[]; categories?: number[]; parent?: number };
  usersList?: PeekEntity[];
  tagsList?: PeekEntity[];
  categoriesList?: PeekEntity[];
  parentList?: PeekEntity[];
  entityName: string;
}

function MetadataSection({ schema, entityData, usersList, tagsList, categoriesList, parentList, entityName }: MetadataSectionProps) {
  const { has_author, has_tags, has_categories, has_parent_child } = schema.features;

  // Don't render section if no metadata features are enabled
  if (!has_author && !has_tags && !has_categories && !has_parent_child) return null;

  return (
    <div className="bg-white dark:bg-gray-800 rounded-lg shadow-sm border border-gray-200 dark:border-gray-700 p-4 mb-4" data-testid="metadata-section">
      <h2 className="text-sm font-semibold text-gray-500 dark:text-gray-400 uppercase tracking-wider mb-3">Metadata</h2>
      <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
        {has_author && (
          <div data-testid="metadata-author">
            <div className="flex items-center gap-1.5 text-xs font-medium text-gray-500 mb-1">
              <User className="w-3.5 h-3.5" />
              Author
            </div>
            <span className="text-gray-900 dark:text-gray-100 text-sm">
              {entityData.author != null && entityData.author > 0
                ? resolveEntityName(usersList, entityData.author)
                : <span className="text-gray-400 italic">Not set</span>}
            </span>
          </div>
        )}

        {has_parent_child && (
          <div data-testid="metadata-parent">
            <div className="flex items-center gap-1.5 text-xs font-medium text-gray-500 dark:text-gray-400 mb-1">
              <GitBranch className="w-3.5 h-3.5" />
              Parent
            </div>
            <span className="text-gray-900 dark:text-gray-100 text-sm">
              {entityData.parent != null && entityData.parent > 0
                ? (
                  <Link
                    to={`/entities-view/${entityName}?id=${entityData.parent}`}
                    className="text-blue-600 hover:underline"
                  >
                    {resolveEntityName(parentList, entityData.parent)}
                  </Link>
                )
                : <span className="text-gray-400 italic">None</span>}
            </span>
          </div>
        )}

        {has_tags && (
          <div data-testid="metadata-tags">
            <div className="flex items-center gap-1.5 text-xs font-medium text-gray-500 mb-1">
              <Tag className="w-3.5 h-3.5" />
              Tags
            </div>
            <div className="flex flex-wrap gap-1.5">
              {entityData.tags && entityData.tags.length > 0
                ? entityData.tags.map(tagId => (
                    <span key={tagId} className="inline-flex items-center px-2 py-0.5 rounded-full text-xs font-medium bg-blue-100 dark:bg-blue-900/30 text-blue-800 dark:text-blue-300">
                      {resolveEntityName(tagsList, tagId)}
                    </span>
                  ))
                : <span className="text-gray-400 italic text-sm">No tags</span>}
            </div>
          </div>
        )}

        {has_categories && (
          <div data-testid="metadata-categories">
            <div className="flex items-center gap-1.5 text-xs font-medium text-gray-500 mb-1">
              <FolderTree className="w-3.5 h-3.5" />
              Categories
            </div>
            <div className="flex flex-wrap gap-1.5">
              {entityData.categories && entityData.categories.length > 0
                ? entityData.categories.map(catId => (
                    <span key={catId} className="inline-flex items-center px-2 py-0.5 rounded-full text-xs font-medium bg-green-100 dark:bg-green-900/30 text-green-800 dark:text-green-300">
                      {resolveEntityName(categoriesList, catId)}
                    </span>
                  ))
                : <span className="text-gray-400 italic text-sm">No categories</span>}
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
