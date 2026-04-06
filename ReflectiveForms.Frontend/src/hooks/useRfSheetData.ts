import { useState, useEffect, useCallback, useRef } from 'react';
import { bulkRead } from '../api/client';
import type { BulkReadSource } from '../types/schema';

export interface RfSheetDataStore {
  /** Map<entityName, Map<entityId, fields>> */
  entityData: Map<string, Map<number, Record<string, unknown>>>;
  /** Entities the user doesn't have permission to read */
  unauthorizedEntities: Set<string>;
  /** Whether data is currently being fetched */
  isLoading: boolean;
  /** Last fetch error (if any) */
  error: string | null;
  /** Manually trigger a refresh */
  refresh: () => void;
  /** Get a specific field value */
  getEntityField: (entity: string, id: number, field: string) => unknown;
  /** Get all rows for an entity */
  getAllEntityRows: (entity: string) => Array<{ id: number; fields: Record<string, unknown> }>;
  /** Fields that have been fetched per entity */
  fetchedFields: Map<string, Set<string>>;
  /** Request additional fields — triggers incremental fetch if needed */
  requestFields: (entityName: string, fields: string[]) => void;
}

export function useRfSheetData(
  sources: BulkReadSource[],
  refreshIntervalSeconds: number = 30,
): RfSheetDataStore {
  const [entityData, setEntityData] = useState<Map<string, Map<number, Record<string, unknown>>>>(new Map());
  const [unauthorizedEntities, setUnauthorizedEntities] = useState<Set<string>>(new Set());
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [fetchedFields, setFetchedFields] = useState<Map<string, Set<string>>>(new Map());
  const intervalRef = useRef<ReturnType<typeof setInterval> | null>(null);
  const mountedRef = useRef(true);
  const sourcesRef = useRef(sources);
  sourcesRef.current = sources;

  // Stable key to detect when sources actually change (including fields)
  const sourcesKey = JSON.stringify(sources);

  const fetchData = useCallback(async () => {
    const currentSources = sourcesRef.current;
    if (currentSources.length === 0) return;
    setIsLoading(true);
    setError(null);

    try {
      const result = await bulkRead(currentSources);
      if (!mountedRef.current) return;

      if (result.error) {
        setError(result.error);
        return;
      }

      if (result.data) {
        const newData = new Map<string, Map<number, Record<string, unknown>>>();
        const newFetchedFields = new Map<string, Set<string>>();
        for (const entityResult of result.data.results) {
          const rowMap = new Map<number, Record<string, unknown>>();
          const fieldSet = new Set<string>();
          for (const row of entityResult.rows) {
            rowMap.set(row.id, row.fields);
            for (const key of Object.keys(row.fields)) {
              fieldSet.add(key);
            }
          }
          newData.set(entityResult.entity, rowMap);
          newFetchedFields.set(entityResult.entity, fieldSet);
        }
        setEntityData(newData);
        setFetchedFields(newFetchedFields);
        setUnauthorizedEntities(new Set(result.data.unauthorized));
      }
    } catch (e) {
      if (mountedRef.current) {
        setError(e instanceof Error ? e.message : 'Unknown error');
      }
    } finally {
      if (mountedRef.current) {
        setIsLoading(false);
      }
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [sourcesKey]);

  // Initial fetch and polling
  useEffect(() => {
    mountedRef.current = true;
    fetchData();

    if (refreshIntervalSeconds > 0) {
      intervalRef.current = setInterval(fetchData, refreshIntervalSeconds * 1000);
    }

    return () => {
      mountedRef.current = false;
      if (intervalRef.current) {
        clearInterval(intervalRef.current);
        intervalRef.current = null;
      }
    };
  }, [fetchData, refreshIntervalSeconds]);

  const getEntityField = useCallback(
    (entity: string, id: number, field: string): unknown => {
      if (unauthorizedEntities.has(entity)) return '#NO_ACCESS';
      const entityRows = entityData.get(entity);
      if (!entityRows) return '#NO_DATA';
      const row = entityRows.get(id);
      if (!row) return '#NOT_FOUND';
      if (!(field in row)) return '#FIELD_REMOVED';
      return row[field];
    },
    [entityData, unauthorizedEntities],
  );

  const getAllEntityRows = useCallback(
    (entity: string): Array<{ id: number; fields: Record<string, unknown> }> => {
      const entityRows = entityData.get(entity);
      if (!entityRows) return [];
      return Array.from(entityRows.entries()).map(([id, fields]) => ({ id, fields }));
    },
    [entityData],
  );

  const requestFields = useCallback(
    (_entityName: string, _fields: string[]) => {
      // This is a no-op placeholder. Field requests are handled by passing
      // updated sources with fields to the hook. The caller (RfSheetPage)
      // extracts required fields from the workbook and passes them via sources.
    },
    [],
  );

  return {
    entityData,
    unauthorizedEntities,
    isLoading,
    error,
    refresh: fetchData,
    getEntityField,
    getAllEntityRows,
    fetchedFields,
    requestFields,
  };
}
