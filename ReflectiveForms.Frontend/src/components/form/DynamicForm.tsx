import { useCallback, useEffect, useRef, createContext, useContext } from 'react';
import { useForm, useWatch, FormProvider, UseFormReturn } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { Lock } from 'lucide-react';
import { useNavigate } from 'react-router-dom';
import { EntitySchema, EntityData } from '../../types/schema';
import { schemaToZod, generateDefaults } from '../../lib/schemaToZod';
import { FormField } from '../fields/FormField';
import { useSanityCheck, useCreateEntity, useUpdateEntity, useGlobalSettings } from '../../hooks/useEntity';
import { useEntityLock } from '../../hooks/useEntityLock';
import { useAutoSave } from '../../hooks/useAutoSave';
import { useLiveUpdates } from '../../hooks/useLiveUpdates';
import { AutoSaveIndicator } from './AutoSaveIndicator';
import { SearchableSelect } from './SearchableSelect';
import { useAiAssistantOptional } from '../../lib/AiAssistantContext';

/** Context providing entity-level info to nested field components (AI integrations, etc.) */
interface EntityFormContextValue {
  entityName: string;
  canUpdate: boolean;
}

const EntityFormContext = createContext<EntityFormContextValue | null>(null);

export function useEntityFormContext(): EntityFormContextValue | null {
  return useContext(EntityFormContext);
}

/**
 * Fix date formats in the payload so they match what the backend expects.
 * normalizeDates() converts ALL yyyy-MM-dd strings to yyyyMMdd, but
 * fields with dateFormat="yyyy-MM-dd" must stay in hyphenated form.
 */
function fixDateFormatsForSchema(values: Record<string, unknown>, schema: EntitySchema): void {
  const fields = values.fields as Record<string, unknown> | undefined;
  if (!fields) return;
  for (const fieldSchema of schema.fields) {
    if (fieldSchema.type !== 'DatePicker') continue;
    const fmt = fieldSchema.date_options?.format;
    if (!fmt || fmt === 'yyyyMMdd') continue; // already converted by normalizeDates
    // fmt is e.g. "yyyy-MM-dd" — convert yyyyMMdd back to yyyy-MM-dd
    const val = fields[fieldSchema.name];
    if (typeof val === 'string' && /^\d{8}$/.test(val)) {
      fields[fieldSchema.name] = `${val.slice(0, 4)}-${val.slice(4, 6)}-${val.slice(6, 8)}`;
    }
  }
}

interface DynamicFormProps {
  schema: EntitySchema;
  initialData?: Partial<EntityData>;
  entityId?: number;
  onSuccess?: (data: EntityData) => void;
}

export function DynamicForm({ schema, initialData, entityId, onSuccess }: DynamicFormProps) {
  const isCreateMode = entityId === undefined || Number.isNaN(entityId) || entityId < 0;
  const formRef = useRef<HTMLFormElement>(null);
  const navigate = useNavigate();
  const globalSettings = useGlobalSettings();

  // Entity locking for edit mode
  const { lockStatus, lockedBy, signalActivity } = useEntityLock(
    schema.entity_name,
    entityId,
    {
      enabled: !isCreateMode,
      inactivityTimeout: globalSettings.edit_inactivity_timeout_ms,
      onLockFailed: () => {
        // Another tab/user holds the lock — show warning banner and disable form (handled by isFormDisabled)
      },
      onLockLost: () => {
        // Redirect to view-only page when lock expires due to inactivity
        navigate(`/entities-view/${schema.entity_name}?id=${entityId}`);
      },
    }
  );

  // Build Zod schema from entity schema
  const zodSchema = schemaToZod(schema);

  // Initialize form
  const form = useForm({
    resolver: zodResolver(zodSchema),
    defaultValues: initialData ?? generateDefaults(schema),
    mode: 'onChange',
  });

  // Expose setValue for E2E testing
  useEffect(() => {
    if (import.meta.env.DEV) {
      (window as any).__rfFormSetValue = form.setValue;
      return () => { delete (window as any).__rfFormSetValue; };
    }
  }, [form.setValue]);

  // Mutations
  const sanityCheck = useSanityCheck(schema.entity_name);
  const createMutation = useCreateEntity(schema.entity_name);
  const updateMutation = useUpdateEntity(schema.entity_name);

  // Push form errors to AI assistant context (debounced)
  const assistant = useAiAssistantOptional();
  const formErrors = form.formState.errors;
  useEffect(() => {
    if (!assistant) return;
    const errorMessages: string[] = [];
    const collectErrors = (errs: Record<string, unknown>, prefix = '') => {
      for (const [key, val] of Object.entries(errs)) {
        if (val && typeof val === 'object' && 'message' in (val as Record<string, unknown>)) {
          errorMessages.push(`${prefix}${key}: ${(val as { message?: string }).message}`);
        } else if (val && typeof val === 'object') {
          collectErrors(val as Record<string, unknown>, `${prefix}${key}.`);
        }
      }
    };
    collectErrors(formErrors as unknown as Record<string, unknown>);
    assistant.setContext({
      errors: errorMessages.length > 0 ? errorMessages : undefined,
    });
  }, [formErrors, assistant]);

  // Push current form field values to AI assistant context (debounced 1s)
  const watchedFields = useWatch({ control: form.control, name: 'fields' });
  const fieldsPushTimerRef = useRef<ReturnType<typeof setTimeout>>();
  useEffect(() => {
    if (!assistant) return;
    clearTimeout(fieldsPushTimerRef.current);
    fieldsPushTimerRef.current = setTimeout(() => {
      assistant.setContext({
        current_fields: watchedFields as Record<string, unknown> | undefined,
      });
    }, 1000);
    return () => clearTimeout(fieldsPushTimerRef.current);
  }, [watchedFields, assistant]);

  // Handle set_field actions from AI assistant (apply suggested values to form)
  useEffect(() => {
    if (!assistant) return;
    return assistant.subscribeAutoAction((action) => {
      if (action.action_type === 'set_field' && action.payload) {
        const fieldName = (action.payload as Record<string, unknown>).field_name as string;
        const value = (action.payload as Record<string, unknown>).suggested_value;
        if (fieldName === '__title__') {
          // Special: set the entity title
          // eslint-disable-next-line @typescript-eslint/no-explicit-any
          form.setValue('title.rendered' as any, value as any, { shouldDirty: true });
        } else if (fieldName) {
          // eslint-disable-next-line @typescript-eslint/no-explicit-any
          form.setValue(`fields.${fieldName}` as any, value as any, { shouldDirty: true });
        }
      }
    });
  }, [assistant, form]);

  // Check if form should be disabled
  const isFormDisabled = !isCreateMode && lockStatus === 'failed';

  // Normalize date strings from yyyy-MM-dd (HTML input) to yyyyMMdd (backend format)
  // Empty strings are converted to null so the backend skips validation on optional fields
  const normalizeDates = useCallback((obj: unknown): unknown => {
    if (typeof obj === 'string') {
      if (/^\d{4}-\d{2}-\d{2}$/.test(obj)) return obj.replace(/-/g, '');
      return obj;
    }
    if (Array.isArray(obj)) return obj.map(normalizeDates);
    if (obj && typeof obj === 'object') {
      return Object.fromEntries(
        Object.entries(obj as Record<string, unknown>).map(([k, v]) => [k, normalizeDates(v)])
      );
    }
    return obj;
  }, []);

  const getPayload = useCallback(() => {
    const values = normalizeDates(form.getValues()) as Record<string, unknown>;
    if (!isCreateMode && entityId !== undefined) {
      values.id = entityId;
    }
    // Fix date formats: normalizeDates converts yyyy-MM-dd → yyyyMMdd
    // unconditionally, but fields with dateFormat="yyyy-MM-dd" must stay
    // in that format for the backend sanity check.
    fixDateFormatsForSchema(values, schema);
    return values;
  }, [form, normalizeDates, isCreateMode, entityId, schema]);

  // Strip the backend "Sanity check for X, entity id: N has failed with" prefix
  const humanizeError = useCallback(
    (msg: string): string => humanizeSanityError(msg),
    [],
  );

  // Sanity check callback for autosave
  const handleSanityCheck = useCallback(async () => {
    signalActivity(); // Any save attempt counts as activity
    const values = getPayload();
    try {
      const result = await sanityCheck.mutateAsync(values as Partial<EntityData>);
      if (result.error) {
        return { passed: false, errors: [humanizeError(result.error)] };
      }
      return { passed: true };
    } catch (err) {
      const msg = err instanceof Error ? err.message : 'Validation failed';
      return { passed: false, errors: [humanizeError(msg)] };
    }
  }, [sanityCheck, getPayload, humanizeError, signalActivity]);

  // Save callback for autosave
  const handleSave = useCallback(async () => {
    signalActivity(); // Any save attempt counts as activity
    if (isFormDisabled) {
      throw new Error('Cannot save: entity is locked by another user');
    }
    const values = getPayload();
    const mutation = isCreateMode ? createMutation : updateMutation;
    const result = await mutation.mutateAsync(values as Partial<EntityData>);
    if (result.error) {
      throw new Error(humanizeError(result.error));
    }
    if (result.data) {
      onSuccess?.(result.data);
    }
  }, [form, createMutation, updateMutation, isCreateMode, onSuccess, isFormDisabled, getPayload, humanizeError, signalActivity]);

  // Auto-save hook
  const autoSave = useAutoSave({
    onSanityCheck: handleSanityCheck,
    onSave: handleSave,
    countdownDuration: 3000,
    enabled: !isFormDisabled,
  });

  // Live updates: when form is the active editor, broadcast changes to viewers.
  // When this window failed to acquire the lock (isFormDisabled), connect as a
  // viewer instead so the locked-out editor also receives live updates.
  // IMPORTANT: only claim editor role after the lock is confirmed ('locked').
  // During 'idle' (lock in-flight) we must NOT connect as editor — doing so
  // would overwrite the real editor's server-side room reference.
  const liveRole = (!isCreateMode && lockStatus === 'locked') ? 'editor' : 'viewer';
  const handleLiveViewerUpdate = useCallback(
    (data: Record<string, unknown>) => {
      // Silently update the disabled form so the locked-out editor sees changes
      if (isFormDisabled) {
        const entries = Object.entries(data);
        for (const [key, value] of entries) {
          form.setValue(key, value, { shouldDirty: false, shouldValidate: false });
        }
      }
    },
    [form, isFormDisabled],
  );
  const { broadcastUpdate } = useLiveUpdates({
    entityName: schema.entity_name,
    entityId,
    role: liveRole,
    onUpdate: handleLiveViewerUpdate,
    enabled: !isCreateMode && lockStatus !== 'idle',
  });
  const broadcastUpdateRef = useRef(broadcastUpdate);
  broadcastUpdateRef.current = broadcastUpdate;

  // Track dirty state and trigger autosave on blur OR debounced value commit.
  // Blur covers text inputs; debounce covers Controller-based fields (Select,
  // Relation, Wysiwyg, Media, Repeater) that don't emit focus/blur.
  //
  // IMPORTANT: autoSave is a new object every render (spread of state + callbacks).
  // We use a ref so the debounce timer and blur handler always call the latest
  // triggerAutoSave without the useEffect re-running and clearing the timer.
  const isDirtyRef = useRef(false);
  const commitTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const triggerAutoSaveRef = useRef(autoSave.triggerAutoSave);
  triggerAutoSaveRef.current = autoSave.triggerAutoSave;

  useEffect(() => {
    if (isFormDisabled) return;
    const subscription = form.watch(() => {
      isDirtyRef.current = true;

      // Broadcast live update to viewers (debounced inside the hook)
      broadcastUpdateRef.current(form.getValues() as Record<string, unknown>);

      // Debounce: if no further change within 600ms, treat as "committed"
      if (commitTimerRef.current) clearTimeout(commitTimerRef.current);
      commitTimerRef.current = setTimeout(() => {
        if (isDirtyRef.current) {
          isDirtyRef.current = false;
          triggerAutoSaveRef.current();
        }
      }, 600);
    });
    return () => {
      subscription.unsubscribe();
      if (commitTimerRef.current) clearTimeout(commitTimerRef.current);
    };
  }, [form, isFormDisabled]);

  const handleFormBlur = useCallback(() => {
    if (isDirtyRef.current && !isFormDisabled) {
      // Clear the debounce timer since blur is a more immediate signal
      if (commitTimerRef.current) {
        clearTimeout(commitTimerRef.current);
        commitTimerRef.current = null;
      }
      isDirtyRef.current = false;
      triggerAutoSaveRef.current();
    }
  }, [isFormDisabled]);

  // Manual save — bypass Zod (same path as autosave: backend sanity check is
  // the source of truth). Also cancel any pending autosave and clear dirty flag
  // so autosave doesn't re-trigger after a successful manual save.
  const handleManualSave = useCallback(async (e?: React.FormEvent) => {
    e?.preventDefault();
    autoSave.cancel();
    isDirtyRef.current = false;
    if (commitTimerRef.current) {
      clearTimeout(commitTimerRef.current);
      commitTimerRef.current = null;
    }
    await autoSave.saveNow();
  }, [autoSave]);

  return (
    <EntityFormContext.Provider value={{ entityName: schema.entity_name, canUpdate: !isFormDisabled }}>
    <FormProvider {...form}>
      {/* Lock warning banner */}
      {isFormDisabled && (
        <div className="mb-6 bg-yellow-50 border border-yellow-200 rounded-lg p-4 flex items-center gap-3">
          <Lock className="w-5 h-5 text-yellow-600 flex-shrink-0" />
          <div>
            <p className="font-medium text-yellow-800">
              This entity is locked
            </p>
            <p className="text-sm text-yellow-700">
              {lockedBy} is currently editing this entity. You can view but not edit.
            </p>
          </div>
        </div>
      )}

      {/* Auto-save indicator */}
      <AutoSaveIndicator
        status={autoSave.status}
        countdownRemaining={autoSave.countdownRemaining}
        countdownTotal={autoSave.countdownTotal}
        validationErrors={autoSave.validationErrors}
        error={autoSave.error}
        onDismissValidation={autoSave.dismissValidation}
      />

      <form ref={formRef} onSubmit={handleManualSave} onBlur={handleFormBlur} className="space-y-6">
        {/* Title field (always present) */}
        <div className="bg-white rounded-lg shadow-sm border border-gray-200 p-4">
          <label className="block text-sm font-medium text-gray-700 mb-2">
            Title <span className="text-red-500">*</span>
          </label>
          <input
            {...form.register('title.rendered')}
            disabled={isFormDisabled}
            className={`w-full px-3 py-2 border border-gray-300 rounded-md shadow-sm focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-blue-500 ${isFormDisabled ? 'bg-gray-100 cursor-not-allowed' : ''}`}
            placeholder={`Enter ${schema.readable_name.singular.toLowerCase()} title`}
          />
          {form.formState.errors.title && (
            <p className="mt-1 text-sm text-red-600">
              {(form.formState.errors.title as { rendered?: { message?: string } })?.rendered?.message}
            </p>
          )}
        </div>

        {/* Author field (when entity has author feature) */}
        {schema.features.has_author && (
          <AuthorSelect form={form} disabled={isFormDisabled || (!isCreateMode && !(initialData?.can_edit_author ?? false))} />
        )}

        {/* Tags field (when entity has tags feature) */}
        {schema.features.has_tags && (
          <TagsSelect form={form} disabled={isFormDisabled} />
        )}

        {/* Categories field (when entity has categories feature) */}
        {schema.features.has_categories && (
          <CategoriesSelect form={form} disabled={isFormDisabled} />
        )}

        {/* Parent field (when entity has parent-child feature) */}
        {schema.features.has_parent_child && (
          <ParentSelect form={form} disabled={isFormDisabled} entityName={schema.entity_name} entityId={entityId} />
        )}

        {/* Entity fields */}
        <fieldset disabled={isFormDisabled} className={isFormDisabled ? 'opacity-60' : ''}>
          {schema.fields.map((fieldSchema) => (
            <FormField key={fieldSchema.name} fieldSchema={fieldSchema} />
          ))}
        </fieldset>

        {/* Submit button */}
        <div className="flex justify-end gap-4">
          <button
            type="submit"
            disabled={form.formState.isSubmitting || isFormDisabled}
            className="
              px-6 py-2 bg-blue-600 text-white rounded-md
              hover:bg-blue-700 disabled:opacity-50 disabled:cursor-not-allowed
              transition-colors
            "
          >
            {form.formState.isSubmitting ? 'Saving...' : 'Save Now'}
          </button>
        </div>
      </form>
    </FormProvider>
    </EntityFormContext.Provider>
  );
}

function AuthorSelect({ form, disabled }: { form: UseFormReturn; disabled: boolean }) {
  const value = form.watch('author') ?? -1;

  return (
    <div className="field-wrapper bg-white rounded-lg shadow-sm border border-gray-200 p-4" data-testid="author-select">
      <label className="block text-sm font-medium text-gray-700 mb-2">
        Author <span className="text-red-500">*</span>
      </label>
      <SearchableSelect
        entityName="users"
        value={value}
        onChange={(val) => form.setValue('author', val, { shouldDirty: true })}
        disabled={disabled}
        placeholder="-- Select Author --"
      />
    </div>
  );
}

function TagsSelect({ form, disabled }: { form: UseFormReturn; disabled: boolean }) {
  const values = form.watch('tags') ?? [];

  return (
    <div className="field-wrapper bg-white rounded-lg shadow-sm border border-gray-200 p-4" data-testid="tags-select">
      <label className="block text-sm font-medium text-gray-700 mb-2">
        Tags
      </label>
      <SearchableSelect
        entityName="tags"
        multiSelect
        multiValue={values}
        onMultiChange={(vals) => form.setValue('tags', vals, { shouldDirty: true })}
        disabled={disabled}
        placeholder="-- Select Tags --"
      />
    </div>
  );
}

function CategoriesSelect({ form, disabled }: { form: UseFormReturn; disabled: boolean }) {
  const values = form.watch('categories') ?? [];

  return (
    <div className="field-wrapper bg-white rounded-lg shadow-sm border border-gray-200 p-4" data-testid="categories-select">
      <label className="block text-sm font-medium text-gray-700 mb-2">
        Categories
      </label>
      <SearchableSelect
        entityName="categories"
        multiSelect
        multiValue={values}
        onMultiChange={(vals) => form.setValue('categories', vals, { shouldDirty: true })}
        disabled={disabled}
        placeholder="-- Select Categories --"
      />
    </div>
  );
}

function ParentSelect({ form, disabled, entityName, entityId }: { form: UseFormReturn; disabled: boolean; entityName: string; entityId?: number }) {
  const value = form.watch('parent') ?? -1;

  return (
    <div className="field-wrapper bg-white rounded-lg shadow-sm border border-gray-200 p-4" data-testid="parent-select">
      <label className="block text-sm font-medium text-gray-700 mb-2">
        Parent
      </label>
      <SearchableSelect
        entityName={entityName}
        value={value}
        onChange={(val) => form.setValue('parent', val, { shouldDirty: true })}
        disabled={disabled}
        excludeId={entityId}
        placeholder="-- Select Parent --"
      />
    </div>
  );
}

/**
 * Humanize a backend sanity-check error message by:
 * - Stripping the backend prefix ("Sanity check for ... has failed with")
 * - Stripping raw "Field value: ..." suffixes
 * - Rewriting "has to be in length between X and Y" → "must be between X and Y characters"
 */
export function humanizeSanityError(msg: string, _fieldLabelMap: Record<string, string> = {}): string {
  let cleaned = msg.replace(/^Sanity check for .+ has failed with\s*/i, '');
  cleaned = cleaned.replace(/\s*Field value:.*$/i, '');
  cleaned = cleaned.replace(/has to be in length between (\d+) and (\d+)/gi, 'must be between $1 and $2 characters');
  return cleaned;
}
