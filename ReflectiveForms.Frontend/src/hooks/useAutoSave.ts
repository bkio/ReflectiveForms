import { useRef, useCallback, useState } from 'react';

interface UseAutoSaveOptions {
  onSanityCheck: () => Promise<{ passed: boolean; errors?: string[] }>;
  onSave: () => Promise<void>;
  countdownDuration?: number;
  enabled?: boolean;
}

export type AutoSaveStatus = 'idle' | 'checking' | 'validation-error' | 'countdown' | 'saving' | 'saved' | 'error';

interface AutoSaveState {
  status: AutoSaveStatus;
  lastSaved: Date | null;
  error: string | null;
  validationErrors: string[];
  countdownRemaining: number;
  countdownTotal: number;
}

export function useAutoSave({
  onSanityCheck,
  onSave,
  countdownDuration = 3000,
  enabled = true,
}: UseAutoSaveOptions) {
  const countdownRef = useRef<ReturnType<typeof setInterval> | null>(null);
  const saveTimeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const statusRef = useRef<AutoSaveStatus>('idle');
  const [state, setState] = useState<AutoSaveState>({
    status: 'idle',
    lastSaved: null,
    error: null,
    validationErrors: [],
    countdownRemaining: 0,
    countdownTotal: countdownDuration,
  });

  const setStatus = useCallback((newState: AutoSaveState | ((prev: AutoSaveState) => AutoSaveState)) => {
    setState(prev => {
      const next = typeof newState === 'function' ? newState(prev) : newState;
      statusRef.current = next.status;
      return next;
    });
  }, []);

  const clearTimers = useCallback(() => {
    if (countdownRef.current) {
      clearInterval(countdownRef.current);
      countdownRef.current = null;
    }
    if (saveTimeoutRef.current) {
      clearTimeout(saveTimeoutRef.current);
      saveTimeoutRef.current = null;
    }
  }, []);

  const performSave = useCallback(async () => {
    clearTimers();
    setStatus(prev => ({ ...prev, status: 'saving', countdownRemaining: 0 }));
    try {
      await onSave();
      setStatus({
        status: 'saved',
        lastSaved: new Date(),
        error: null,
        validationErrors: [],
        countdownRemaining: 0,
        countdownTotal: countdownDuration,
      });
      // Auto-dismiss after 2s
      saveTimeoutRef.current = setTimeout(() => {
        setStatus(prev => (prev.status === 'saved' ? { ...prev, status: 'idle' } : prev));
      }, 2000);
    } catch (err) {
      setStatus(prev => ({
        ...prev,
        status: 'error',
        error: err instanceof Error ? err.message : 'Save failed',
      }));
    }
  }, [onSave, clearTimers, countdownDuration, setStatus]);

  const startCountdown = useCallback(() => {
    clearTimers();
    const step = 100; // Update every 100ms for smooth progress
    let remaining = countdownDuration;

    setStatus(prev => ({
      ...prev,
      status: 'countdown',
      countdownRemaining: countdownDuration,
      countdownTotal: countdownDuration,
    }));

    countdownRef.current = setInterval(() => {
      remaining -= step;
      if (remaining <= 0) {
        clearTimers();
        performSave();
      } else {
        setStatus(prev => (prev.status === 'countdown' ? { ...prev, countdownRemaining: remaining } : prev));
      }
    }, step);
  }, [countdownDuration, clearTimers, performSave, setStatus]);

  // Called on blur / value commit — the main trigger
  const triggerAutoSave = useCallback(async () => {
    if (!enabled) return;

    // If already counting down, restart the countdown (new change came in)
    if (statusRef.current === 'countdown') {
      startCountdown();
      return;
    }

    // Don't interrupt saving or checking
    if (statusRef.current === 'saving' || statusRef.current === 'checking') return;

    clearTimers();

    setStatus(prev => ({ ...prev, status: 'checking', validationErrors: [], error: null }));

    try {
      const result = await onSanityCheck();
      if (result.passed) {
        startCountdown();
      } else {
        setStatus(prev => ({
          ...prev,
          status: 'validation-error',
          validationErrors: result.errors ?? ['Validation failed'],
        }));
      }
    } catch {
      // Sanity check network error — still try to save (CRUD has its own validation)
      startCountdown();
    }
  }, [enabled, onSanityCheck, startCountdown, clearTimers, setStatus]);

  // Immediate save (e.g. save button)
  const saveNow = useCallback(async () => {
    clearTimers();
    await performSave();
  }, [clearTimers, performSave]);

  // Cancel pending save
  const cancel = useCallback(() => {
    clearTimers();
    setStatus(prev => ({ ...prev, status: 'idle', countdownRemaining: 0 }));
  }, [clearTimers, setStatus]);

  // Dismiss validation errors (user acknowledged)
  const dismissValidation = useCallback(() => {
    setStatus(prev => (prev.status === 'validation-error' ? { ...prev, status: 'idle', validationErrors: [] } : prev));
  }, [setStatus]);

  return {
    ...state,
    triggerAutoSave,
    saveNow,
    cancel,
    dismissValidation,
  };
}
