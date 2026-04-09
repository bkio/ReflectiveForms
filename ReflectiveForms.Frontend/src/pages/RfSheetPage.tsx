import { useParams, useNavigate } from 'react-router-dom';
import { ArrowLeft, Save, RefreshCw, PanelLeftClose, PanelLeft, Download, AlertTriangle, Share2, Lock } from 'lucide-react';
import { useEntity, useAllSchemas, useCapabilities } from '../hooks/useEntity';
import { createEntity, updateEntity, fetchLockStatus } from '../api/client';
import { useState, useEffect, useRef, useCallback, useMemo } from 'react';
import { useQuery } from '@tanstack/react-query';
import { toast } from 'sonner';
import { useAuth } from '../hooks/useAuth';
import { useEntityLock } from '../hooks/useEntityLock';
import { useRfSheetData } from '../hooks/useRfSheetData';
import { registerRfFormulas } from '../lib/rf-sheet-functions';
import type { FormulaContext } from '../lib/rf-sheet-functions';
import { exportWorkbookToXlsx } from '../lib/rf-sheet-export';
import { detectStaleFields, extractRequiredFields } from '../lib/rf-sheet-schema-validator';
import type { StaleFieldReference } from '../lib/rf-sheet-schema-validator';
import { EntitySourcePanel } from '../components/sheets/EntitySourcePanel';
import { RepeaterDropDialog } from '../components/sheets/RepeaterDropDialog';
import type { RepeaterFormulaChoice } from '../components/sheets/RepeaterDropDialog';
import { SheetSharingDialog } from '../components/sheets/SheetSharingDialog';
import type { SheetSharingState } from '../components/sheets/SheetSharingDialog';
import type { BulkReadSource, FieldSchema } from '../types/schema';

// Univer imports
import { UniverSheetsCorePreset } from '@univerjs/preset-sheets-core';
import UniverPresetSheetsCoreEnUS from '@univerjs/preset-sheets-core/locales/en-US';
import { createUniver, LocaleType, mergeLocales } from '@univerjs/presets';
import '@univerjs/preset-sheets-core/lib/index.css';

interface SheetFields {
  sources: string;
  bound_regions: string;
  workbook_data: string;
  refresh_interval_seconds: number;
  is_public: boolean;
  shared_users: Array<{ user: number; permission: string }>;
  shared_roles: Array<{ role: number; permission: string }>;
}

function parseSheetFields(fields: unknown): SheetFields {
  const f = fields as Record<string, unknown> | undefined;
  return {
    sources: typeof f?.sources === 'string' ? f.sources : '[]',
    bound_regions: typeof f?.bound_regions === 'string' ? f.bound_regions : '[]',
    workbook_data: typeof f?.workbook_data === 'string' ? f.workbook_data : '{}',
    refresh_interval_seconds: typeof f?.refresh_interval_seconds === 'number' ? f.refresh_interval_seconds : 30,
    is_public: typeof f?.is_public === 'boolean' ? f.is_public : false,
    shared_users: Array.isArray(f?.shared_users) ? f.shared_users as SheetFields['shared_users'] : [],
    shared_roles: Array.isArray(f?.shared_roles) ? f.shared_roles as SheetFields['shared_roles'] : [],
  };
}

/**
 * Strips cached computed values (v, t, si) from cells that have formulas (f).
 * When Univer loads a snapshot with both formula + cached value, it restores the
 * cell as a 1×1 allocation using the cached scalar. Later recalculations can't
 * expand that cell into a spill array. Stripping cached values forces Univer to
 * compute formulas fresh, so spill arrays are allocated correctly from the start.
 */
function stripFormulaCachedValues(snapshot: Record<string, unknown>): Record<string, unknown> {
  const sheets = snapshot.sheets as Record<string, Record<string, unknown>> | undefined;
  if (!sheets) return snapshot;

  for (const sheetData of Object.values(sheets)) {
    const cellData = sheetData.cellData as Record<string, Record<string, Record<string, unknown>>> | undefined;
    if (!cellData) continue;
    for (const row of Object.values(cellData)) {
      for (const cell of Object.values(row)) {
        if (cell.f) {
          // Cell has a formula — remove cached value/type so Univer computes fresh
          delete cell.v;
          delete cell.t;
          delete cell.si;
        }
      }
    }
  }
  return snapshot;
}

export function RfSheetPage() {
  const { sheetId } = useParams<{ sheetId: string }>();
  const navigate = useNavigate();
  const isNew = sheetId === 'new';
  const numericId = isNew ? undefined : Number(sheetId);

  const { data: existingSheet, isLoading } = useEntity('rf-sheets', numericId);
  const { data: schemas } = useAllSchemas();
  const { user: currentUser } = useAuth();
  const [title, setTitle] = useState('');
  const [saving, setSaving] = useState(false);
  const [showPanel, setShowPanel] = useState(true);
  const [showSharingDialog, setShowSharingDialog] = useState(false);
  const [activeSources, setActiveSources] = useState<string[]>([]);
  const [sheetAuthorId, setSheetAuthorId] = useState<number | undefined>(undefined);
  const [sharing, setSharing] = useState<SheetSharingState>({
    is_public: false,
    shared_users: [],
    shared_roles: [],
  });

  const univerContainerRef = useRef<HTMLDivElement>(null);
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const univerAPIRef = useRef<any>(null);
  const formulaDisposableRef = useRef<{ dispose: () => void } | null>(null);

  // Mutable context ref — formulas read from this. Avoids dispose/re-register on data change.
  const formulaContextRef = useRef<FormulaContext>({
    dataStore: { entityData: new Map(), unauthorizedEntities: new Set(), isLoading: false, error: null, refresh: () => {}, getEntityField: () => '#NO_DATA', getAllEntityRows: () => [], fetchedFields: new Map(), requestFields: () => {} },
  });

  // Ref for the drop handler so the Univer Drop event can always call the latest version
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const dropHandlerRef = useRef<(params: any) => void>(() => {});

  // Parse existing sheet fields
  const sheetFields = useMemo(() => {
    if (existingSheet) return parseSheetFields(existingSheet.fields);
    return parseSheetFields(undefined);
  }, [existingSheet]);

  // Track fields required by workbook formulas (entity → Set<field>)
  const [requiredFields, setRequiredFields] = useState<Map<string, Set<string>>>(new Map());

  // Build bulk-read sources from active sources + formula-required fields
  const bulkReadSources = useMemo<BulkReadSource[]>(
    () => activeSources.map((entity) => {
      const fields = requiredFields.get(entity);
      if (fields && fields.size > 0) {
        return { entity, fields: Array.from(fields) };
      }
      return { entity };
    }),
    [activeSources, requiredFields],
  );

  const dataStore = useRfSheetData(bulkReadSources, sheetFields.refresh_interval_seconds);

  // Ownership: current user is the sheet creator (author field matches user id)
  const sheetAccessLevel = existingSheet?.access_level;
  const isOwner = isNew || sheetAccessLevel === 'owner';

  // Design mode: user can edit structure (panel, formulas, save). View mode: read-only.
  // Use the per-sheet access_level returned by the backend (owner/edit/view)
  const hasEditRight = isNew || sheetAccessLevel === 'owner' || sheetAccessLevel === 'edit';
  const { data: capabilities } = useCapabilities();

  // Entity locking — acquire lock when user has edit rights on an existing sheet
  const { lockStatus, lockedBy, signalActivity } = useEntityLock(
    'rf-sheets',
    numericId,
    {
      enabled: hasEditRight && !isNew,
      onLockLost: () => {
        // Stay on same page but in view-only mode (lockStatus becomes 'failed')
      },
    },
  );
  // For existing sheets: must hold the lock to edit. While lock is in-flight (idle), stay read-only.
  const isDesignMode = isNew ? true : (hasEditRight && lockStatus === 'locked');

  // For read-only viewers (no edit right) or locked-out editors: poll lock status to show banner
  const showLockPoll = !isNew && numericId !== undefined && !isDesignMode;
  const { data: polledLockStatus } = useQuery({
    queryKey: ['sheet-lock-status', numericId],
    queryFn: async () => {
      const res = await fetchLockStatus('rf-sheets', numericId!);
      return res.data ?? null;
    },
    enabled: showLockPoll,
    refetchInterval: 10_000,
    refetchIntervalInBackground: false,
  });

  // Determine who's editing: from lock hook (for editors) or polled status (for viewers)
  const activeLockHolder = hasEditRight && lockStatus === 'failed'
    ? lockedBy
    : polledLockStatus?.locked_by_user_name ?? null;

  // Schema evolution: detect stale field references
  const [staleFields, setStaleFields] = useState<StaleFieldReference[]>([]);

  // Pending repeater drop — when set, shows dialog before inserting formula
  const [pendingRepeaterDrop, setPendingRepeaterDrop] = useState<{
    entityName: string;
    fieldName: string;
    fieldLabel: string;
    itemSchema: FieldSchema[];
    row: number;
    col: number;
  } | null>(null);

  // Sync title, sources, AND required fields from loaded sheet (all at once to avoid double fetch)
  useEffect(() => {
    if (existingSheet) {
      setTitle(existingSheet.title?.rendered ?? '');
      setSheetAuthorId(existingSheet.author);
      const fields = parseSheetFields(existingSheet.fields);
      try {
        const sources = JSON.parse(fields.sources);
        if (Array.isArray(sources)) {
          setActiveSources(sources.map((s: string | { entity: string }) => (typeof s === 'string' ? s : s.entity)));
        }
      } catch { /* ignore parse errors */ }
      // Extract required fields from saved workbook data synchronously
      // so the first bulk_read already has the correct field filter.
      try {
        const workbookData = JSON.parse(fields.workbook_data);
        setRequiredFields(extractRequiredFields(workbookData));
      } catch { /* ignore */ }
      // Sync sharing state
      setSharing({
        is_public: fields.is_public,
        shared_users: fields.shared_users.map((u) => ({ user: u.user, permission: u.permission as 'view' | 'edit' })),
        shared_roles: fields.shared_roles.map((r) => ({ role: r.role, permission: r.permission as 'view' | 'edit' })),
      });
    }
  }, [existingSheet]);

  // Initialize Univer
  useEffect(() => {
    if (!univerContainerRef.current) return;

    const { univerAPI } = createUniver({
      locale: LocaleType.EN_US,
      locales: {
        [LocaleType.EN_US]: mergeLocales(UniverPresetSheetsCoreEnUS),
      },
      presets: [
        UniverSheetsCorePreset({
          container: univerContainerRef.current,
        }),
      ],
    });

    univerAPIRef.current = univerAPI;
    // Expose for e2e testing (mirrors __rfFormSetValue pattern in DynamicForm)
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    (window as any).__univerAPI = univerAPI;

    // Load existing workbook data or create blank
    let workbookData = {};
    if (existingSheet) {
      try {
        const parsed = JSON.parse(parseSheetFields(existingSheet.fields).workbook_data);
        if (parsed && typeof parsed === 'object' && Object.keys(parsed).length > 0) {
          workbookData = parsed;
        }
      } catch { /* use blank */ }
    }
    // Strip cached values from formula cells before loading.
    // Without this, Univer restores formula cells as 1×1 with the cached scalar.
    // Later recalculations can't expand them into spill arrays.
    workbookData = stripFormulaCachedValues(workbookData as Record<string, unknown>);
    univerAPI.createWorkbook(workbookData);

    // Register RF formulas immediately. Since cached values were stripped,
    // Univer has no preconceived allocation for formula cells. When data
    // arrives and executeCalculation() runs, formulas return arrays and
    // Univer creates proper spill regions from scratch.
    formulaDisposableRef.current = registerRfFormulas(univerAPI, formulaContextRef.current);

    // Listen for Univer's native DragOver (to allow drop) and Drop events.
    // The Drop event gives us the exact { row, column } of the cell under the cursor.
    const dragOverDisposable = univerAPI.addEvent(
      univerAPI.Event.DragOver,
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      (params: any) => { params.preventDefault?.(); },
    );
    const dropDisposable = univerAPI.addEvent(
      univerAPI.Event.Drop,
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      (params: any) => { dropHandlerRef.current(params); },
    );

    return () => {
      dragOverDisposable.dispose();
      dropDisposable.dispose();
      formulaDisposableRef.current?.dispose();
      formulaDisposableRef.current = null;
      univerAPI.dispose();
      univerAPIRef.current = null;
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      delete (window as any).__univerAPI;
    };
    // Only re-init when the sheet entity loads for the first time
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [existingSheet?.id]);

  // Helper: extract required fields from the current workbook snapshot
  const updateRequiredFields = useCallback(() => {
    if (!univerAPIRef.current) return;
    try {
      const workbook = univerAPIRef.current.getActiveWorkbook();
      if (!workbook) return;
      const snapshot = workbook.save();
      const fields = extractRequiredFields(snapshot);
      setRequiredFields(fields);
    } catch { /* Univer not ready */ }
  }, []);

  // Keep the mutable context ref always up-to-date (safe to do during render for a ref).
  formulaContextRef.current.dataStore = dataStore;
  formulaContextRef.current.schemas = schemas ?? undefined;

  // Trigger recalculation when data arrives or changes.
  // Formulas are registered during Univer init. Cached values were stripped from
  // the snapshot, so formula cells have no preconceived 1×1 allocation.
  // When this runs, formulas read fresh data from the mutable context ref and
  // return arrays — Univer creates correct spill regions.
  useEffect(() => {
    const hasData = dataStore.entityData.size > 0 || dataStore.unauthorizedEntities.size > 0;
    if (!hasData || !univerAPIRef.current) return;

    try {
      univerAPIRef.current.getFormula().executeCalculation();
    } catch { /* Univer may not be ready yet */ }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [dataStore.entityData, dataStore.unauthorizedEntities, schemas]);

  // Validate workbook fields against current schemas on load
  useEffect(() => {
    if (!existingSheet || !schemas) return;
    try {
      const workbookData = JSON.parse(parseSheetFields(existingSheet.fields).workbook_data);
      const stale = detectStaleFields(workbookData, schemas);
      setStaleFields(stale);
    } catch {
      setStaleFields([]);
    }
  }, [existingSheet, schemas]);

  // Get the current workbook snapshot as JSON string
  const getWorkbookData = useCallback((): string => {
    if (!univerAPIRef.current) return '{}';
    try {
      const workbook = univerAPIRef.current.getActiveWorkbook();
      if (!workbook) return '{}';
      const snapshot = workbook.save();
      return JSON.stringify(snapshot);
    } catch {
      return '{}';
    }
  }, []);

  const handleSave = async () => {
    if (!title.trim()) {
      toast.error('Sheet name is required');
      return;
    }

    signalActivity(); // Reset lock inactivity timer on save
    setSaving(true);
    try {
      const fields: Record<string, unknown> = {
        sources: JSON.stringify(activeSources),
        bound_regions: sheetFields.bound_regions,
        workbook_data: getWorkbookData(),
        refresh_interval_seconds: sheetFields.refresh_interval_seconds,
        is_public: sharing.is_public,
        shared_users: sharing.shared_users,
        shared_roles: sharing.shared_roles,
      };

      if (isNew) {
        const result = await createEntity('rf-sheets', {
          title: { rendered: title },
          author: currentUser?.id,
          fields,
        });
        if (result.error) {
          toast.error(result.error);
        } else if (result.data) {
          toast.success('Sheet created');
          navigate(`/sheets/${result.data.id}`, { replace: true });
        }
      } else {
        const result = await updateEntity('rf-sheets', {
          id: numericId,
          title: { rendered: title },
          author: sheetAuthorId,
          fields,
        });
        if (result.error) {
          toast.error(result.error);
        } else {
          toast.success('Sheet saved');
        }
      }
    } finally {
      setSaving(false);
    }
  };

  const handleAddSource = (entityName: string) => {
    setActiveSources((prev) => (prev.includes(entityName) ? prev : [...prev, entityName]));
  };

  const handleRemoveSource = (entityName: string) => {
    setActiveSources((prev) => prev.filter((s) => s !== entityName));
  };

  // Save sharing settings immediately when the dialog is closed (for existing sheets).
  const handleSharingDone = useCallback(async () => {
    setShowSharingDialog(false);
    if (isNew || !numericId) return; // Nothing to persist yet — will be saved with initial create.

    signalActivity(); // Reset lock inactivity timer
    setSaving(true);
    try {
      const result = await updateEntity('rf-sheets', {
        id: numericId,
        title: { rendered: title },
        author: sheetAuthorId,
        fields: {
          sources: JSON.stringify(activeSources),
          bound_regions: sheetFields.bound_regions,
          workbook_data: getWorkbookData(),
          refresh_interval_seconds: sheetFields.refresh_interval_seconds,
          is_public: sharing.is_public,
          shared_users: sharing.shared_users,
          shared_roles: sharing.shared_roles,
        },
      });
      if (result.error) {
        toast.error(result.error);
      } else {
        toast.success('Sharing settings saved');
      }
    } finally {
      setSaving(false);
    }
  }, [isNew, numericId, title, activeSources, sheetFields, sharing, getWorkbookData, signalActivity, sheetAuthorId]);

  // Drop handler — called from Univer's Drop event with the exact cell coordinates
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const handleSheetDrop = useCallback((params: any) => {
    const { row, column: col, dataTransfer } = params as { row: number; column: number; dataTransfer: DataTransfer };
    const raw = dataTransfer.getData('application/rf-sheet-field');
    if (!raw) return;

    let payload: { entity: string; field: string; label: string };
    try { payload = JSON.parse(raw); } catch { return; }
    const { entity: entityName, field: fieldName } = payload;

    if (!univerAPIRef.current) return;
    const workbook = univerAPIRef.current.getActiveWorkbook();
    if (!workbook) return;
    const sheet = workbook.getActiveSheet();
    if (!sheet) return;

    // Check if this field is a Repeater — look up in schema
    const entitySchema = schemas?.[entityName];
    if (entitySchema) {
      const fieldSchema = findFieldSchema(entitySchema.fields, fieldName);
      if (fieldSchema?.type === 'Repeater' && fieldSchema.repeater_options?.item_schema) {
        setPendingRepeaterDrop({
          entityName,
          fieldName,
          fieldLabel: fieldSchema.label,
          itemSchema: fieldSchema.repeater_options.item_schema,
          row,
          col,
        });
        return;
      }
    }

    if (fieldName === 'id') {
      sheet.getRange(row, col, 1, 1).setValue('ID');
      sheet.getRange(row + 1, col, 1, 1).setValue({ f: `=RF.IDS("${entityName}")` });
    } else {
      sheet.getRange(row, col, 1, 1).setValue(fieldName);
      sheet.getRange(row + 1, col, 1, 1).setValue({ f: `=RF.LIST("${entityName}", "${fieldName}")` });
    }

    updateRequiredFields();
  }, [updateRequiredFields, schemas]);

  // Keep the drop handler ref in sync so the Univer event always calls the latest closure
  dropHandlerRef.current = handleSheetDrop;

  const handleRepeaterConfirm = useCallback(
    (choice: RepeaterFormulaChoice) => {
      if (!pendingRepeaterDrop || !univerAPIRef.current) {
        setPendingRepeaterDrop(null);
        return;
      }
      const { entityName, fieldName, row, col } = pendingRepeaterDrop;
      const workbook = univerAPIRef.current.getActiveWorkbook();
      if (!workbook) { setPendingRepeaterDrop(null); return; }
      const sheet = workbook.getActiveSheet();
      if (!sheet) { setPendingRepeaterDrop(null); return; }

      const colLetter = columnToLetter(col);

      if (choice.type === 'count') {
        // Insert IDs at col, count formula at col+1
        sheet.getRange(row, col, 1, 1).setValue('ID');
        sheet.getRange(row + 1, col, 1, 1).setValue({ f: `=RF.IDS("${entityName}")` });
        sheet.getRange(row, col + 1, 1, 1).setValue(`${fieldName} count`);
        sheet.getRange(row + 1, col + 1, 1, 1).setValue({
          f: `=RF.REPEATCOUNT("${entityName}", ${colLetter}${row + 2}, "${fieldName}")`,
        });
      } else if (choice.type === 'list') {
        // Insert IDs column + sub-field column side by side
        sheet.getRange(row, col, 1, 1).setValue('ID');
        sheet.getRange(row + 1, col, 1, 1).setValue({ f: `=RF.IDS("${entityName}")` });
        sheet.getRange(row, col + 1, 1, 1).setValue(`${fieldName}.${choice.subField}`);
        sheet.getRange(row + 1, col + 1, 1, 1).setValue({
          f: `=RF.REPEAT("${entityName}", ${colLetter}${row + 2}, "${fieldName}", "${choice.subField}")`,
        });
      } else {
        // field — Insert IDs at col, single indexed value at col+1
        sheet.getRange(row, col, 1, 1).setValue('ID');
        sheet.getRange(row + 1, col, 1, 1).setValue({ f: `=RF.IDS("${entityName}")` });
        sheet.getRange(row, col + 1, 1, 1).setValue(`${fieldName}[0].${choice.subField}`);
        sheet.getRange(row + 1, col + 1, 1, 1).setValue({
          f: `=RF.REPEATFIELD("${entityName}", ${colLetter}${row + 2}, "${fieldName}", 0, "${choice.subField}")`,
        });
      }

      setPendingRepeaterDrop(null);
      updateRequiredFields();
    },
    [pendingRepeaterDrop, updateRequiredFields],
  );

  const handleExport = useCallback(() => {
    if (!univerAPIRef.current) return;
    const exportName = title.trim() || 'rf-sheet';
    exportWorkbookToXlsx(univerAPIRef.current, exportName);
    toast.success('Sheet exported');
  }, [title]);

  if (!isNew && isLoading) {
    return (
      <div className="space-y-4">
        <div className="h-10 w-48 bg-gray-100 dark:bg-gray-800 rounded animate-pulse" />
        <div className="h-[600px] bg-gray-100 dark:bg-gray-800 rounded animate-pulse" />
      </div>
    );
  }

  return (
    <div className="flex flex-col h-[calc(100vh-6rem)]">
      {/* Lock warning banner — shown to anyone viewing a sheet that is currently being edited */}
      {!isNew && !isDesignMode && activeLockHolder && (
        <div className="mb-2 bg-yellow-50 dark:bg-yellow-900/30 border border-yellow-200 dark:border-yellow-800 rounded-lg px-4 py-3 flex items-center gap-3">
          <Lock className="w-5 h-5 text-yellow-600 dark:text-yellow-400 flex-shrink-0" />
          <div>
            <p className="font-medium text-yellow-800 dark:text-yellow-200 text-sm">
              Currently being edited by {activeLockHolder}
            </p>
          </div>
        </div>
      )}
      {/* Header */}
      <div className="flex items-center justify-between px-1 pb-3">
        <div className="flex items-center gap-3">
          <button
            onClick={() => navigate('/sheets')}
            className="p-2 text-gray-500 hover:text-gray-700 hover:bg-gray-100 dark:hover:bg-gray-700 rounded-lg"
          >
            <ArrowLeft className="w-5 h-5" />
          </button>
          {isDesignMode && (
            <button
              onClick={() => setShowPanel(!showPanel)}
              className="p-2 text-gray-500 hover:text-gray-700 hover:bg-gray-100 dark:hover:bg-gray-700 rounded-lg"
              title={showPanel ? 'Hide entity panel' : 'Show entity panel'}
            >
              {showPanel ? <PanelLeftClose className="w-5 h-5" /> : <PanelLeft className="w-5 h-5" />}
            </button>
          )}
          {isDesignMode ? (
            <input
              type="text"
              value={title}
              onChange={(e) => setTitle(e.target.value)}
              placeholder="Untitled Sheet"
              className="text-2xl font-bold text-gray-900 dark:text-gray-100 bg-transparent border-none outline-none placeholder-gray-400"
            />
          ) : (
            <h1 className="text-2xl font-bold text-gray-900 dark:text-gray-100">
              {title || 'Untitled Sheet'}
            </h1>
          )}
        </div>
        <div className="flex items-center gap-2">
          {dataStore.isLoading && (
            <span className="text-xs text-gray-400 animate-pulse">Fetching data...</span>
          )}
          <button
            onClick={dataStore.refresh}
            className="p-2 text-gray-500 hover:text-gray-700 hover:bg-gray-100 dark:hover:bg-gray-700 rounded-lg"
            title="Refresh data"
          >
            <RefreshCw className="w-4 h-4" />
          </button>
          <button
            onClick={handleExport}
            className="p-2 text-gray-500 hover:text-gray-700 hover:bg-gray-100 dark:hover:bg-gray-700 rounded-lg"
            title="Export to .xlsx"
          >
            <Download className="w-4 h-4" />
          </button>
          {!isNew && isOwner && isDesignMode && (
            <button
              onClick={() => setShowSharingDialog(true)}
              className="p-2 text-gray-500 hover:text-gray-700 hover:bg-gray-100 dark:hover:bg-gray-700 rounded-lg"
              title="Sharing settings"
            >
              <Share2 className="w-4 h-4" />
            </button>
          )}
          {isDesignMode && (
            <button
              onClick={handleSave}
              disabled={saving}
              className="inline-flex items-center gap-2 px-4 py-2 bg-primary-600 text-white rounded-lg hover:bg-primary-700 transition-colors disabled:opacity-50"
            >
              <Save className="w-4 h-4" />
              {saving ? 'Saving...' : 'Save'}
            </button>
          )}
        </div>
      </div>

      {/* Stale field warning banner */}
      {staleFields.length > 0 && (
        <div className="flex items-center gap-2 px-3 py-2 mb-2 bg-yellow-50 dark:bg-yellow-900/30 border border-yellow-200 dark:border-yellow-800 rounded-lg text-sm text-yellow-800 dark:text-yellow-200">
          <AlertTriangle className="w-4 h-4 flex-shrink-0" />
          <span>
            This sheet references fields that no longer exist:{' '}
            {staleFields.map((sf) => `${sf.entity}.${sf.field}`).join(', ')}.
            {isDesignMode ? ' Edit formulas to fix.' : ''}
          </span>
          <button
            onClick={() => setStaleFields([])}
            className="ml-auto text-yellow-600 dark:text-yellow-400 hover:text-yellow-800 dark:hover:text-yellow-200"
          >
            ×
          </button>
        </div>
      )}

      {/* Main Content: Panel + Spreadsheet */}
      <div className="flex flex-1 min-h-0 rounded-lg border border-gray-200 dark:border-gray-700 shadow">
        {/* Entity Source Panel (design mode only) */}
        {isDesignMode && showPanel && schemas && (
          <EntitySourcePanel
            schemas={schemas}
            activeSources={activeSources}
            unauthorizedEntities={dataStore.unauthorizedEntities}
            onAddSource={handleAddSource}
            onRemoveSource={handleRemoveSource}
          />
        )}

        {/* Univer Spreadsheet Container */}
        <div ref={univerContainerRef} className="flex-1 min-w-0" />
      </div>

      {/* Repeater drop dialog */}
      {pendingRepeaterDrop && (
        <RepeaterDropDialog
          entityName={pendingRepeaterDrop.entityName}
          repeaterPath={pendingRepeaterDrop.fieldName}
          repeaterLabel={pendingRepeaterDrop.fieldLabel}
          itemSchema={pendingRepeaterDrop.itemSchema}
          onConfirm={handleRepeaterConfirm}
          onCancel={() => setPendingRepeaterDrop(null)}
        />
      )}

      {/* Sharing dialog */}
      {showSharingDialog && (
        <SheetSharingDialog
          isOwner={isOwner}
          sharing={sharing}
          onChange={setSharing}
          onClose={handleSharingDone}
          canPeekUsers={capabilities?.['users']?.can_peek_all ?? false}
          canPeekRoles={capabilities?.['iam-role']?.can_peek_all ?? false}
          authorId={sheetAuthorId}
          onAuthorChange={setSheetAuthorId}
          isSystemOwner={isOwner && sheetAuthorId !== currentUser?.id}
        />
      )}
    </div>
  );
}

/**
 * Finds a FieldSchema by name, including top-level fields only.
 * Used to detect whether a dragged field is a Repeater.
 */
function findFieldSchema(fields: FieldSchema[], name: string): FieldSchema | undefined {
  return fields.find((f) => f.name === name);
}

/** Converts a 0-based column index to a spreadsheet letter (0→A, 1→B, … 25→Z, 26→AA). */
function columnToLetter(col: number): string {
  let s = '';
  let c = col;
  while (c >= 0) {
    s = String.fromCharCode((c % 26) + 65) + s;
    c = Math.floor(c / 26) - 1;
  }
  return s;
}
