import { useEffect, useRef, useCallback, useState } from 'react';
import { getApiBaseUrl } from '../api/client';

export type LiveUpdateRole = 'editor' | 'viewer';

export type LiveConnectionStatus = 'disconnected' | 'connecting' | 'connected';

/** Maximum reconnect delay (ms) for exponential backoff. */
const MAX_RECONNECT_DELAY = 30_000;
/** Base reconnect delay (ms). Doubles on each consecutive failure. */
const BASE_RECONNECT_DELAY = 1_000;

interface UseLiveUpdatesOptions {
  /** Entity type name (e.g. "objective") */
  entityName: string;
  /** Entity numeric ID. Omit or pass undefined to disable. */
  entityId: number | undefined;
  /** Whether this client is the editor or a viewer. */
  role: LiveUpdateRole;
  /** Called on the viewer side when the editor sends an update. */
  onUpdate?: (data: Record<string, unknown>) => void;
  /** Set to false to disable the connection entirely. */
  enabled?: boolean;
}

/**
 * Hook for WebSocket-based live entity updates.
 *
 * **Editor usage:** call `broadcastUpdate(formValues)` whenever form values change.
 * The hook debounces broadcasts by 300ms to avoid flooding the wire.
 *
 * **Viewer usage:** provide an `onUpdate` callback to receive live snapshots
 * from the editor.
 *
 * Connection is automatically established when `enabled`, `entityName` and
 * `entityId` are all valid, and torn down on unmount or when inputs change.
 *
 * **Auto-reconnect:** on unexpected disconnects the hook automatically
 * reconnects with exponential backoff (1s → 2s → 4s … 30s cap).
 */
export function useLiveUpdates({
  entityName,
  entityId,
  role,
  onUpdate,
  enabled = true,
}: UseLiveUpdatesOptions) {
  const wsRef = useRef<WebSocket | null>(null);
  const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const pendingBroadcastRef = useRef<Record<string, unknown> | null>(null);
  const reconnectTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const reconnectAttemptRef = useRef(0);
  const unmountedRef = useRef(false);
  const [status, setStatus] = useState<LiveConnectionStatus>('disconnected');

  // Keep onUpdate in a ref so we don't reconnect when the callback identity changes
  const onUpdateRef = useRef(onUpdate);
  onUpdateRef.current = onUpdate;

  // Build WS URL from the HTTP API base URL
  const getWsUrl = useCallback(() => {
    const httpBase = getApiBaseUrl(); // e.g. "http://localhost:9000/rf/api"
    const wsBase = httpBase.replace(/^http/, 'ws');
    return `${wsBase}/live/${encodeURIComponent(entityName)}/${entityId}?role=${role}`;
  }, [entityName, entityId, role]);

  useEffect(() => {
    unmountedRef.current = false;
    reconnectAttemptRef.current = 0; // Reset backoff when inputs change (new entity, role switch, etc.)

    if (!enabled || !entityName || entityId === undefined || entityId < 0 || Number.isNaN(entityId)) {
      setStatus('disconnected');
      return;
    }

    let intentionallyClosed = false;

    function connect() {
      if (unmountedRef.current || intentionallyClosed) return;

      const url = getWsUrl();
      setStatus('connecting');

      const ws = new WebSocket(url);
      wsRef.current = ws;

      ws.onopen = () => {
        reconnectAttemptRef.current = 0; // Reset backoff on success
        setStatus('connected');
      };

      ws.onmessage = (event) => {
        if (role === 'viewer' && onUpdateRef.current) {
          try {
            const data = JSON.parse(event.data);
            onUpdateRef.current(data);
          } catch {
            // Ignore malformed messages
          }
        }
      };

      ws.onclose = () => {
        wsRef.current = null;
        if (unmountedRef.current || intentionallyClosed) {
          setStatus('disconnected');
          return;
        }
        // Auto-reconnect with exponential backoff
        setStatus('disconnected');
        const attempt = reconnectAttemptRef.current++;
        const delay = Math.min(BASE_RECONNECT_DELAY * Math.pow(2, attempt), MAX_RECONNECT_DELAY);
        reconnectTimerRef.current = setTimeout(connect, delay);
      };

      ws.onerror = () => {
        // onerror is always followed by onclose, so reconnect logic fires there
      };
    }

    connect();

    return () => {
      unmountedRef.current = true;
      intentionallyClosed = true;

      if (reconnectTimerRef.current) {
        clearTimeout(reconnectTimerRef.current);
        reconnectTimerRef.current = null;
      }
      // Flush pending broadcast before closing the socket so the last editor snapshot isn't lost
      if (debounceRef.current) {
        clearTimeout(debounceRef.current);
        debounceRef.current = null;
        const pending = pendingBroadcastRef.current;
        pendingBroadcastRef.current = null;
        const openWs = wsRef.current;
        if (pending && openWs && openWs.readyState === WebSocket.OPEN) {
          try { openWs.send(JSON.stringify(pending)); } catch { /* closing */ }
        }
      }
      const ws = wsRef.current;
      if (ws && (ws.readyState === WebSocket.OPEN || ws.readyState === WebSocket.CONNECTING)) {
        ws.close();
      }
      wsRef.current = null;
      setStatus('disconnected');
    };
  }, [enabled, entityName, entityId, role, getWsUrl]);

  /**
   * Send a form snapshot to viewers. Debounced at 300ms — only the last call
   * within the window is sent. This is designed to be called from form.watch()
   * in the same way the autosave debounce works.
   */
  const broadcastUpdate = useCallback(
    (data: Record<string, unknown>) => {
      if (role !== 'editor') return;

      if (debounceRef.current) clearTimeout(debounceRef.current);

      pendingBroadcastRef.current = data;
      debounceRef.current = setTimeout(() => {
        debounceRef.current = null;
        pendingBroadcastRef.current = null;
        const ws = wsRef.current;
        if (ws && ws.readyState === WebSocket.OPEN) {
          ws.send(JSON.stringify(data));
        }
      }, 300);
    },
    [role],
  );

  return { status, broadcastUpdate };
}
