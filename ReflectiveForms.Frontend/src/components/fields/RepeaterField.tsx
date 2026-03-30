import { useFieldArray, useFormContext } from 'react-hook-form';
import { Plus, Trash2, ChevronUp, ChevronDown } from 'lucide-react';
import { FormField } from './FormField';
import { FieldComponentProps } from './types';
import { FieldSchema } from '../../types/schema';

/** Height of each repeater sticky header in pixels (py-2 + text-sm line) */
export const REPEATER_HEADER_HEIGHT = 37;
/** Height of the top navigation bar in pixels (h-16 = 4rem) */
export const TOP_BAR_HEIGHT = 64;

export function RepeaterField({ schema, path, depth = 0 }: FieldComponentProps) {
  const { control } = useFormContext();
  const { fields, append, remove, move } = useFieldArray({
    control,
    name: path,
  });

  const itemSchema = schema.repeater_options?.item_schema ?? [];
  const minItems = schema.repeater_options?.min_items;
  const maxItems = schema.repeater_options?.max_items;
  const addButtonLabel = schema.repeater_options?.add_button_label ?? 'Add Item';
  const useAccordion = schema.repeater_options?.use_accordion ?? false;

  const canAdd = maxItems == null || fields.length < maxItems;
  const canRemove = minItems == null || fields.length > minItems;

  const handleAdd = () => {
    // Generate default values for new item
    const defaultItem: Record<string, unknown> = {
      _unique_field_id: generateRandomId(),
    };
    for (const field of itemSchema) {
      defaultItem[field.name] = field.default_value ?? getDefaultForType(field.type);
    }
    append(defaultItem);
  };

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
          canRemove={canRemove}
          canMoveUp={index > 0}
          canMoveDown={index < fields.length - 1}
          onRemove={() => remove(index)}
          onMoveUp={() => move(index, index - 1)}
          onMoveDown={() => move(index, index + 1)}
          label={schema.label}
        />
      ))}

      {canAdd && (
        <button
          type="button"
          onClick={handleAdd}
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
  canRemove: boolean;
  canMoveUp: boolean;
  canMoveDown: boolean;
  onRemove: () => void;
  onMoveUp: () => void;
  onMoveDown: () => void;
  label: string;
}

function RepeaterItem({
  index,
  path,
  itemSchema,
  depth,
  useAccordion: _useAccordion,
  canRemove,
  canMoveUp,
  canMoveDown,
  onRemove,
  onMoveUp,
  onMoveDown,
  label,
}: RepeaterItemProps) {
  const itemPath = `${path}.${index}`;

  return (
    <div className="border border-gray-200 rounded-lg overflow-visible">
      {/* Item header — sticky so users always see which item they're editing */}
      <div
        className="flex items-center justify-between bg-gray-50 dark:bg-gray-700 px-4 py-2 border-b border-gray-200 sticky rounded-t-lg"
        style={{
          top: `${TOP_BAR_HEIGHT + depth * REPEATER_HEADER_HEIGHT}px`,
          zIndex: 10 - depth,
        }}
        data-testid={`repeater-header-depth-${depth}`}
      >
        <span className="font-medium text-gray-700 dark:text-gray-200">
          {label} #{index + 1}
        </span>
        <div className="flex items-center gap-1">
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

      {/* Item content */}
      <div className="p-4">
        {itemSchema.map((fieldSchema) => (
          <FormField
            key={fieldSchema.name}
            fieldSchema={fieldSchema}
            basePath={itemPath}
            depth={depth + 1}
          />
        ))}
      </div>
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
