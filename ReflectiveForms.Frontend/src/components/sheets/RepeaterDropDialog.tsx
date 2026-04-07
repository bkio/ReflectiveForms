import { useState } from 'react';
import { Hash, List, ArrowRight, X } from 'lucide-react';
import type { FieldSchema } from '../../types/schema';

export type RepeaterFormulaChoice =
  | { type: 'count' }
  | { type: 'list'; subField: string }
  | { type: 'field'; subField: string };

export interface RepeaterDropDialogProps {
  entityName: string;
  repeaterPath: string;
  repeaterLabel: string;
  itemSchema: FieldSchema[];
  onConfirm: (choice: RepeaterFormulaChoice) => void;
  onCancel: () => void;
}

/**
 * Dialog shown when a Repeater field is dropped onto the spreadsheet.
 * Lets the user choose which RF formula to generate:
 *   - RF.REPEATCOUNT — row count
 *   - RF.REPEAT — spill array of a sub-field
 *   - RF.REPEATFIELD — single indexed value of a sub-field
 *
 * For nested Groups inside the repeater, sub-fields are shown with dot-paths.
 */
export function RepeaterDropDialog({
  entityName,
  repeaterPath,
  repeaterLabel,
  itemSchema,
  onConfirm,
  onCancel,
}: RepeaterDropDialogProps) {
  const [mode, setMode] = useState<'count' | 'list' | 'field'>('list');
  const [selectedSubField, setSelectedSubField] = useState(flattenFields(itemSchema)[0]?.path ?? '');

  const flatFields = flattenFields(itemSchema);

  const handleConfirm = () => {
    if (mode === 'count') {
      onConfirm({ type: 'count' });
    } else {
      onConfirm({ type: mode, subField: selectedSubField });
    }
  };

  return (
    // Backdrop
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/40"
      onClick={onCancel}
    >
      {/* Dialog */}
      <div
        className="bg-white dark:bg-gray-800 rounded-xl shadow-2xl w-[420px] max-h-[80vh] flex flex-col"
        onClick={(e) => e.stopPropagation()}
      >
        {/* Header */}
        <div className="px-5 py-4 border-b border-gray-200 dark:border-gray-700 flex items-center justify-between">
          <div>
            <h3 className="text-sm font-semibold text-gray-900 dark:text-gray-100">
              Insert Repeater Formula
            </h3>
            <p className="text-xs text-gray-500 dark:text-gray-400 mt-0.5">
              <span className="font-medium">{entityName}</span>
              {' → '}
              <span className="font-medium">{repeaterLabel}</span>
            </p>
          </div>
          <button
            onClick={onCancel}
            className="p-1 text-gray-400 hover:text-gray-600 dark:hover:text-gray-300 rounded"
          >
            <X className="w-4 h-4" />
          </button>
        </div>

        {/* Mode selection */}
        <div className="px-5 py-3 space-y-1.5">
          <ModeOption
            icon={<Hash className="w-4 h-4" />}
            label="Count"
            description={`RF.REPEATCOUNT — number of items in ${repeaterPath}`}
            selected={mode === 'count'}
            onClick={() => setMode('count')}
          />
          <ModeOption
            icon={<List className="w-4 h-4" />}
            label="List all values"
            description="RF.REPEAT — spill array of a sub-field across all items"
            selected={mode === 'list'}
            onClick={() => setMode('list')}
          />
          <ModeOption
            icon={<ArrowRight className="w-4 h-4" />}
            label="Single value at index"
            description="RF.REPEATFIELD — one value from a specific item index"
            selected={mode === 'field'}
            onClick={() => setMode('field')}
          />
        </div>

        {/* Sub-field picker (for list / field modes) */}
        {mode !== 'count' && (
          <div className="px-5 pb-3">
            <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1.5">
              Sub-field
            </label>
            <select
              value={selectedSubField}
              onChange={(e) => setSelectedSubField(e.target.value)}
              className="w-full text-sm border border-gray-300 dark:border-gray-600 rounded-lg px-3 py-1.5 bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100 focus:ring-2 focus:ring-primary-500 focus:outline-none"
            >
              {flatFields.map((f) => (
                <option key={f.path} value={f.path}>
                  {f.path} — {f.type}
                </option>
              ))}
            </select>
          </div>
        )}

        {/* Formula preview */}
        <div className="px-5 pb-3">
          <div className="text-[11px] font-mono text-gray-500 dark:text-gray-400 bg-gray-50 dark:bg-gray-900 rounded-lg px-3 py-2 break-all">
            {mode === 'count' && `=RF.REPEATCOUNT("${entityName}", <id>, "${repeaterPath}")`}
            {mode === 'list' && `=RF.REPEAT("${entityName}", <id>, "${repeaterPath}", "${selectedSubField}")`}
            {mode === 'field' && `=RF.REPEATFIELD("${entityName}", <id>, "${repeaterPath}", 0, "${selectedSubField}")`}
          </div>
          <p className="text-[10px] text-gray-400 dark:text-gray-500 mt-1">
            {mode === 'list'
              ? 'Inserts a column with IDs and this formula referencing each ID.'
              : mode === 'field'
                ? 'Uses index 0 — edit the formula to change the item index.'
                : 'Inserts header + formula in the active cell area.'}
          </p>
        </div>

        {/* Actions */}
        <div className="px-5 py-3 border-t border-gray-200 dark:border-gray-700 flex justify-end gap-2">
          <button
            onClick={onCancel}
            className="px-3 py-1.5 text-sm text-gray-600 dark:text-gray-400 hover:bg-gray-100 dark:hover:bg-gray-700 rounded-lg transition-colors"
          >
            Cancel
          </button>
          <button
            onClick={handleConfirm}
            className="px-4 py-1.5 text-sm bg-primary-600 text-white rounded-lg hover:bg-primary-700 transition-colors"
          >
            Insert
          </button>
        </div>
      </div>
    </div>
  );
}

function ModeOption({
  icon,
  label,
  description,
  selected,
  onClick,
}: {
  icon: React.ReactNode;
  label: string;
  description: string;
  selected: boolean;
  onClick: () => void;
}) {
  return (
    <button
      onClick={onClick}
      className={`w-full flex items-start gap-3 px-3 py-2 rounded-lg text-left transition-colors ${
        selected
          ? 'bg-primary-50 dark:bg-primary-900/30 border border-primary-300 dark:border-primary-700'
          : 'hover:bg-gray-50 dark:hover:bg-gray-750 border border-transparent'
      }`}
    >
      <span className={`mt-0.5 ${selected ? 'text-primary-600 dark:text-primary-400' : 'text-gray-400'}`}>
        {icon}
      </span>
      <div className="flex-1 min-w-0">
        <span className={`text-sm font-medium ${selected ? 'text-primary-700 dark:text-primary-300' : 'text-gray-700 dark:text-gray-300'}`}>
          {label}
        </span>
        <p className="text-[11px] text-gray-500 dark:text-gray-400 mt-0.5 leading-tight">
          {description}
        </p>
      </div>
    </button>
  );
}

/**
 * Flattens a repeater's item_schema into dot-path entries.
 * Groups are recursively expanded; Repeaters are listed as-is (not expanded).
 */
function flattenFields(
  schema: FieldSchema[],
  prefix = '',
): Array<{ path: string; type: string }> {
  const result: Array<{ path: string; type: string }> = [];
  for (const field of schema) {
    const path = prefix ? `${prefix}.${field.name}` : field.name;
    if (field.type === 'Group' && field.group_options?.child_schema) {
      result.push(...flattenFields(field.group_options.child_schema, path));
    } else {
      result.push({ path, type: field.type });
    }
  }
  return result;
}
