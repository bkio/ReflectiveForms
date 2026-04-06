import { useState, useEffect, useRef } from 'react';
import type { EntitySchema } from '../../types/schema';

interface FormulaAutocompleteProps {
  /** All schemas available to the user */
  schemas: Record<string, EntitySchema>;
  /** Current text in the formula bar / cell editor */
  inputValue: string;
  /** Position to render the autocomplete popup */
  position: { x: number; y: number } | null;
  /** Called when user selects a suggestion */
  onSelect: (suggestion: string) => void;
  /** Called when dismissed */
  onDismiss: () => void;
}

const RF_FUNCTIONS = [
  { name: 'RF.FIELD', signature: 'RF.FIELD(entity, id, fieldName)', desc: 'Get a field value for an entity' },
  { name: 'RF.TITLE', signature: 'RF.TITLE(entity, id)', desc: 'Get the title of an entity' },
  { name: 'RF.LIST', signature: 'RF.LIST(entity, fieldName)', desc: 'List all values of a field (spills down)' },
  { name: 'RF.LOOKUP', signature: 'RF.LOOKUP(entity, matchField, matchValue, returnField)', desc: 'Look up a field by matching' },
  { name: 'RF.COUNT', signature: 'RF.COUNT(entity)', desc: 'Count rows for an entity' },
  { name: 'RF.SUM', signature: 'RF.SUM(entity, field)', desc: 'Sum a numeric field' },
  { name: 'RF.AVG', signature: 'RF.AVG(entity, field)', desc: 'Average a numeric field' },
  { name: 'RF.IDS', signature: 'RF.IDS(entity)', desc: 'List all entity IDs (spills down)' },
  { name: 'RF.FILTER', signature: 'RF.FILTER(entity, field, filterField, filterValue)', desc: 'Filtered list of a field' },
  { name: 'RF.MATCH', signature: 'RF.MATCH(entity, id, field, operator, value)', desc: 'Conditional check (true/false)' },
  { name: 'RF.MATCHLIST', signature: 'RF.MATCHLIST(entity, field, operator, value)', desc: 'Conditional check per row (spills)' },
];

type SuggestionMode = 'functions' | 'entities' | 'fields' | 'none';

interface Suggestion {
  text: string;
  insertText: string;
  detail?: string;
}

export function FormulaAutocomplete({
  schemas,
  inputValue,
  position,
  onSelect,
  onDismiss,
}: FormulaAutocompleteProps) {
  const [selectedIndex, setSelectedIndex] = useState(0);
  const listRef = useRef<HTMLDivElement>(null);

  const { mode, suggestions, context } = parseSuggestions(inputValue, schemas);

  useEffect(() => {
    setSelectedIndex(0);
  }, [inputValue]);

  useEffect(() => {
    if (!position || mode === 'none') return;

    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'ArrowDown') {
        e.preventDefault();
        setSelectedIndex((prev) => Math.min(prev + 1, suggestions.length - 1));
      } else if (e.key === 'ArrowUp') {
        e.preventDefault();
        setSelectedIndex((prev) => Math.max(prev - 1, 0));
      } else if (e.key === 'Enter' || e.key === 'Tab') {
        if (suggestions[selectedIndex]) {
          e.preventDefault();
          onSelect(suggestions[selectedIndex].insertText);
        }
      } else if (e.key === 'Escape') {
        onDismiss();
      }
    };

    document.addEventListener('keydown', handleKeyDown, true);
    return () => document.removeEventListener('keydown', handleKeyDown, true);
  }, [position, mode, suggestions, selectedIndex, onSelect, onDismiss]);

  if (!position || mode === 'none' || suggestions.length === 0) return null;

  return (
    <div
      ref={listRef}
      className="fixed z-50 bg-white dark:bg-gray-800 border border-gray-200 dark:border-gray-700 rounded-lg shadow-lg max-h-48 overflow-y-auto min-w-[250px]"
      style={{ left: position.x, top: position.y }}
    >
      <div className="px-2 py-1 text-[10px] text-gray-400 dark:text-gray-500 border-b border-gray-100 dark:border-gray-700">
        {mode === 'functions' && 'RF Functions'}
        {mode === 'entities' && 'Entity Types'}
        {mode === 'fields' && `Fields for ${context}`}
      </div>
      {suggestions.map((s, i) => (
        <button
          key={s.text}
          onClick={() => onSelect(s.insertText)}
          className={`w-full text-left px-3 py-1.5 text-sm flex items-center justify-between gap-2 transition-colors ${
            i === selectedIndex
              ? 'bg-primary-50 dark:bg-primary-900/30 text-primary-700 dark:text-primary-300'
              : 'text-gray-700 dark:text-gray-300 hover:bg-gray-50 dark:hover:bg-gray-700'
          }`}
        >
          <span className="font-mono text-xs">{s.text}</span>
          {s.detail && <span className="text-[10px] text-gray-400 truncate">{s.detail}</span>}
        </button>
      ))}
    </div>
  );
}

function parseSuggestions(
  input: string,
  schemas: Record<string, EntitySchema>,
): { mode: SuggestionMode; suggestions: Suggestion[]; context: string } {
  if (!input) return { mode: 'none', suggestions: [], context: '' };

  // Check if we're typing an RF function
  const rfMatch = input.match(/=\s*RF\.([A-Z]*)/i);
  if (rfMatch && !input.includes('(')) {
    const partial = rfMatch[1].toUpperCase();
    const filtered = RF_FUNCTIONS.filter((f) => f.name.startsWith('RF.' + partial));
    return {
      mode: 'functions',
      suggestions: filtered.map((f) => ({
        text: f.name,
        insertText: f.signature,
        detail: f.desc,
      })),
      context: '',
    };
  }

  // Check if we're inside an RF function and need entity suggestions
  const entityArgMatch = input.match(/=\s*RF\.\w+\(\s*"?([^",)]*)/i);
  if (entityArgMatch) {
    const partial = entityArgMatch[1].toLowerCase();
    // If we already have a complete entity name in quotes, suggest fields
    const fieldArgMatch = input.match(/=\s*RF\.\w+\(\s*"([^"]+)"\s*,\s*(?:\d+\s*,\s*)?"?([^",)]*)/i);
    if (fieldArgMatch) {
      const entityName = fieldArgMatch[1];
      const fieldPartial = fieldArgMatch[2].toLowerCase();
      const schema = schemas[entityName];
      if (schema) {
        const fields = schema.fields
          .filter((f) => f.name.toLowerCase().startsWith(fieldPartial) || f.label.toLowerCase().startsWith(fieldPartial))
          .map((f) => ({
            text: f.name,
            insertText: `"${f.name}"`,
            detail: `${f.type} — ${f.label}`,
          }));
        return { mode: 'fields', suggestions: fields, context: entityName };
      }
    }

    // Suggest entity names
    const entityNames = Object.keys(schemas).filter(
      (name) => name !== 'rf-sheets' && name.toLowerCase().startsWith(partial),
    );
    return {
      mode: 'entities',
      suggestions: entityNames.map((name) => ({
        text: name,
        insertText: `"${name}"`,
        detail: schemas[name]?.readable_name?.singular,
      })),
      context: '',
    };
  }

  return { mode: 'none', suggestions: [], context: '' };
}
