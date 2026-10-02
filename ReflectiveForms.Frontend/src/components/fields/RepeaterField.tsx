import { useEffect, useState } from 'react';
import { useFieldArray, useFormContext, useWatch } from 'react-hook-form';
import { Plus, Trash2, ChevronUp, ChevronDown, ChevronRight, BetweenHorizontalStart } from 'lucide-react';
import { FormField } from './FormField';
import { FieldComponentProps } from './types';
import { FieldSchema, GroupRenderStyle } from '../../types/schema';

/** Max characters to show from the sticky title field value */
const STICKY_TITLE_MAX_CHARS = 40;

/** Height of each repeater sticky header in pixels (py-2 + text-sm line) */
export const REPEATER_HEADER_HEIGHT = 37;
/** Height of the top navigation bar in pixels (h-16 = 4rem) */
export const TOP_BAR_HEIGHT = 64;

const gridClassMap: Record<GroupRenderStyle, string> = {
  Full: 'grid-cols-1',
  Grid2: 'grid-cols-1 md:grid-cols-2',
  Grid3: 'grid-cols-1 md:grid-cols-3',
  Grid4: 'grid-cols-1 md:grid-cols-2 lg:grid-cols-4',
  Grid6: 'grid-cols-1 md:grid-cols-2 lg:grid-cols-3 xl:grid-cols-6',
};

export function RepeaterField({ schema, path, depth = 0 }: FieldComponentProps) {
  const { control } = useFormContext();
  const { fields, append, insert, remove, move } = useFieldArray({
    control,
    name: path,
  });

  const itemSchema = schema.repeater_options?.item_schema ?? [];
  const minItems = schema.repeater_options?.min_items;
  const maxItems = schema.repeater_options?.max_items;
  const addButtonLabel = schema.repeater_options?.add_button_label ?? 'Add Item';
  const useAccordion = schema.repeater_options?.use_accordion ?? false;
  const stickyTitleField = schema.repeater_options?.sticky_title_field ?? null;
  const renderStyle = schema.repeater_options?.render_style ?? 'Full';

  const canAdd = maxItems == null || fields.length < maxItems;
  const canRemove = minItems == null || fields.length > minItems;

  // Accordion state: track which items are expanded (by field array id)
  const [expandedIds, setExpandedIds] = useState<Set<string>>(() => {
    // When not in accordion mode, everything is expanded (doesn't matter since we won't check).
    // When in accordion mode, start all collapsed.
    return new Set<string>();
  });

  const toggleExpanded = (id: string) => {
    setExpandedIds((prev) => {
      const next = new Set(prev);
      if (next.has(id)) {
        next.delete(id);
      } else {
        next.add(id);
      }
      return next;
    });
  };

  const createDefaultItem = () => {
    // Generate default values for new item
    const defaultItem: Record<string, unknown> = {
      _unique_field_id: generateRandomId(),
    };
    for (const field of itemSchema) {
      defaultItem[field.name] = field.default_value ?? getDefaultForType(field.type);
    }
    return defaultItem;
  };

  // Index of a just-added item that should open once react-hook-form has
  // given it a field id (accordion mode only).
  const [pendingExpandIndex, setPendingExpandIndex] = useState<number | null>(null);

  const handleAddAndExpand = () => {
    if (useAccordion) setPendingExpandIndex(fields.length);
    append(createDefaultItem());
  };

  const handleInsertBefore = (index: number) => {
    if (useAccordion) setPendingExpandIndex(index);
    insert(index, createDefaultItem());
  };

  // Resolve pending expansion for newly added items. The current render
  // already shows it open; the effect makes that stick.
  const pendingFieldId = pendingExpandIndex !== null ? fields[pendingExpandIndex]?.id : undefined;
  const resolvedExpandedIds = new Set(expandedIds);
  if (pendingFieldId) resolvedExpandedIds.add(pendingFieldId);
  useEffect(() => {
    if (!pendingFieldId) return;
    setExpandedIds((prev) => new Set([...prev, pendingFieldId]));
    setPendingExpandIndex(null);
  }, [pendingFieldId]);

  return (
    <div className="space-y-4">
      {fields.map((field, index) => (
        <RepeaterItem
          key={field.id}
          index={index}
          path={path}
          itemSchema={itemSchema}
          depth={depth}
          useAccordion={useAccordion}
          isExpanded={!useAccordion || resolvedExpandedIds.has(field.id)}
          onToggleExpand={() => toggleExpanded(field.id)}
          canRemove={canRemove}
          canInsert={canAdd}
          canMoveUp={index > 0}
          canMoveDown={index < fields.length - 1}
          onRemove={() => remove(index)}
          onInsertBefore={() => handleInsertBefore(index)}
          onMoveUp={() => move(index, index - 1)}
          onMoveDown={() => move(index, index + 1)}
          label={schema.label}
          stickyTitleField={stickyTitleField}
          renderStyle={renderStyle}
        />
      ))}

      {canAdd && (
        <button
          type="button"
          onClick={handleAddAndExpand}
          className="
            flex items-center gap-2 px-4 py-2
            bg-blue-50 text-blue-600 rounded-md
            hover:bg-blue-100 transition-colors
          "
        >
          <Plus className="w-4 h-4" />
          {addButtonLabel}
        </button>
      )}
    </div>
  );
}

interface RepeaterItemProps {
  index: number;
  path: string;
  itemSchema: FieldSchema[];
  depth: number;
  useAccordion: boolean;
  isExpanded: boolean;
  onToggleExpand: () => void;
  canRemove: boolean;
  canInsert: boolean;
  canMoveUp: boolean;
  canMoveDown: boolean;
  onRemove: () => void;
  onInsertBefore: () => void;
  onMoveUp: () => void;
  onMoveDown: () => void;
  label: string;
  stickyTitleField: string | null;
  renderStyle: GroupRenderStyle;
}

function RepeaterItem({
  index,
  path,
  itemSchema,
  depth,
  useAccordion,
  isExpanded,
  onToggleExpand,
  canRemove,
  canInsert,
  canMoveUp,
  canMoveDown,
  onRemove,
  onInsertBefore,
  onMoveUp,
  onMoveDown,
  label,
  stickyTitleField,
  renderStyle,
}: RepeaterItemProps) {
  const itemPath = `${path}.${index}`;

  // Resolve the watch path: supports dotted paths like "title.rendered"
  const watchPath = stickyTitleField ? `${itemPath}.${stickyTitleField}` : undefined;
  const stickyValue = useWatch({ name: watchPath as string, disabled: !watchPath });

  // Build the display suffix from the watched value
  let stickyPreview = '';
  if (stickyTitleField && stickyValue != null && String(stickyValue).trim() !== '') {
    const raw = String(stickyValue).trim();
    stickyPreview = raw.length > STICKY_TITLE_MAX_CHARS
      ? raw.slice(0, STICKY_TITLE_MAX_CHARS) + '…'
      : raw;
  }

  const gridClass = gridClassMap[renderStyle] ?? gridClassMap.Full;

  return (
    <div className="border border-gray-200 rounded-lg overflow-visible">
      {/* Item header — sticky so users always see which item they're editing */}
      <div
        className={`flex items-center justify-between bg-gray-50 dark:bg-gray-700 px-4 py-2 border-b border-gray-200 sticky rounded-t-lg gap-2${useAccordion ? ' cursor-pointer select-none' : ''}`}
        style={{
          top: `${TOP_BAR_HEIGHT + depth * REPEATER_HEADER_HEIGHT}px`,
          zIndex: 10 - depth,
        }}
        data-testid={`repeater-header-depth-${depth}`}
        onClick={useAccordion ? onToggleExpand : undefined}
      >
        {useAccordion && (
          <ChevronRight
            className={`w-4 h-4 text-gray-500 dark:text-gray-400 transition-transform flex-shrink-0 ${isExpanded ? 'rotate-90' : ''}`}
            data-testid={`accordion-chevron-${index}`}
          />
        )}
        <span className="font-medium text-gray-700 dark:text-gray-200 min-w-0 truncate flex-1" data-testid={`repeater-title-${index}`}>
          {label} #{index + 1}
          {stickyPreview && (
            <span className="font-normal text-gray-500 dark:text-gray-400"> — {stickyPreview}</span>
          )}
        </span>
        <div className="flex items-center gap-1 flex-shrink-0" onClick={(e) => e.stopPropagation()}>
          {canInsert && (
            <button
              type="button"
              onClick={onInsertBefore}
              className="p-1 text-gray-500 hover:text-gray-700"
              title="Insert before"
              aria-label="Insert before"
              data-testid={`repeater-insert-before-${index}`}
            >
              <BetweenHorizontalStart className="w-4 h-4" />
            </button>
          )}
          <button
            type="button"
            onClick={onMoveUp}
            disabled={!canMoveUp}
            className="p-1 text-gray-500 hover:text-gray-700 disabled:opacity-30 disabled:cursor-not-allowed"
            title="Move up"
          >
            <ChevronUp className="w-4 h-4" />
          </button>
          <button
            type="button"
            onClick={onMoveDown}
            disabled={!canMoveDown}
            className="p-1 text-gray-500 hover:text-gray-700 disabled:opacity-30 disabled:cursor-not-allowed"
            title="Move down"
          >
            <ChevronDown className="w-4 h-4" />
          </button>
          {canRemove && (
            <button
              type="button"
              onClick={onRemove}
              className="p-1 text-red-500 hover:text-red-700"
              title="Remove"
            >
              <Trash2 className="w-4 h-4" />
            </button>
          )}
        </div>
      </div>

      {/* Item content — collapsed when accordion is active and item is not expanded */}
      {isExpanded && (
        <div className={`p-4 grid ${gridClass} gap-4`}>
          {itemSchema.map((fieldSchema) => (
            <FormField
              key={fieldSchema.name}
              fieldSchema={fieldSchema}
              basePath={itemPath}
              depth={depth + 1}
            />
          ))}
        </div>
      )}
    </div>
  );
}

function generateRandomId(): string {
  const chars = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ';
  let result = '';
  for (let i = 0; i < 16; i++) {
    result += chars.charAt(Math.floor(Math.random() * chars.length));
  }
  return result;
}

function getDefaultForType(type: string): unknown {
  switch (type) {
    case 'Text':
    case 'TextArea':
    case 'Email':
    case 'Url':
    case 'DatePicker':
      return '';
    case 'Number':
    case 'Range':
      return 0;
    case 'Checkbox':
      return false;
    case 'Select':
      return '';
    case 'Relation':
      return -1;
    case 'Group':
      return {};
    case 'Repeater':
      return [];
    default:
      return null;
  }
}
