import { useEffect, useRef, useCallback, useState } from 'react';
import { toast } from 'sonner';
import { tryLockEntity, unlockEntity } from '../api/client';

const LOCK_REFRESH_INTERVAL = 30000; // 30 seconds
const API_BASE_URL = import.meta.env.VITE_API_BASE_URL || 'http://localhost:9000/rf/api';

interface UseEntityLockOptions {
  enabled?: boolean;
  onLockFailed?: (lockedBy: string) => void;
}

export function useEntityLock(
  entityName: string,
  entityId: number | undefined,
  options: UseEntityLockOptions = {}
) {
  const { enabled = true, onLockFailed } = options;

  const lockIntervalRef = useRef<NodeJS.Timeout | null>(null);
  const isLockedRef = useRef(false);
  const [lockStatus, setLockStatus] = useState<'idle' | 'locked' | 'failed' | 'error'>('idle');
  const [lockedBy, setLockedBy] = useState<string | null>(null);

  const acquireLock = useCallback(async (): Promise<boolean> => {
    if (!entityId || entityId < 0 || !enabled) {
      return true; // No lock needed for new entities
    }

    try {
      const result = await tryLockEntity(entityName, entityId);

      if (result.error) {
        // Check if it's a "locked by another user" error
        if (result.error.includes('locked')) {
          setLockStatus('failed');
          const lockOwner = 'another user';
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

  const refreshLock = useCallback(async (): Promise<void> => {
    if (isLockedRef.current) {
      const success = await acquireLock();
      if (!success) {
        // Lock was lost, clear interval
        if (lockIntervalRef.current) {
          clearInterval(lockIntervalRef.current);
          lockIntervalRef.current = null;
        }
      }
    }
  }, [acquireLock]);

  // Acquire lock on mount, refresh periodically, release on unmount
  useEffect(() => {
    if (!enabled || !entityId || entityId < 0) {
      return;
    }

    // Initial lock acquisition
    acquireLock();

    // Refresh lock periodically
    lockIntervalRef.current = setInterval(refreshLock, LOCK_REFRESH_INTERVAL);

    // Cleanup
    return () => {
      if (lockIntervalRef.current) {
        clearInterval(lockIntervalRef.current);
        lockIntervalRef.current = null;
      }
      releaseLock();
    };
  }, [entityId, enabled, acquireLock, refreshLock, releaseLock]);

  // Release lock on page unload
  useEffect(() => {
    const handleBeforeUnload = () => {
      if (isLockedRef.current && entityId && entityId > 0) {
        // Use sendBeacon for reliable delivery on page unload
        const url = `${API_BASE_URL}/entity_lock?type=${encodeURIComponent(entityName)}&id=${entityId}&action=unlock`;
        navigator.sendBeacon(url);
      }
    };

    window.addEventListener('beforeunload', handleBeforeUnload);
    return () => window.removeEventListener('beforeunload', handleBeforeUnload);
  }, [entityName, entityId]);

  // Handle visibility change (tab switch)
  useEffect(() => {
    const handleVisibilityChange = () => {
      if (document.visibilityState === 'visible' && isLockedRef.current) {
        // Refresh lock when tab becomes visible
        refreshLock();
      }
    };

    document.addEventListener('visibilitychange', handleVisibilityChange);
    return () => document.removeEventListener('visibilitychange', handleVisibilityChange);
  }, [refreshLock]);

  return {
    acquireLock,
    releaseLock,
    isLocked: isLockedRef.current,
    lockStatus,
    lockedBy,
  };
}
