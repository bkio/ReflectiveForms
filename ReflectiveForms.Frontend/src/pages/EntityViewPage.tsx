import { useParams, useSearchParams, Link } from 'react-router-dom';
import { useSchema, useEntity } from '../hooks/useEntity';
import { FieldSchema, EntitySchema } from '../types/schema';
import { sanitizeHtml } from '../lib/sanitize';
import { evaluateCompoundCondition } from '../lib/conditionParser';
import { ArrowLeft, Edit } from 'lucide-react';

export function EntityViewPage() {
  const { entityName } = useParams<{ entityName: string }>();
  const [searchParams] = useSearchParams();
  const idParam = searchParams.get('id');
  const entityId = idParam ? parseInt(idParam, 10) : undefined;

  const { data: schema, isLoading: schemaLoading, error: schemaError } = useSchema(entityName ?? '');
  const { data: entityData, isLoading: entityLoading, error: entityError } = useEntity(entityName ?? '', entityId);

  if (schemaLoading || entityLoading) {
    return (
      <div className="min-h-screen bg-gray-50 flex items-center justify-center">
        <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-blue-600"></div>
      </div>
    );
  }

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

  if (!schema || !entityData) {
    return (
      <div className="min-h-screen bg-gray-50 flex items-center justify-center">
        <div className="text-gray-500">Entity not found</div>
      </div>
    );
  }

  const title = entityData.title;
  const fields = entityData.fields ?? {};

  return (
    <div className="min-h-screen bg-gray-50">
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
              <h1 className="text-2xl font-bold text-gray-900">
                {title?.rendered || 'Untitled'}
              </h1>
            </div>
            <p className="ml-8 text-sm text-gray-500">
              {schema.readable_name.singular} — ID: {entityId}
            </p>
          </div>
          <Link
            to={`/entities-admin/${entityName}?id=${entityId}`}
            className="flex items-center gap-2 px-4 py-2 bg-blue-600 text-white rounded-md hover:bg-blue-700 transition-colors"
            title="Edit"
          >
            <Edit className="w-4 h-4" />
            Edit
          </Link>
        </div>

        {/* Fields */}
        <div className="space-y-4">
          {schema.fields.map((fieldSchema) => (
            <ReadOnlyField
              key={fieldSchema.name}
              fieldSchema={fieldSchema}
              value={fields[fieldSchema.name]}
              allFields={fields}
              schema={schema}
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
}

function ReadOnlyField({ fieldSchema, value, allFields, schema, depth = 0 }: ReadOnlyFieldProps) {
  // Evaluate display condition
  if (fieldSchema.display_condition) {
    if (!evaluateCompoundCondition(fieldSchema.display_condition, allFields)) {
      return null;
    }
  }

  return (
    <div className={`field-view field-type-${fieldSchema.type.toLowerCase()} ${depth > 0 ? 'ml-4' : ''}`}>
      <div className="bg-white rounded-lg shadow-sm border border-gray-200 p-4">
        <div className="mb-1">
          <span className="text-sm font-medium text-gray-500">
            {fieldSchema.label}
          </span>
        </div>
        <ReadOnlyValue fieldSchema={fieldSchema} value={value} allFields={allFields} schema={schema} depth={depth} />
      </div>
    </div>
  );
}

interface ReadOnlyValueProps {
  fieldSchema: FieldSchema;
  value: unknown;
  allFields: Record<string, unknown>;
  schema: EntitySchema;
  depth?: number;
}

function ReadOnlyValue({ fieldSchema, value, schema, depth = 0 }: ReadOnlyValueProps) {
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
      return <span className="text-gray-900">{String(value)}</span>;

    case 'TextArea':
      return <p className="text-gray-900 whitespace-pre-wrap">{String(value)}</p>;

    case 'WysiwygEditor':
      return (
        <div
          className="prose prose-sm max-w-none text-gray-900"
          dangerouslySetInnerHTML={{ __html: sanitizeHtml(String(value)) }}
        />
      );

    case 'Number':
    case 'Range':
      return <span className="text-gray-900 font-mono">{String(value)}</span>;

    case 'Checkbox':
      return (
        <span className={`inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium ${value ? 'bg-green-100 text-green-800' : 'bg-gray-100 text-gray-800'}`}>
          {value ? 'Yes' : 'No'}
        </span>
      );

    case 'DatePicker': {
      const dateStr = String(value);
      // Format yyyyMMdd to yyyy-MM-dd
      if (/^\d{8}$/.test(dateStr)) {
        return <span className="text-gray-900">{`${dateStr.slice(0, 4)}-${dateStr.slice(4, 6)}-${dateStr.slice(6, 8)}`}</span>;
      }
      return <span className="text-gray-900">{dateStr}</span>;
    }

    case 'Select': {
      const strVal = String(value);
      const choice = fieldSchema.select_options?.choices?.find(c => c.value === strVal);
      return (
        <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-blue-100 text-blue-800">
          {choice?.label ?? strVal}
        </span>
      );
    }

    case 'Relation': {
      const relVal = Number(value);
      if (relVal <= 0) return <span className="text-gray-400 italic text-sm">None</span>;
      return <span className="text-gray-900">ID: {relVal}</span>;
    }

    case 'MediaSourceBase64': {
      const mediaVal = String(value);
      if (!mediaVal) return <span className="text-gray-400 italic text-sm">No media</span>;
      return (
        <div className="mt-1">
          <img
            src={mediaVal.startsWith('data:') ? mediaVal : mediaVal}
            alt={fieldSchema.label}
            className="max-w-xs max-h-48 object-contain rounded border border-gray-200"
          />
        </div>
      );
    }

    case 'Group': {
      const groupVal = (value && typeof value === 'object') ? value as Record<string, unknown> : {};
      const childFields = fieldSchema.group_options?.child_schema ?? [];
      if (childFields.length === 0) return <span className="text-gray-400 italic text-sm">Empty</span>;
      return (
        <div className="space-y-3 mt-2">
          {childFields.map(child => (
            <ReadOnlyField
              key={child.name}
              fieldSchema={child}
              value={groupVal[child.name]}
              allFields={groupVal}
              schema={schema}
              depth={depth + 1}
            />
          ))}
        </div>
      );
    }

    case 'Repeater': {
      const items = Array.isArray(value) ? value : [];
      const itemFields = fieldSchema.repeater_options?.item_schema ?? [];
      if (items.length === 0) return <span className="text-gray-400 italic text-sm">No items</span>;
      return (
        <div className="space-y-3 mt-2">
          {items.map((item, idx) => {
            const itemObj = (item && typeof item === 'object') ? item as Record<string, unknown> : {};
            return (
              <div key={idx} className="border border-gray-200 rounded-lg p-3 bg-gray-50">
                <div className="text-xs font-medium text-gray-500 mb-2">Item {idx + 1}</div>
                <div className="space-y-2">
                  {itemFields.map(child => (
                    <ReadOnlyField
                      key={child.name}
                      fieldSchema={child}
                      value={itemObj[child.name]}
                      allFields={itemObj}
                      schema={schema}
                      depth={depth + 1}
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
      return <span className="text-gray-900">{JSON.stringify(value)}</span>;
  }
}
