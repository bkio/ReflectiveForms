import { useRef, useEffect, useCallback, useState } from 'react';
import { UseFormReturn } from 'react-hook-form';
import { toast } from 'sonner';

interface UseAutoSaveOptions {
  form: UseFormReturn<any>;
  onSave: () => Promise<void>;
  delay?: number;
  enabled?: boolean;
}

interface AutoSaveState {
  status: 'idle' | 'pending' | 'saving' | 'saved' | 'error';
  lastSaved: Date | null;
  error: string | null;
}

export function useAutoSave({ form, onSave, delay = 5000, enabled = true }: UseAutoSaveOptions) {
  const saveTimeoutRef = useRef<NodeJS.Timeout | null>(null);
  const [state, setState] = useState<AutoSaveState>({
    status: 'idle',
    lastSaved: null,
    error: null,
  });

  const clearPendingSave = useCallback(() => {
    if (saveTimeoutRef.current) {
      clearTimeout(saveTimeoutRef.current);
      saveTimeoutRef.current = null;
    }
  }, []);

  const triggerSave = useCallback(async () => {
    clearPendingSave();

    setState(prev => ({ ...prev, status: 'saving', error: null }));

    try {
      await onSave();
      setState({
        status: 'saved',
        lastSaved: new Date(),
        error: null,
      });
    } catch (error) {
      setState(prev => ({
        ...prev,
        status: 'error',
        error: error instanceof Error ? error.message : 'Save failed',
      }));
    }
  }, [onSave, clearPendingSave]);

  const scheduleSave = useCallback(() => {
    if (!enabled) return;

    clearPendingSave();
    setState(prev => ({ ...prev, status: 'pending' }));

    saveTimeoutRef.current = setTimeout(triggerSave, delay);
  }, [enabled, delay, triggerSave, clearPendingSave]);

  // Watch form changes
  useEffect(() => {
    if (!enabled) return;

    const subscription = form.watch(() => {
      scheduleSave();
    });

    return () => {
      subscription.unsubscribe();
      clearPendingSave();
    };
  }, [form, scheduleSave, clearPendingSave, enabled]);

  // Save immediately (e.g., on submit button click)
  const saveNow = useCallback(async () => {
    await triggerSave();
  }, [triggerSave]);

  // Cancel pending save
  const cancel = useCallback(() => {
    clearPendingSave();
    setState(prev => ({ ...prev, status: 'idle' }));
  }, [clearPendingSave]);

  return {
    ...state,
    saveNow,
    cancel,
    isPending: state.status === 'pending',
    isSaving: state.status === 'saving',
  };
}

/**
 * Hook for showing save status in the UI
 */
export function useSaveIndicator(autoSave: ReturnType<typeof useAutoSave>) {
  useEffect(() => {
    if (autoSave.status === 'pending') {
      toast.loading('Changes will be saved...', { id: 'auto-save', duration: Infinity });
    } else if (autoSave.status === 'saving') {
      toast.loading('Saving...', { id: 'auto-save', duration: Infinity });
    } else if (autoSave.status === 'saved') {
      toast.success('Saved', { id: 'auto-save', duration: 2000 });
    } else if (autoSave.status === 'error') {
      toast.error(autoSave.error || 'Save failed', { id: 'auto-save', duration: 5000 });
    }
  }, [autoSave.status, autoSave.error]);

  return autoSave;
}
