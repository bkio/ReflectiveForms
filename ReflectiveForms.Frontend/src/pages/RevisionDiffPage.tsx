import { useState, useMemo, useRef, useEffect, useCallback } from 'react';
import { useParams, useSearchParams, Link } from 'react-router-dom';
import { useSchema, useEntity, useEntityHistory, useEntityList } from '../hooks/useEntity';
import { RevisionEntry, FieldSchema, EntitySchema, EntityData, GroupRenderStyle } from '../types/schema';
import { sanitizeHtml } from '../lib/sanitize';
import { evaluateCompoundCondition } from '../lib/conditionParser';
import { ArrowLeft, ChevronDown, Search } from 'lucide-react';

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

type RevisionOption = {
  label: string;
  value: string; // 'latest' or revision number as string
  date: string;
  modifiedBy: string;
};

function formatDate(dateStr: string): string {
  try {
    const d = new Date(dateStr);
    return d.toLocaleString();
  } catch {
    return dateStr;
  }
}

function buildRevisionOptions(
  revisions: RevisionEntry[],
  currentEntity: EntityData | null | undefined,
): RevisionOption[] {
  const options: RevisionOption[] = [];

  if (currentEntity) {
    options.push({
      label: 'Latest',
      value: 'latest',
      date: currentEntity.modified || currentEntity.date || '',
      modifiedBy: '',
    });
  }

  // Revisions are numbered 1..N (oldest first). Show newest first.
  for (let i = revisions.length - 1; i >= 0; i--) {
    const rev = revisions[i];
    options.push({
      label: `Revision ${rev.revision_number}`,
      value: String(rev.revision_number),
      date: rev.date || '',
      modifiedBy: rev.modified_by_email || '',
    });
  }

  return options;
}

function getRevisionData(
  value: string,
  revisions: RevisionEntry[],
  currentEntity: EntityData | null | undefined,
): EntityData | null {
  if (value === 'latest') return currentEntity ?? null;
  const num = parseInt(value, 10);
  const rev = revisions.find(r => r.revision_number === num);
  return rev?.object ?? null;
}

export function RevisionDiffPage() {
  const { entityName } = useParams<{ entityName: string }>();
  const [searchParams] = useSearchParams();
  const idParam = searchParams.get('id');
  const entityId = idParam ? parseInt(idParam, 10) : undefined;

  const { data: schema, isLoading: schemaLoading, error: schemaError } = useSchema(entityName ?? '');
  const { data: entityData, isLoading: entityLoading, error: entityError } = useEntity(entityName ?? '', entityId);
  const { data: historyData, isLoading: historyLoading, error: historyError } = useEntityHistory(entityName ?? '', entityId);

  const [leftValue, setLeftValue] = useState<string>('');
  const [rightValue, setRightValue] = useState<string>('');
  const [sameRevisionError, setSameRevisionError] = useState(false);

  // Collect all unique relation entity names from the schema
  const relationEntityNames = useMemo(
    () => (schema ? collectRelationEntityNames(schema.fields) : []),
    [schema],
  );

  // Fetch relation entity lists — hooks must always be called in the same order
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
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [relationEntityNames, ...relationQueries.map(q => q.data)]);

  const revisionOptions = useMemo(() => {
    if (!historyData) return [];
    return buildRevisionOptions(historyData.revisions, entityData);
  }, [historyData, entityData]);

  // Auto-select defaults when options become available
  useMemo(() => {
    if (revisionOptions.length >= 2 && !leftValue && !rightValue) {
      setLeftValue(revisionOptions[0].value);
      setRightValue(revisionOptions[1].value);
    }
  }, [revisionOptions, leftValue, rightValue]);

  const handleCompare = (side: 'left' | 'right', value: string) => {
    if (side === 'left') {
      setLeftValue(value);
      setSameRevisionError(value === rightValue);
    } else {
      setRightValue(value);
      setSameRevisionError(value === leftValue);
    }
  };

  const isLoading = schemaLoading || entityLoading || historyLoading;
  const loadError = schemaError || entityError || historyError;

  if (isLoading) {
    return (
      <div className="flex items-center justify-center min-h-[400px]">
        <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-blue-600"></div>
      </div>
    );
  }

  if (loadError) {
    return (
      <div className="flex items-center justify-center min-h-[400px]">
        <div className="bg-red-50 text-red-600 p-6 rounded-lg">
          <h2 className="text-lg font-semibold mb-2">Error</h2>
          <p>{loadError.message}</p>
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

  const leftData = leftValue ? getRevisionData(leftValue, historyData?.revisions ?? [], entityData) : null;
  const rightData = rightValue ? getRevisionData(rightValue, historyData?.revisions ?? [], entityData) : null;

  return (
    <div>
      <div className="max-w-7xl mx-auto py-8 px-4">
        {/* Header */}
        <div className="mb-6">
          <div className="flex items-center gap-3 mb-2">
            <Link
              to={`/entities-view/${entityName}?id=${entityId}`}
              className="text-gray-500 hover:text-gray-700 transition-colors"
              title="Back to entity"
            >
              <ArrowLeft className="w-5 h-5" />
            </Link>
            <h1 className="text-2xl font-bold text-gray-900 dark:text-gray-100">
              Compare Revisions
            </h1>
          </div>
          <p className="ml-8 text-sm text-gray-500">
            {schema.readable_name.singular} — ID: {entityId} — {entityData.title?.rendered || 'Untitled'}
          </p>
        </div>

        {/* Same revision error */}
        {sameRevisionError && (
          <div
            className="mb-4 bg-red-50 dark:bg-red-900/30 border border-red-200 dark:border-red-800 text-red-700 dark:text-red-300 px-4 py-3 rounded-lg"
            data-testid="same-revision-error"
          >
            Cannot compare the same revision with itself. Please select two different revisions.
          </div>
        )}

        {/* Side-by-side selectors and diff */}
        <div className="grid grid-cols-2 gap-6">
          {/* Left side */}
          <div>
            <RevisionSelector
              label="Left"
              options={revisionOptions}
              value={leftValue}
              onChange={(v) => handleCompare('left', v)}
              testId="left-revision-selector"
            />
          </div>

          {/* Right side */}
          <div>
            <RevisionSelector
              label="Right"
              options={revisionOptions}
              value={rightValue}
              onChange={(v) => handleCompare('right', v)}
              testId="right-revision-selector"
            />
          </div>
        </div>

        {/* Divider between selectors and diff content */}
        <div className="border-t border-gray-300 dark:border-gray-600 my-6" />

        {/* Diff content */}
        <div className="grid grid-cols-2 gap-6">
          <div>
            {leftData && !sameRevisionError && (
              <div data-testid="left-revision-content">
                <RevisionContent schema={schema} data={leftData} otherData={rightData} side="left" relationLookup={relationLookup} />
              </div>
            )}
          </div>
          <div>
            {rightData && !sameRevisionError && (
              <div data-testid="right-revision-content">
                <RevisionContent schema={schema} data={rightData} otherData={leftData} side="right" relationLookup={relationLookup} />
              </div>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}

interface RevisionSelectorProps {
  label: string;
  options: RevisionOption[];
  value: string;
  onChange: (value: string) => void;
  testId: string;
}

function RevisionSelector({ label, options, value, onChange, testId }: RevisionSelectorProps) {
  const [isOpen, setIsOpen] = useState(false);
  const [search, setSearch] = useState('');
  const containerRef = useRef<HTMLDivElement>(null);
  const searchInputRef = useRef<HTMLInputElement>(null);
  const listRef = useRef<HTMLDivElement>(null);

  const filteredOptions = useMemo(() => {
    if (!search.trim()) return options;
    const lower = search.toLowerCase();
    return options.filter((opt) => {
      const text = `${opt.label} ${opt.date ? formatDate(opt.date) : ''} ${opt.modifiedBy}`.toLowerCase();
      return text.includes(lower);
    });
  }, [options, search]);

  const selectedOption = options.find((o) => o.value === value);

  // Close on outside click
  useEffect(() => {
    const handler = (e: MouseEvent) => {
      if (containerRef.current && !containerRef.current.contains(e.target as Node)) {
        setIsOpen(false);
        setSearch('');
      }
    };
    document.addEventListener('mousedown', handler);
    return () => document.removeEventListener('mousedown', handler);
  }, []);

  // Focus search when opened
  useEffect(() => {
    if (isOpen) searchInputRef.current?.focus();
  }, [isOpen]);

  const handleSelect = useCallback(
    (optValue: string) => {
      onChange(optValue);
      setIsOpen(false);
      setSearch('');
    },
    [onChange],
  );

  return (
    <div ref={containerRef} className="relative" data-testid={testId}>
      <h2 className="text-sm font-semibold text-gray-500 dark:text-gray-400 uppercase tracking-wider mb-2">
        {label} Revision
      </h2>

      {/* Trigger */}
      <button
        type="button"
        onClick={() => setIsOpen(!isOpen)}
        className="w-full flex items-center justify-between px-3 py-2.5 border border-gray-200 dark:border-gray-700 rounded-lg bg-white dark:bg-gray-800 hover:border-gray-400 dark:hover:border-gray-500 text-left focus:outline-none focus:ring-2 focus:ring-blue-500"
        aria-haspopup="listbox"
        aria-expanded={isOpen}
      >
        {selectedOption ? (
          <span className="flex-1 min-w-0 truncate">
            <span className="font-medium text-gray-900 dark:text-gray-100">{selectedOption.label}</span>
            {selectedOption.date && (
              <span className="ml-2 text-sm text-gray-500 dark:text-gray-400">— {formatDate(selectedOption.date)}</span>
            )}
            {selectedOption.modifiedBy && (
              <span className="ml-2 text-sm text-gray-400 dark:text-gray-500">— {selectedOption.modifiedBy}</span>
            )}
          </span>
        ) : (
          <span className="text-gray-500 dark:text-gray-400">Select a revision…</span>
        )}
        <ChevronDown className={`w-4 h-4 text-gray-400 ml-2 flex-shrink-0 transition-transform ${isOpen ? 'rotate-180' : ''}`} />
      </button>

      {/* Dropdown */}
      {isOpen && (
        <div className="absolute z-50 w-full mt-1 bg-white dark:bg-gray-800 border border-gray-200 dark:border-gray-700 rounded-lg shadow-lg">
          {/* Search */}
          <div className="p-2 border-b border-gray-100 dark:border-gray-700">
            <div className="relative">
              <Search className="absolute left-2.5 top-1/2 -translate-y-1/2 w-4 h-4 text-gray-400" />
              <input
                ref={searchInputRef}
                type="text"
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                placeholder="Search revisions…"
                className="w-full pl-8 pr-3 py-1.5 text-sm border border-gray-200 dark:border-gray-600 rounded bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100 focus:outline-none focus:ring-1 focus:ring-blue-500"
              />
            </div>
          </div>

          {/* Options list */}
          <div ref={listRef} className="max-h-60 overflow-y-auto" role="listbox">
            {filteredOptions.length === 0 ? (
              <div className="px-3 py-4 text-sm text-gray-500 dark:text-gray-400 text-center">
                No matching revisions
              </div>
            ) : (
              filteredOptions.map((opt) => (
                <div
                  key={opt.value}
                  role="option"
                  aria-selected={value === opt.value}
                  data-testid={`revision-option-${opt.value}`}
                  data-value={opt.value}
                  onClick={() => handleSelect(opt.value)}
                  className={`flex items-center gap-3 px-4 py-2.5 cursor-pointer border-b border-gray-50 dark:border-gray-700 last:border-b-0 ${
                    value === opt.value
                      ? 'bg-blue-50 dark:bg-blue-900/40 text-blue-700 dark:text-blue-300'
                      : 'hover:bg-gray-50 dark:hover:bg-gray-700 text-gray-900 dark:text-gray-100'
                  }`}
                >
                  <div className="flex-1 min-w-0">
                    <span className="font-medium">{opt.label}</span>
                    {opt.date && (
                      <span className="ml-2 text-sm opacity-70">— {formatDate(opt.date)}</span>
                    )}
                    {opt.modifiedBy && (
                      <span className="ml-2 text-sm opacity-50">— {opt.modifiedBy}</span>
                    )}
                  </div>
                </div>
              ))
            )}
          </div>
        </div>
      )}
    </div>
  );
}

interface RevisionContentProps {
  schema: EntitySchema;
  data: EntityData;
  otherData: EntityData | null;
  side: 'left' | 'right';
  relationLookup: Record<string, Record<number, string>>;
}

function RevisionContent({ schema, data, otherData, relationLookup }: RevisionContentProps) {
  const fields = data.fields ?? {};
  const otherFields = otherData?.fields ?? {};

  return (
    <div className="space-y-3">
      {/* Title */}
      <DiffField
        label="Title"
        value={data.title?.rendered ?? ''}
        otherValue={otherData?.title?.rendered ?? ''}
      />

      {/* Fields */}
      {schema.fields.map((fieldSchema) => (
        <DiffFieldRenderer
          key={fieldSchema.name}
          fieldSchema={fieldSchema}
          value={fields[fieldSchema.name]}
          otherValue={otherFields[fieldSchema.name]}
          allFields={fields}
          schema={schema}
          relationLookup={relationLookup}
        />
      ))}
    </div>
  );
}

interface DiffFieldProps {
  label: string;
  value: unknown;
  otherValue: unknown;
}

function DiffField({ label, value, otherValue }: DiffFieldProps) {
  const valueStr = formatDisplayValue(value);
  const otherStr = formatDisplayValue(otherValue);
  const isDifferent = valueStr !== otherStr;

  return (
    <div
      className={`rounded-lg border p-3 ${
        isDifferent
          ? 'bg-yellow-50 dark:bg-yellow-900/30 border-yellow-300 dark:border-yellow-700'
          : 'bg-white dark:bg-gray-800 border-gray-200 dark:border-gray-700'
      }`}
      data-diff={isDifferent ? 'changed' : 'unchanged'}
    >
      <div className="text-xs font-medium text-gray-500 dark:text-gray-400 mb-1">{label}</div>
      <div className="text-sm text-gray-900 dark:text-gray-100 break-words whitespace-pre-wrap">
        {valueStr || <span className="text-gray-400 dark:text-gray-500 italic">Not set</span>}
      </div>
    </div>
  );
}

function formatDisplayValue(value: unknown): string {
  if (value === null || value === undefined) return '';
  if (typeof value === 'object') return JSON.stringify(value, null, 2);
  return String(value);
}

interface DiffFieldRendererProps {
  fieldSchema: FieldSchema;
  value: unknown;
  otherValue: unknown;
  allFields: Record<string, unknown>;
  schema: EntitySchema;
  depth?: number;
  relationLookup?: Record<string, Record<number, string>>;
}

function DiffFieldRenderer({ fieldSchema, value, otherValue, allFields, schema, depth = 0, relationLookup }: DiffFieldRendererProps) {
  // Evaluate display condition
  if (fieldSchema.display_condition) {
    if (!evaluateCompoundCondition(fieldSchema.display_condition, allFields)) {
      return null;
    }
  }

  const type = fieldSchema.type;

  if (type === 'Group') {
    return (
      <DiffGroupField
        fieldSchema={fieldSchema}
        value={value}
        otherValue={otherValue}
        schema={schema}
        depth={depth}
        relationLookup={relationLookup}
      />
    );
  }

  if (type === 'Repeater') {
    return (
      <DiffRepeaterField
        fieldSchema={fieldSchema}
        value={value}
        otherValue={otherValue}
        schema={schema}
        depth={depth}
        relationLookup={relationLookup}
      />
    );
  }

  if (type === 'WysiwygEditor') {
    const valStr = String(value ?? '');
    const otherStr = String(otherValue ?? '');
    const isDifferent = valStr !== otherStr;

    return (
      <div
        className={`rounded-lg border p-3 ${
          isDifferent ? 'bg-yellow-50 dark:bg-yellow-900/30 border-yellow-300 dark:border-yellow-700' : 'bg-white dark:bg-gray-800 border-gray-200 dark:border-gray-700'
        }`}
        data-diff={isDifferent ? 'changed' : 'unchanged'}
      >
        <div className="text-xs font-medium text-gray-500 dark:text-gray-400 mb-1">{fieldSchema.label}</div>
        <div
          className="prose prose-sm max-w-none text-gray-900 dark:text-gray-100"
          dangerouslySetInnerHTML={{ __html: sanitizeHtml(valStr) }}
        />
      </div>
    );
  }

  // For Relation, resolve ID to title
  if (type === 'Relation') {
    const relEntityName = fieldSchema.relation_options?.relation_entity_name ?? '';
    const resolve = (v: unknown): string => {
      if (v === null || v === undefined || v === '') return '';
      const id = Number(v);
      if (isNaN(id)) return String(v);
      return relationLookup?.[relEntityName]?.[id] ?? `#${id}`;
    };
    return <DiffField label={fieldSchema.label} value={resolve(value)} otherValue={resolve(otherValue)} />;
  }

  // For Select, resolve choice labels
  if (type === 'Select') {
    const strVal = String(value ?? '');
    const choice = fieldSchema.select_options?.choices?.find(c => c.value === strVal);
    const displayVal = choice?.label ?? strVal;

    const otherStrVal = String(otherValue ?? '');
    const otherChoice = fieldSchema.select_options?.choices?.find(c => c.value === otherStrVal);
    const otherDisplayVal = otherChoice?.label ?? otherStrVal;

    return <DiffField label={fieldSchema.label} value={displayVal} otherValue={otherDisplayVal} />;
  }

  // For Checkbox
  if (type === 'Checkbox') {
    return <DiffField label={fieldSchema.label} value={value ? 'Yes' : 'No'} otherValue={otherValue ? 'Yes' : 'No'} />;
  }

  // For DatePicker, format yyyyMMdd to yyyy-MM-dd
  if (type === 'DatePicker') {
    const fmt = (v: unknown) => {
      const s = String(v ?? '');
      if (/^\d{8}$/.test(s)) return `${s.slice(0, 4)}-${s.slice(4, 6)}-${s.slice(6, 8)}`;
      return s;
    };
    return <DiffField label={fieldSchema.label} value={fmt(value)} otherValue={fmt(otherValue)} />;
  }

  return <DiffField label={fieldSchema.label} value={value} otherValue={otherValue} />;
}

interface DiffGroupFieldProps {
  fieldSchema: FieldSchema;
  value: unknown;
  otherValue: unknown;
  schema: EntitySchema;
  depth: number;
  relationLookup?: Record<string, Record<number, string>>;
}

function DiffGroupField({ fieldSchema, value, otherValue, schema, depth, relationLookup }: DiffGroupFieldProps) {
  const groupVal = (value && typeof value === 'object') ? value as Record<string, unknown> : {};
  const otherGroupVal = (otherValue && typeof otherValue === 'object') ? otherValue as Record<string, unknown> : {};
  const childFields = fieldSchema.group_options?.child_schema ?? [];

  return (
    <div className="rounded-lg border border-gray-200 dark:border-gray-700 p-3">
      <div className="text-xs font-medium text-gray-500 dark:text-gray-400 mb-2">{fieldSchema.label}</div>
      <div className="space-y-2 pl-2">
        {childFields.map(child => (
          <DiffFieldRenderer
            key={child.name}
            fieldSchema={child}
            value={groupVal[child.name]}
            otherValue={otherGroupVal[child.name]}
            allFields={groupVal}
            schema={schema}
            depth={depth + 1}
            relationLookup={relationLookup}
          />
        ))}
      </div>
    </div>
  );
}

interface DiffRepeaterFieldProps {
  fieldSchema: FieldSchema;
  value: unknown;
  otherValue: unknown;
  schema: EntitySchema;
  depth: number;
  relationLookup?: Record<string, Record<number, string>>;
}

function DiffRepeaterField({ fieldSchema, value, otherValue, schema, depth, relationLookup }: DiffRepeaterFieldProps) {
  const items = Array.isArray(value) ? value : [];
  const otherItems = Array.isArray(otherValue) ? otherValue : [];
  const itemFields = fieldSchema.repeater_options?.item_schema ?? [];
  const maxLen = Math.max(items.length, otherItems.length);

  return (
    <div className="rounded-lg border border-gray-200 dark:border-gray-700 p-3">
      <div className="text-xs font-medium text-gray-500 dark:text-gray-400 mb-2">{fieldSchema.label}</div>
      {maxLen === 0 ? (
        <span className="text-gray-400 dark:text-gray-500 italic text-sm">No items</span>
      ) : (
        <div className="space-y-2">
          {Array.from({ length: maxLen }, (_, idx) => {
            const item = items[idx] ?? {};
            const otherItem = otherItems[idx] ?? {};
            const itemObj = (item && typeof item === 'object') ? item as Record<string, unknown> : {};
            const otherItemObj = (otherItem && typeof otherItem === 'object') ? otherItem as Record<string, unknown> : {};

            return (
              <div key={idx} className="border border-gray-100 dark:border-gray-700 rounded p-2">
                <div className="text-xs font-semibold text-gray-600 dark:text-gray-400 mb-1">
                  {fieldSchema.label} #{idx + 1}
                </div>
                <div className="space-y-1 pl-2">
                  {itemFields.map(child => (
                    <DiffFieldRenderer
                      key={child.name}
                      fieldSchema={child}
                      value={itemObj[child.name]}
                      otherValue={otherItemObj[child.name]}
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
      )}
    </div>
  );
}
