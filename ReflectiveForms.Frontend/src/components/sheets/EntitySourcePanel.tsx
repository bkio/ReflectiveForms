import { useState } from 'react';
import { ChevronDown, ChevronRight, GripVertical, Database, Lock } from 'lucide-react';
import type { AllCapabilities, EntitySchema, FieldSchema } from '../../types/schema';

export interface EntitySourcePanelProps {
  /** All schemas the user can see */
  schemas: Record<string, EntitySchema>;
  /** Entities the user added as sources to this sheet */
  activeSources: string[];
  /** Entities the current user doesn't have permission for */
  unauthorizedEntities: Set<string>;
  /** Per-entity-type capabilities for the current user */
  capabilities?: AllCapabilities;
  /** Called when user wants to add a source entity */
  onAddSource: (entityName: string) => void;
  /** Called when user wants to remove a source entity */
  onRemoveSource: (entityName: string) => void;
}

export function EntitySourcePanel({
  schemas,
  activeSources,
  unauthorizedEntities,
  capabilities,
  onAddSource,
  onRemoveSource,
}: EntitySourcePanelProps) {
  const [expandedEntities, setExpandedEntities] = useState<Set<string>>(new Set(activeSources));
  const [showEntityPicker, setShowEntityPicker] = useState(false);

  const toggleExpand = (entityName: string) => {
    setExpandedEntities((prev) => {
      const next = new Set(prev);
      if (next.has(entityName)) {
        next.delete(entityName);
      } else {
        next.add(entityName);
      }
      return next;
    });
  };

  const availableEntities = Object.keys(schemas).filter(
    (name) => !activeSources.includes(name) && name !== 'rf-sheets'
      && (!capabilities || capabilities[name]?.can_peek_all || capabilities[name]?.can_read),
  );

  return (
    <div className="w-64 border-r border-gray-200 dark:border-gray-700 bg-gray-50 dark:bg-gray-900 flex flex-col h-full overflow-hidden">
      {/* Header */}
      <div className="p-3 border-b border-gray-200 dark:border-gray-700 flex items-center justify-between">
        <h3 className="text-sm font-semibold text-gray-700 dark:text-gray-300 flex items-center gap-1.5">
          <Database className="w-4 h-4" />
          Entity Sources
        </h3>
        <button
          onClick={() => setShowEntityPicker(!showEntityPicker)}
          className="text-xs px-2 py-1 bg-primary-600 text-white rounded hover:bg-primary-700 transition-colors"
        >
          + Add
        </button>
      </div>

      {/* Entity Picker Dropdown */}
      {showEntityPicker && availableEntities.length > 0 && (
        <div className="p-2 border-b border-gray-200 dark:border-gray-700 bg-white dark:bg-gray-800 max-h-48 overflow-y-auto">
          {availableEntities.map((name) => (
            <button
              key={name}
              onClick={() => {
                onAddSource(name);
                setExpandedEntities((prev) => new Set(prev).add(name));
                setShowEntityPicker(false);
              }}
              className="w-full text-left px-2 py-1.5 text-sm text-gray-700 dark:text-gray-300 hover:bg-gray-100 dark:hover:bg-gray-700 rounded transition-colors"
            >
              {schemas[name]?.readable_name?.singular ?? name}
            </button>
          ))}
        </div>
      )}

      {/* Source List */}
      <div className="flex-1 overflow-y-auto">
        {activeSources.length === 0 && (
          <div className="p-4 text-center text-sm text-gray-400 dark:text-gray-500">
            No entity sources added yet. Click &quot;+ Add&quot; to begin.
          </div>
        )}

        {activeSources.map((entityName) => {
          const schema = schemas[entityName];
          const isExpanded = expandedEntities.has(entityName);
          const isUnauthorized = unauthorizedEntities.has(entityName);

          return (
            <div key={entityName} className="border-b border-gray-100 dark:border-gray-800">
              {/* Entity Header */}
              <div
                role="button"
                tabIndex={0}
                onClick={() => toggleExpand(entityName)}
                onKeyDown={(e) => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); toggleExpand(entityName); } }}
                className="w-full flex items-center gap-2 px-3 py-2 text-sm font-medium text-gray-700 dark:text-gray-300 hover:bg-gray-100 dark:hover:bg-gray-800 transition-colors cursor-pointer"
              >
                {isExpanded ? (
                  <ChevronDown className="w-3.5 h-3.5 flex-shrink-0" />
                ) : (
                  <ChevronRight className="w-3.5 h-3.5 flex-shrink-0" />
                )}
                <span className="flex-1 text-left truncate">
                  {schema?.readable_name?.singular ?? entityName}
                </span>
                {isUnauthorized && <Lock className="w-3.5 h-3.5 text-amber-500 flex-shrink-0" />}
                <button
                  onClick={(e) => {
                    e.stopPropagation();
                    onRemoveSource(entityName);
                  }}
                  className="text-xs text-gray-400 hover:text-red-500 transition-colors px-1"
                  title="Remove source"
                >
                  ×
                </button>
              </div>

              {/* Field List */}
              {isExpanded && schema && !isUnauthorized && (
                <div className="pb-1">
                  {/* ID field (always available) */}
                  <FieldItem
                    entityName={entityName}
                    fieldName="id"
                    fieldLabel="ID"
                    fieldType="Number"
                  />
                  {schema.fields.map((field) =>
                    field.type === 'Group' && field.group_options?.child_schema ? (
                      <GroupFieldItems
                        key={field.name}
                        entityName={entityName}
                        parentPath={field.name}
                        parentLabel={field.label}
                        childSchema={field.group_options.child_schema}
                      />
                    ) : (
                      <FieldItem
                        key={field.name}
                        entityName={entityName}
                        fieldName={field.name}
                        fieldLabel={field.label}
                        fieldType={field.type}
                      />
                    ),
                  )}
                </div>
              )}

              {isExpanded && isUnauthorized && (
                <div className="px-4 py-2 text-xs text-amber-600 dark:text-amber-400">
                  No access to this entity&apos;s data.
                </div>
              )}
            </div>
          );
        })}
      </div>
    </div>
  );
}

function FieldItem({
  entityName,
  fieldName,
  fieldLabel,
  fieldType,
  indent = 0,
}: {
  entityName: string;
  fieldName: string;
  fieldLabel: string;
  fieldType: string;
  indent?: number;
}) {
  return (
    <div
      draggable
      onDragStart={(e) => {
        e.dataTransfer.setData(
          'application/rf-sheet-field',
          JSON.stringify({ entity: entityName, field: fieldName, label: fieldLabel }),
        );
      }}
      className="flex items-center gap-1.5 py-1 text-xs text-gray-600 dark:text-gray-400 hover:bg-gray-100 dark:hover:bg-gray-800 cursor-grab active:cursor-grabbing transition-colors"
      style={{ paddingLeft: `${16 + indent * 12}px`, paddingRight: '16px' }}
    >
      <GripVertical className="w-3 h-3 text-gray-300 dark:text-gray-600 flex-shrink-0" />
      <span className="flex-1 truncate">{fieldLabel}</span>
      <span className="text-[10px] text-gray-400 dark:text-gray-600 flex-shrink-0">{fieldType}</span>
    </div>
  );
}

/**
 * Renders a Group field's sub-fields as dot-notation paths.
 * Recursively expands nested groups (group-in-group).
 */
function GroupFieldItems({
  entityName,
  parentPath,
  parentLabel,
  childSchema,
  depth = 0,
}: {
  entityName: string;
  parentPath: string;
  parentLabel: string;
  childSchema: FieldSchema[];
  depth?: number;
}) {
  return (
    <>
      {/* Group header (non-draggable) */}
      <div
        className="flex items-center gap-1.5 py-1 text-xs font-medium text-gray-500 dark:text-gray-500"
        style={{ paddingLeft: `${16 + depth * 12}px`, paddingRight: '16px' }}
      >
        <span className="truncate">{parentLabel}</span>
        <span className="text-[10px] text-gray-400 dark:text-gray-600 flex-shrink-0">Group</span>
      </div>
      {childSchema.map((child) => {
        const dotPath = `${parentPath}.${child.name}`;
        const dotLabel = `${parentPath}.${child.name}`;
        if (child.type === 'Group' && child.group_options?.child_schema) {
          return (
            <GroupFieldItems
              key={dotPath}
              entityName={entityName}
              parentPath={dotPath}
              parentLabel={dotLabel}
              childSchema={child.group_options.child_schema}
              depth={depth + 1}
            />
          );
        }
        return (
          <FieldItem
            key={dotPath}
            entityName={entityName}
            fieldName={dotPath}
            fieldLabel={dotLabel}
            fieldType={child.type}
            indent={depth + 1}
          />
        );
      })}
    </>
  );
}
