import { useEffect, useRef, useCallback, useState } from 'react';
import { toast } from 'sonner';
import { tryLockEntity, unlockEntity, fetchLockStatus, getApiBaseUrl } from '../api/client';

const HEARTBEAT_CHECK_INTERVAL = 15000; // Check every 15 seconds
const INACTIVITY_TIMEOUT = 60000; // Lock expires after 60 seconds of no save activity

interface UseEntityLockOptions {
  enabled?: boolean;
  onLockFailed?: (lockedBy: string) => void;
  onLockLost?: () => void;
}

export function useEntityLock(
  entityName: string,
  entityId: number | undefined,
  options: UseEntityLockOptions = {}
) {
  const { enabled = true, onLockFailed, onLockLost } = options;

  const lockIntervalRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const isLockedRef = useRef(false);
  const lastActivityRef = useRef<number>(Date.now());
  const [lockStatus, setLockStatus] = useState<'idle' | 'locked' | 'failed' | 'error'>('idle');
  const [lockedBy, setLockedBy] = useState<string | null>(null);

  const acquireLock = useCallback(async (): Promise<boolean> => {
    if (!entityId || entityId < 0 || !enabled) {
      return true; // No lock needed for new entities
    }

    try {
      const result = await tryLockEntity(entityName, entityId);

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
          onLockFailed?.(lockOwner);
          toast.error(`This entity is being edited by ${lockOwner}`);
          return false;
        }
        setLockStatus('error');
        console.error('Lock request failed:', result.error);
        return false;
      }

      isLockedRef.current = true;
      setLockStatus('locked');
      return true;
    } catch (error) {
      console.error('Failed to acquire lock:', error);
      setLockStatus('error');
      return false;
    }
  }, [entityName, entityId, enabled, onLockFailed]);

  const releaseLock = useCallback(async (): Promise<void> => {
    if (!entityId || entityId < 0 || !isLockedRef.current) {
      return;
    }

    try {
      await unlockEntity(entityName, entityId);
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
    onLockLost?.();
  }, [onLockLost]);

  // Heartbeat: only refreshes the lock if there has been recent save activity.
  // If the user has been idle longer than INACTIVITY_TIMEOUT, release the lock
  // and notify via onLockLost so the page can redirect to view-only.
  const heartbeat = useCallback(async (): Promise<void> => {
    if (!isLockedRef.current) return;

    const elapsed = Date.now() - lastActivityRef.current;
    if (elapsed >= INACTIVITY_TIMEOUT) {
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
  }, [acquireLock, releaseLock, handleLockLost]);

  // Acquire lock on mount, run heartbeat periodically, release on unmount
  useEffect(() => {
    if (!enabled || !entityId || entityId < 0) {
      return;
    }

    // Mark initial activity
    lastActivityRef.current = Date.now();

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
  }, [entityId, enabled, acquireLock, heartbeat, releaseLock]);

  // Release lock on page unload
  useEffect(() => {
    const handleBeforeUnload = () => {
      if (isLockedRef.current && entityId && entityId > 0) {
        // Use sendBeacon with form data for reliable delivery on page unload.
        // sendBeacon with Blob sends as a simple CORS request (no preflight),
        // and the URL includes all params so the body is just a placeholder.
        const url = `${getApiBaseUrl()}/entity_lock_control?type=${encodeURIComponent(entityName)}&id=${entityId}&operation=try_unlock`;
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
