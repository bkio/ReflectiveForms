import { useCallback, useEffect, useRef } from 'react';
import { useForm, FormProvider, UseFormReturn } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { toast } from 'sonner';
import { Lock } from 'lucide-react';
import { EntitySchema, EntityData } from '../../types/schema';
import { schemaToZod, generateDefaults } from '../../lib/schemaToZod';
import { FormField } from '../fields/FormField';
import { useSanityCheck, useCreateEntity, useUpdateEntity } from '../../hooks/useEntity';
import { useEntityLock } from '../../hooks/useEntityLock';
import { SearchableSelect } from './SearchableSelect';

interface DynamicFormProps {
  schema: EntitySchema;
  initialData?: Partial<EntityData>;
  entityId?: number;
  onSuccess?: (data: EntityData) => void;
}

export function DynamicForm({ schema, initialData, entityId, onSuccess }: DynamicFormProps) {
  const isCreateMode = entityId === undefined || entityId < 0;
  const saveTimeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  // Entity locking for edit mode
  const { lockStatus, lockedBy } = useEntityLock(
    schema.entity_name,
    entityId,
    { enabled: !isCreateMode }
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

  // Check if form should be disabled
  const isFormDisabled = !isCreateMode && lockStatus === 'failed';

  // Auto-save logic
  const handleAutoSave = useCallback(async () => {
    if (isFormDisabled) {
      toast.error('Cannot save: entity is locked by another user');
      return;
    }

    // Normalize date strings from yyyy-MM-dd (HTML input) to yyyyMMdd (backend format)
    // Empty strings are converted to null so the backend skips validation on optional fields
    const normalizeDates = (obj: unknown): unknown => {
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
    };

    const values = normalizeDates(form.getValues()) as Record<string, unknown>;

    // Include entity ID for update operations (backend requires it in the body)
    if (!isCreateMode && entityId !== undefined) {
      values.id = entityId;
    }

    try {

      // Save directly — the CRUD endpoint performs its own sanity check
      const mutation = isCreateMode ? createMutation : updateMutation;
      const result = await mutation.mutateAsync(values as Partial<EntityData>);

      if (result.error) {
        toast.error(result.error);
      } else {
        toast.success('Changes saved');
        if (result.data) {
          onSuccess?.(result.data);
        }
      }
    } catch (err) {
      console.error('Save error:', err);
      toast.error(err instanceof Error ? err.message : 'Save failed');
    }
  }, [form, sanityCheck, createMutation, updateMutation, isCreateMode, onSuccess, isFormDisabled]);

  // Watch for changes and trigger auto-save with debounce
  useEffect(() => {
    if (isFormDisabled) return;

    const subscription = form.watch(() => {
      // Clear existing timeout
      if (saveTimeoutRef.current) {
        clearTimeout(saveTimeoutRef.current);
      }

      // Show pending save indicator
      toast.info('Changes will be saved...', { duration: 5000 });

      // Set new timeout for auto-save
      saveTimeoutRef.current = setTimeout(handleAutoSave, 5000);
    });

    return () => {
      subscription.unsubscribe();
      if (saveTimeoutRef.current) {
        clearTimeout(saveTimeoutRef.current);
      }
    };
  }, [form, handleAutoSave, isFormDisabled]);

  // Manual save
  const handleSubmit = form.handleSubmit(
    async (_data) => {
      if (saveTimeoutRef.current) {
        clearTimeout(saveTimeoutRef.current);
      }
      await handleAutoSave();
    },
    (errors) => {
      console.error('Form validation errors:', JSON.stringify(errors, null, 2));
      const messages = Object.entries(errors)
        .map(([key, err]) => `${key}: ${(err as { message?: string })?.message || JSON.stringify(err)}`)
        .join('; ');
      if (messages) {
        toast.error(`Validation: ${messages}`);
      }
    }
  );

  return (
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

      <form onSubmit={handleSubmit} className="space-y-6">
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
          <AuthorSelect form={form} disabled={isFormDisabled} />
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
