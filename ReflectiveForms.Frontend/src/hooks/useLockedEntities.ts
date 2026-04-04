import { useQuery } from '@tanstack/react-query';
import { fetchAllLocked, LockStatus } from '../api/client';

const POLL_INTERVAL = 10_000; // 10 seconds

/**
 * Polls the all_locked endpoint and returns a Map of entity_id → LockStatus
 * for real-time lock visibility in list views.
 */
export function useLockedEntities(entityName: string) {
  const query = useQuery({
    queryKey: ['locked-entities', entityName],
    queryFn: async () => {
      const res = await fetchAllLocked(entityName);
      if (res.error) return new Map<number, LockStatus>();
      const map = new Map<number, LockStatus>();
      for (const lock of res.data ?? []) {
        map.set(lock.entity_id, lock);
      }
      return map;
    },
    enabled: !!entityName,
    refetchInterval: POLL_INTERVAL,
    refetchIntervalInBackground: false,
  });

  return query.data ?? new Map<number, LockStatus>();
}
