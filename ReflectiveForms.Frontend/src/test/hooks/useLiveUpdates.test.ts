import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, act, waitFor } from '@testing-library/react';
import { createElement } from 'react';
import { useLiveUpdates } from '../../hooks/useLiveUpdates';

// Mock the API client
vi.mock('../../api/client', () => ({
  getApiBaseUrl: vi.fn(() => 'http://localhost:9000/rf/api'),
}));

// ── WebSocket mock ──────────────────────────────────────────────
type WsListener = (event: { data: string }) => void;

class MockWebSocket {
  static readonly CONNECTING = 0;
  static readonly OPEN = 1;
  static readonly CLOSING = 2;
  static readonly CLOSED = 3;
  static instances: MockWebSocket[] = [];

  readyState = MockWebSocket.CONNECTING;
  url: string;
  onopen: (() => void) | null = null;
  onclose: (() => void) | null = null;
  onmessage: WsListener | null = null;
  onerror: (() => void) | null = null;
  sentMessages: string[] = [];
  closeCalled = false;

  constructor(url: string) {
    this.url = url;
    MockWebSocket.instances.push(this);
  }

  send(data: string) {
    this.sentMessages.push(data);
  }

  close() {
    this.closeCalled = true;
    this.readyState = MockWebSocket.CLOSED;
    this.onclose?.();
  }

  // Helpers for tests
  simulateOpen() {
    this.readyState = MockWebSocket.OPEN;
    this.onopen?.();
  }

  simulateMessage(data: Record<string, unknown>) {
    this.onmessage?.({ data: JSON.stringify(data) });
  }

  simulateClose() {
    this.readyState = MockWebSocket.CLOSED;
    this.onclose?.();
  }

  simulateError() {
    this.onerror?.();
  }
}

function TestWrapper({ children }: { children: React.ReactNode }) {
  return createElement('div', null, children);
}

describe('useLiveUpdates', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.useFakeTimers({ shouldAdvanceTime: true });
    MockWebSocket.instances = [];
    // @ts-expect-error - replacing global WebSocket with mock
    globalThis.WebSocket = MockWebSocket;
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  // ── Basic connection lifecycle ─────────────────────────────────

  it('should start disconnected when disabled', () => {
    const { result } = renderHook(
      () => useLiveUpdates({ entityName: 'test', entityId: 1, role: 'viewer', enabled: false }),
      { wrapper: TestWrapper },
    );

    expect(result.current.status).toBe('disconnected');
    expect(MockWebSocket.instances).toHaveLength(0);
  });

  it('should not connect when entityId is undefined', () => {
    const { result } = renderHook(
      () => useLiveUpdates({ entityName: 'test', entityId: undefined, role: 'viewer' }),
      { wrapper: TestWrapper },
    );

    expect(result.current.status).toBe('disconnected');
    expect(MockWebSocket.instances).toHaveLength(0);
  });

  it('should connect with correct URL for editor role', () => {
    renderHook(
      () => useLiveUpdates({ entityName: 'objective', entityId: 42, role: 'editor' }),
      { wrapper: TestWrapper },
    );

    expect(MockWebSocket.instances).toHaveLength(1);
    expect(MockWebSocket.instances[0].url).toBe(
      'ws://localhost:9000/rf/api/live/objective/42?role=editor',
    );
  });

  it('should connect with correct URL for viewer role', () => {
    renderHook(
      () => useLiveUpdates({ entityName: 'blog-post', entityId: 7, role: 'viewer' }),
      { wrapper: TestWrapper },
    );

    expect(MockWebSocket.instances).toHaveLength(1);
    expect(MockWebSocket.instances[0].url).toBe(
      'ws://localhost:9000/rf/api/live/blog-post/7?role=viewer',
    );
  });

  it('should transition to connected when WebSocket opens', async () => {
    const { result } = renderHook(
      () => useLiveUpdates({ entityName: 'test', entityId: 1, role: 'viewer' }),
      { wrapper: TestWrapper },
    );

    expect(result.current.status).toBe('connecting');

    act(() => {
      MockWebSocket.instances[0].simulateOpen();
    });

    expect(result.current.status).toBe('connected');
  });

  it('should transition to disconnected when WebSocket closes', async () => {
    const { result } = renderHook(
      () => useLiveUpdates({ entityName: 'test', entityId: 1, role: 'viewer' }),
      { wrapper: TestWrapper },
    );

    act(() => MockWebSocket.instances[0].simulateOpen());
    expect(result.current.status).toBe('connected');

    act(() => MockWebSocket.instances[0].simulateClose());
    // Will transition through disconnected then schedule reconnect
    expect(result.current.status).toBe('disconnected');
  });

  // ── Message handling ───────────────────────────────────────────

  it('should call onUpdate when viewer receives a message', async () => {
    const onUpdate = vi.fn();
    renderHook(
      () => useLiveUpdates({ entityName: 'test', entityId: 1, role: 'viewer', onUpdate }),
      { wrapper: TestWrapper },
    );

    act(() => MockWebSocket.instances[0].simulateOpen());

    const payload = { title: { rendered: 'Hello' }, fields: { content: 'World' } };
    act(() => MockWebSocket.instances[0].simulateMessage(payload));

    expect(onUpdate).toHaveBeenCalledWith(payload);
  });

  it('should not call onUpdate in editor role', async () => {
    const onUpdate = vi.fn();
    renderHook(
      () => useLiveUpdates({ entityName: 'test', entityId: 1, role: 'editor', onUpdate }),
      { wrapper: TestWrapper },
    );

    act(() => MockWebSocket.instances[0].simulateOpen());
    act(() => MockWebSocket.instances[0].simulateMessage({ test: true }));

    expect(onUpdate).not.toHaveBeenCalled();
  });

  it('should ignore malformed WebSocket messages', async () => {
    const onUpdate = vi.fn();
    renderHook(
      () => useLiveUpdates({ entityName: 'test', entityId: 1, role: 'viewer', onUpdate }),
      { wrapper: TestWrapper },
    );

    act(() => MockWebSocket.instances[0].simulateOpen());

    // Send non-JSON message
    act(() => {
      MockWebSocket.instances[0].onmessage?.({ data: 'not json' });
    });

    expect(onUpdate).not.toHaveBeenCalled();
  });

  // ── Broadcasting (editor) ──────────────────────────────────────

  it('should debounce broadcastUpdate calls', async () => {
    const { result } = renderHook(
      () => useLiveUpdates({ entityName: 'test', entityId: 1, role: 'editor' }),
      { wrapper: TestWrapper },
    );

    act(() => MockWebSocket.instances[0].simulateOpen());

    const ws = MockWebSocket.instances[0];

    // Send multiple rapid updates
    act(() => {
      result.current.broadcastUpdate({ v: 1 });
      result.current.broadcastUpdate({ v: 2 });
      result.current.broadcastUpdate({ v: 3 });
    });

    // Nothing sent yet (debounce pending)
    expect(ws.sentMessages).toHaveLength(0);

    // Advance past debounce (300ms)
    act(() => vi.advanceTimersByTime(350));

    // Only the last update should have been sent
    expect(ws.sentMessages).toHaveLength(1);
    expect(JSON.parse(ws.sentMessages[0])).toEqual({ v: 3 });
  });

  it('should not broadcastUpdate in viewer role', async () => {
    const { result } = renderHook(
      () => useLiveUpdates({ entityName: 'test', entityId: 1, role: 'viewer' }),
      { wrapper: TestWrapper },
    );

    act(() => MockWebSocket.instances[0].simulateOpen());
    act(() => {
      result.current.broadcastUpdate({ v: 1 });
    });
    act(() => vi.advanceTimersByTime(350));

    expect(MockWebSocket.instances[0].sentMessages).toHaveLength(0);
  });

  // ── Unmount / entity change ────────────────────────────────────

  it('should close WebSocket on unmount', () => {
    const { unmount } = renderHook(
      () => useLiveUpdates({ entityName: 'test', entityId: 1, role: 'viewer' }),
      { wrapper: TestWrapper },
    );

    const ws = MockWebSocket.instances[0];
    act(() => ws.simulateOpen());

    unmount();

    expect(ws.closeCalled).toBe(true);
  });

  it('should flush pending broadcast on unmount instead of losing it', () => {
    const { result, unmount } = renderHook(
      () => useLiveUpdates({ entityName: 'test', entityId: 1, role: 'editor' }),
      { wrapper: TestWrapper },
    );

    const ws = MockWebSocket.instances[0];
    act(() => ws.simulateOpen());

    // Queue a debounced broadcast
    act(() => result.current.broadcastUpdate({ final: true }));

    // Nothing sent yet (300ms debounce pending)
    expect(ws.sentMessages).toHaveLength(0);

    // Unmount — should flush the pending message before closing
    unmount();

    expect(ws.sentMessages).toHaveLength(1);
    expect(JSON.parse(ws.sentMessages[0])).toEqual({ final: true });
  });

  it('should reconnect when entityId changes', () => {
    const { rerender } = renderHook(
      ({ id }) => useLiveUpdates({ entityName: 'test', entityId: id, role: 'viewer' }),
      { wrapper: TestWrapper, initialProps: { id: 1 as number | undefined } },
    );

    expect(MockWebSocket.instances).toHaveLength(1);

    rerender({ id: 2 });

    // First WS should be closed, second one created
    expect(MockWebSocket.instances).toHaveLength(2);
    expect(MockWebSocket.instances[0].closeCalled).toBe(true);
    expect(MockWebSocket.instances[1].url).toContain('/live/test/2');
  });

  it('should not connect for negative entityId', () => {
    renderHook(
      () => useLiveUpdates({ entityName: 'test', entityId: -1, role: 'viewer' }),
      { wrapper: TestWrapper },
    );

    expect(MockWebSocket.instances).toHaveLength(0);
  });

  it('should not connect for NaN entityId', () => {
    renderHook(
      () => useLiveUpdates({ entityName: 'test', entityId: NaN, role: 'viewer' }),
      { wrapper: TestWrapper },
    );

    expect(MockWebSocket.instances).toHaveLength(0);
  });

  // ── Auto-reconnect with exponential backoff ────────────────────

  it('should auto-reconnect after unexpected close with 1s initial delay', () => {
    renderHook(
      () => useLiveUpdates({ entityName: 'test', entityId: 1, role: 'viewer' }),
      { wrapper: TestWrapper },
    );

    expect(MockWebSocket.instances).toHaveLength(1);
    const ws1 = MockWebSocket.instances[0];

    // Open then close unexpectedly (server drop)
    act(() => ws1.simulateOpen());
    act(() => ws1.simulateClose());

    // No new WS yet
    expect(MockWebSocket.instances).toHaveLength(1);

    // Advance past base delay (1000ms)
    act(() => vi.advanceTimersByTime(1100));

    // New WS should be created
    expect(MockWebSocket.instances).toHaveLength(2);
    expect(MockWebSocket.instances[1].url).toContain('/live/test/1');
  });

  it('should apply exponential backoff on consecutive failures', () => {
    renderHook(
      () => useLiveUpdates({ entityName: 'test', entityId: 1, role: 'viewer' }),
      { wrapper: TestWrapper },
    );

    const ws1 = MockWebSocket.instances[0];

    // First failure — never opened (connection refused scenario)
    act(() => {
      ws1.simulateError();
      ws1.simulateClose();
    });

    // Wait 1s (base delay) — should reconnect
    act(() => vi.advanceTimersByTime(1100));
    expect(MockWebSocket.instances).toHaveLength(2);

    // Second failure
    const ws2 = MockWebSocket.instances[1];
    act(() => {
      ws2.simulateError();
      ws2.simulateClose();
    });

    // Wait 1s — should NOT reconnect yet (delay is now 2s)
    act(() => vi.advanceTimersByTime(1100));
    expect(MockWebSocket.instances).toHaveLength(2);

    // Wait another 1s — should reconnect now (total ~2s)
    act(() => vi.advanceTimersByTime(1100));
    expect(MockWebSocket.instances).toHaveLength(3);
  });

  it('should reset backoff counter after successful connection', () => {
    renderHook(
      () => useLiveUpdates({ entityName: 'test', entityId: 1, role: 'viewer' }),
      { wrapper: TestWrapper },
    );

    const ws1 = MockWebSocket.instances[0];

    // First failure
    act(() => { ws1.simulateError(); ws1.simulateClose(); });
    act(() => vi.advanceTimersByTime(1100));
    expect(MockWebSocket.instances).toHaveLength(2);

    // Second attempt succeeds, then drops
    const ws2 = MockWebSocket.instances[1];
    act(() => ws2.simulateOpen()); // ← resets backoff
    act(() => ws2.simulateClose());

    // Backoff should be reset to 1s, not 2s
    act(() => vi.advanceTimersByTime(1100));
    expect(MockWebSocket.instances).toHaveLength(3);
  });

  it('should NOT auto-reconnect after intentional unmount', () => {
    const { unmount } = renderHook(
      () => useLiveUpdates({ entityName: 'test', entityId: 1, role: 'viewer' }),
      { wrapper: TestWrapper },
    );

    act(() => MockWebSocket.instances[0].simulateOpen());

    unmount();

    // Advance time well past any reconnect delay
    act(() => vi.advanceTimersByTime(60_000));

    // Only the original WS should exist (no reconnect attempts)
    expect(MockWebSocket.instances).toHaveLength(1);
  });

  it('should NOT auto-reconnect when disabled changes to false', () => {
    const { rerender } = renderHook(
      ({ enabled }) => useLiveUpdates({ entityName: 'test', entityId: 1, role: 'viewer', enabled }),
      { wrapper: TestWrapper, initialProps: { enabled: true } },
    );

    act(() => MockWebSocket.instances[0].simulateOpen());

    // Disable the hook
    rerender({ enabled: false });

    // Advance time — no reconnections
    act(() => vi.advanceTimersByTime(60_000));

    // Original WS was closed by cleanup, no new ones
    expect(MockWebSocket.instances).toHaveLength(1);
    expect(MockWebSocket.instances[0].closeCalled).toBe(true);
  });

  it('should cancel pending reconnect timer on unmount', () => {
    const { result, unmount } = renderHook(
      () => useLiveUpdates({ entityName: 'test', entityId: 1, role: 'viewer' }),
      { wrapper: TestWrapper },
    );

    const ws1 = MockWebSocket.instances[0];
    act(() => { ws1.simulateOpen(); });
    act(() => { ws1.simulateClose(); });

    // Reconnect is scheduled but we unmount before it fires
    unmount();

    // Advance past the reconnect delay
    act(() => vi.advanceTimersByTime(5_000));

    // Should NOT have created a new WS
    expect(MockWebSocket.instances).toHaveLength(1);
  });

  // ── Multi-editor-as-viewer ─────────────────────────────────────

  it('should connect as viewer when role is viewer (locked-out editor scenario)', () => {
    // When a second editor window opens and the lock fails, it connects as viewer
    const onUpdate = vi.fn();
    renderHook(
      () => useLiveUpdates({ entityName: 'test', entityId: 1, role: 'viewer', onUpdate }),
      { wrapper: TestWrapper },
    );

    expect(MockWebSocket.instances).toHaveLength(1);
    expect(MockWebSocket.instances[0].url).toContain('role=viewer');

    act(() => MockWebSocket.instances[0].simulateOpen());

    // Should receive updates as a viewer
    const data = { title: { rendered: 'Updated' } };
    act(() => MockWebSocket.instances[0].simulateMessage(data));
    expect(onUpdate).toHaveBeenCalledWith(data);
  });

  // ── Status transitions during reconnect ────────────────────────

  it('should transition connecting → connected → disconnected → connecting on reconnect', () => {
    const { result } = renderHook(
      () => useLiveUpdates({ entityName: 'test', entityId: 1, role: 'viewer' }),
      { wrapper: TestWrapper },
    );

    // Initial: connecting
    expect(result.current.status).toBe('connecting');

    act(() => MockWebSocket.instances[0].simulateOpen());
    expect(result.current.status).toBe('connected');

    act(() => MockWebSocket.instances[0].simulateClose());
    expect(result.current.status).toBe('disconnected');

    // Advance past reconnect delay
    act(() => vi.advanceTimersByTime(1100));

    // New WS created, status should be connecting again
    expect(MockWebSocket.instances).toHaveLength(2);
    expect(result.current.status).toBe('connecting');

    // And it can connect again
    act(() => MockWebSocket.instances[1].simulateOpen());
    expect(result.current.status).toBe('connected');
  });

  // ── Broadcast not sent when disconnected ───────────────────────

  it('should silently drop broadcasts when WebSocket is not open', () => {
    const { result } = renderHook(
      () => useLiveUpdates({ entityName: 'test', entityId: 1, role: 'editor' }),
      { wrapper: TestWrapper },
    );

    // WS is in CONNECTING state, not OPEN
    act(() => {
      result.current.broadcastUpdate({ v: 1 });
    });
    act(() => vi.advanceTimersByTime(350));

    expect(MockWebSocket.instances[0].sentMessages).toHaveLength(0);
  });

  // ── Backoff reset on entity change ─────────────────────────────

  it('should reset backoff counter when entityId changes', () => {
    // Scenario: entity A had multiple failures → backoff is high.
    // User navigates to entity B → backoff should start fresh at 1s.
    const { rerender } = renderHook(
      ({ id }) => useLiveUpdates({ entityName: 'test', entityId: id, role: 'viewer' }),
      { wrapper: TestWrapper, initialProps: { id: 1 as number | undefined } },
    );

    const ws1 = MockWebSocket.instances[0];

    // 3 consecutive failures for entity 1 → backoff escalates to 4s
    act(() => { ws1.simulateError(); ws1.simulateClose(); });
    act(() => vi.advanceTimersByTime(1100)); // 1s → reconnect
    act(() => { MockWebSocket.instances[1].simulateError(); MockWebSocket.instances[1].simulateClose(); });
    act(() => vi.advanceTimersByTime(2100)); // 2s → reconnect
    act(() => { MockWebSocket.instances[2].simulateError(); MockWebSocket.instances[2].simulateClose(); });
    // Now backoff would be 4s for the NEXT attempt

    // Switch to entity 2 — triggers effect cleanup + re-run
    rerender({ id: 2 });

    const entityBWs = MockWebSocket.instances[MockWebSocket.instances.length - 1];
    expect(entityBWs.url).toContain('/live/test/2');

    // Simulate failure for entity B
    act(() => { entityBWs.simulateError(); entityBWs.simulateClose(); });

    // Should reconnect after 1s (base delay), NOT 4s — backoff was reset
    act(() => vi.advanceTimersByTime(1100));
    const lastWs = MockWebSocket.instances[MockWebSocket.instances.length - 1];
    expect(lastWs.url).toContain('/live/test/2');
    expect(lastWs).not.toBe(entityBWs);
  });

  it('should reset backoff counter when role changes', () => {
    const { rerender } = renderHook(
      ({ role }: { role: 'editor' | 'viewer' }) =>
        useLiveUpdates({ entityName: 'test', entityId: 1, role }),
      { wrapper: TestWrapper, initialProps: { role: 'viewer' as 'editor' | 'viewer' } },
    );

    // Failure as viewer
    act(() => { MockWebSocket.instances[0].simulateError(); MockWebSocket.instances[0].simulateClose(); });
    act(() => vi.advanceTimersByTime(1100));

    // Second failure as viewer → backoff now 2s
    act(() => { MockWebSocket.instances[1].simulateError(); MockWebSocket.instances[1].simulateClose(); });

    // Switch role to editor (simulates lock acquired)
    rerender({ role: 'editor' });

    const editorWs = MockWebSocket.instances[MockWebSocket.instances.length - 1];
    expect(editorWs.url).toContain('role=editor');

    // Failure as editor
    act(() => { editorWs.simulateError(); editorWs.simulateClose(); });

    // Should reconnect after 1s (reset), not 2s
    act(() => vi.advanceTimersByTime(1100));
    expect(MockWebSocket.instances[MockWebSocket.instances.length - 1]).not.toBe(editorWs);
  });

  // ── RF Sheets specific ─────────────────────────────────────────

  it('should connect with correct URL for rf-sheets entity', () => {
    renderHook(
      () => useLiveUpdates({ entityName: 'rf-sheets', entityId: 99, role: 'viewer' }),
      { wrapper: TestWrapper },
    );

    expect(MockWebSocket.instances).toHaveLength(1);
    expect(MockWebSocket.instances[0].url).toBe(
      'ws://localhost:9000/rf/api/live/rf-sheets/99?role=viewer',
    );
  });

  it('should connect as editor for rf-sheets when in design mode', () => {
    renderHook(
      () => useLiveUpdates({ entityName: 'rf-sheets', entityId: 10, role: 'editor' }),
      { wrapper: TestWrapper },
    );

    expect(MockWebSocket.instances).toHaveLength(1);
    expect(MockWebSocket.instances[0].url).toBe(
      'ws://localhost:9000/rf/api/live/rf-sheets/10?role=editor',
    );
  });

  it('should receive workbook_data and title in viewer onUpdate', async () => {
    const onUpdate = vi.fn();
    renderHook(
      () => useLiveUpdates({ entityName: 'rf-sheets', entityId: 10, role: 'viewer', onUpdate }),
      { wrapper: TestWrapper },
    );

    act(() => MockWebSocket.instances[0].simulateOpen());

    const payload = { workbook_data: '{"sheets":{}}', title: 'Test Sheet', sources: ['product'] };
    act(() => MockWebSocket.instances[0].simulateMessage(payload));

    expect(onUpdate).toHaveBeenCalledWith(payload);
  });

  it('should broadcast sheet snapshot with sources as editor', async () => {
    const { result } = renderHook(
      () => useLiveUpdates({ entityName: 'rf-sheets', entityId: 10, role: 'editor' }),
      { wrapper: TestWrapper },
    );

    act(() => MockWebSocket.instances[0].simulateOpen());

    const payload = {
      workbook_data: '{"sheets":{}}',
      title: 'My Sheet',
      sources: ['product', 'objective'],
    };
    act(() => result.current.broadcastUpdate(payload));
    act(() => vi.advanceTimersByTime(350));

    const sent = JSON.parse(MockWebSocket.instances[0].sentMessages[0]);
    expect(sent.workbook_data).toBe('{"sheets":{}}');
    expect(sent.title).toBe('My Sheet');
    expect(sent.sources).toEqual(['product', 'objective']);
  });

  it('should switch from editor to viewer for rf-sheets on role change', () => {
    const { rerender } = renderHook(
      ({ role }: { role: 'editor' | 'viewer' }) =>
        useLiveUpdates({ entityName: 'rf-sheets', entityId: 10, role }),
      { wrapper: TestWrapper, initialProps: { role: 'editor' as 'editor' | 'viewer' } },
    );

    expect(MockWebSocket.instances[0].url).toContain('role=editor');

    rerender({ role: 'viewer' });

    const lastWs = MockWebSocket.instances[MockWebSocket.instances.length - 1];
    expect(lastWs.url).toContain('role=viewer');
    expect(lastWs.url).toContain('rf-sheets');
  });
});
