import { useEffect, useRef, useCallback, useState } from 'react';
import { toast } from 'sonner';
import { tryLockEntity, unlockEntity, fetchLockStatus, getApiBaseUrl } from '../api/client';

const HEARTBEAT_CHECK_INTERVAL = 15000; // Check every 15 seconds
const DEFAULT_INACTIVITY_TIMEOUT = 600000; // Lock expires after 10 minutes of no save activity
const TAB_ID_KEY = '__rf_tab_id';

/**
 * Get or create a unique tab identifier.
 * Uses sessionStorage which is scoped per browser tab — different tabs get
 * different storage even for the same origin. The value survives page
 * refreshes (F5) within the same tab, which is the desired behaviour so
 * the refreshed page re-acquires the same lock.
 */
function getTabId(): string {
  try {
    let id = sessionStorage.getItem(TAB_ID_KEY);
    if (!id) {
      id = crypto.randomUUID();
      sessionStorage.setItem(TAB_ID_KEY, id);
    }
    return id;
  } catch {
    // sessionStorage unavailable (SSR, iframe sandbox) — fall back to in-memory
    return crypto.randomUUID();
  }
}

interface UseEntityLockOptions {
  enabled?: boolean;
  onLockFailed?: (lockedBy: string) => void;
  onLockLost?: () => void;
  /** Inactivity timeout in ms before releasing the lock. Defaults to 600000. */
  inactivityTimeout?: number;
}

export function useEntityLock(
  entityName: string,
  entityId: number | undefined,
  options: UseEntityLockOptions = {}
) {
  const { enabled = true, onLockFailed, onLockLost, inactivityTimeout = DEFAULT_INACTIVITY_TIMEOUT } = options;

  // Stable per-tab identifier — survives F5 refresh, unique across tabs
  const tabIdRef = useRef(getTabId());

  const lockIntervalRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const isLockedRef = useRef(false);
  const lastActivityRef = useRef<number>(Date.now());
  const [lockStatus, setLockStatus] = useState<'idle' | 'locked' | 'failed' | 'error'>('idle');
  const [lockedBy, setLockedBy] = useState<string | null>(null);

  // Keep callbacks in refs so the main effect doesn't re-fire when they change
  const onLockFailedRef = useRef(onLockFailed);
  onLockFailedRef.current = onLockFailed;
  const onLockLostRef = useRef(onLockLost);
  onLockLostRef.current = onLockLost;

  // Track whether we already showed the "locked by" toast for this entity to avoid duplicates
  const failedToastShownRef = useRef(false);

  const acquireLock = useCallback(async (): Promise<boolean> => {
    if (!entityId || entityId < 0) {
      return true; // No lock needed for new entities
    }

    try {
      const result = await tryLockEntity(entityName, entityId, tabIdRef.current);

      if (result.error) {
        // Check if it's a "locked by another user" error (case-insensitive)
        if (result.error.toLowerCase().includes('lock')) {
          setLockStatus('failed');
          // Fetch actual lock owner from status endpoint
          let lockOwner = 'another user';
          try {
            const status = await fetchLockStatus(entityName, entityId);
            if (status.data?.locked_by_user_name) {
              lockOwner = status.data.locked_by_user_name;
            }
          } catch { /* fall back to generic name */ }
          setLockedBy(lockOwner);
          onLockFailedRef.current?.(lockOwner);
          if (!failedToastShownRef.current) {
            failedToastShownRef.current = true;
            toast.error(`This entity is being edited by ${lockOwner}`);
          }
          return false;
        }
        setLockStatus('error');
        console.error('Lock request failed:', result.error);
        return false;
      }

      failedToastShownRef.current = false;
      isLockedRef.current = true;
      setLockStatus('locked');
      return true;
    } catch (error) {
      console.error('Failed to acquire lock:', error);
      setLockStatus('error');
      return false;
    }
  }, [entityName, entityId]);

  const releaseLock = useCallback(async (): Promise<void> => {
    if (!entityId || entityId < 0 || !isLockedRef.current) {
      return;
    }

    try {
      await unlockEntity(entityName, entityId, tabIdRef.current);
      isLockedRef.current = false;
      setLockStatus('idle');
    } catch (error) {
      console.error('Failed to release lock:', error);
    }
  }, [entityName, entityId]);

  // Signal that the user is actively working (called on save attempts).
  // This resets the inactivity timer so the heartbeat keeps the lock alive.
  const signalActivity = useCallback(() => {
    lastActivityRef.current = Date.now();
  }, []);

  const handleLockLost = useCallback(() => {
    isLockedRef.current = false;
    setLockStatus('failed');
    if (lockIntervalRef.current) {
      clearInterval(lockIntervalRef.current);
      lockIntervalRef.current = null;
    }
    toast.error('Your editing session expired due to inactivity. Redirecting to view page…');
    onLockLostRef.current?.();
  }, []);

  // Heartbeat: only refreshes the lock if there has been recent save activity.
  // If the user has been idle longer than inactivityTimeout, release the lock
  // and notify via onLockLost so the page can redirect to view-only.
  const heartbeat = useCallback(async (): Promise<void> => {
    if (!isLockedRef.current) return;

    const elapsed = Date.now() - lastActivityRef.current;
    if (elapsed >= inactivityTimeout) {
      // User has been idle too long — release lock and redirect
      await releaseLock();
      handleLockLost();
      return;
    }

    // Still active — refresh the lock
    const success = await acquireLock();
    if (!success) {
      // Lock was taken by someone else (e.g. expired briefly during a network blip)
      handleLockLost();
    }
  }, [acquireLock, releaseLock, handleLockLost, inactivityTimeout]);

  // Acquire lock on mount, run heartbeat periodically, release on unmount.
  // Only depends on entityId and enabled — callbacks are accessed via refs or
  // stable useCallback instances that don't include `enabled`.
  useEffect(() => {
    if (!enabled || !entityId || entityId < 0) {
      return;
    }

    // Mark initial activity
    lastActivityRef.current = Date.now();
    failedToastShownRef.current = false;

    // Initial lock acquisition
    acquireLock();

    // Periodic heartbeat — checks activity before refreshing
    lockIntervalRef.current = setInterval(heartbeat, HEARTBEAT_CHECK_INTERVAL);

    // Cleanup
    return () => {
      if (lockIntervalRef.current) {
        clearInterval(lockIntervalRef.current);
        lockIntervalRef.current = null;
      }
      releaseLock();
    };
  }, [entityId, enabled]); // eslint-disable-line react-hooks/exhaustive-deps

  // Release lock on page unload
  useEffect(() => {
    const handleBeforeUnload = () => {
      if (isLockedRef.current && entityId && entityId > 0) {
        // Use sendBeacon with form data for reliable delivery on page unload.
        // sendBeacon with Blob sends as a simple CORS request (no preflight),
        // and the URL includes all params so the body is just a placeholder.
        const url = `${getApiBaseUrl()}/entity_lock_control?type=${encodeURIComponent(entityName)}&id=${entityId}&operation=try_unlock&tab_id=${encodeURIComponent(tabIdRef.current)}`;
        const blob = new Blob(['{}'], { type: 'application/x-www-form-urlencoded' });
        navigator.sendBeacon(url, blob);
      }
    };

    window.addEventListener('beforeunload', handleBeforeUnload);
    return () => window.removeEventListener('beforeunload', handleBeforeUnload);
  }, [entityName, entityId]);

  // Handle visibility change (tab switch)
  useEffect(() => {
    const handleVisibilityChange = () => {
      if (document.visibilityState === 'visible' && isLockedRef.current) {
        // Tab just became visible — refresh if still within activity window
        heartbeat();
      }
    };

    document.addEventListener('visibilitychange', handleVisibilityChange);
    return () => document.removeEventListener('visibilitychange', handleVisibilityChange);
  }, [heartbeat]);

  return {
    acquireLock,
    releaseLock,
    signalActivity,
    isLocked: isLockedRef.current,
    lockStatus,
    lockedBy,
  };
}
