import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, waitFor, act } from '@testing-library/react';
import { createElement } from 'react';
import { useEntityLock } from '../../hooks/useEntityLock';
import * as client from '../../api/client';

// Mock the API client
vi.mock('../../api/client');

// Mock sonner toast
vi.mock('sonner', () => ({
  toast: {
    info: vi.fn(),
    success: vi.fn(),
    error: vi.fn(),
    warning: vi.fn(),
  },
}));

// Mock sessionStorage for tab_id isolation
const sessionStorageMock = (() => {
  let store: Record<string, string> = {};
  return {
    getItem: (key: string) => store[key] ?? null,
    setItem: (key: string, value: string) => { store[key] = value; },
    removeItem: (key: string) => { delete store[key]; },
    clear: () => { store = {}; },
  };
})();

Object.defineProperty(globalThis, 'sessionStorage', { value: sessionStorageMock, writable: true });

// Mock crypto.randomUUID
let uuidCounter = 0;
Object.defineProperty(globalThis, 'crypto', {
  value: { randomUUID: () => `mock-uuid-${++uuidCounter}` },
  writable: true,
});

// Simple wrapper - no need for QueryClient since useEntityLock doesn't use React Query
function TestWrapper({ children }: { children: React.ReactNode }) {
  return createElement('div', null, children);
}

describe('useEntityLock', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.useFakeTimers({ shouldAdvanceTime: true });
    sessionStorageMock.clear();
    uuidCounter = 0;
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.resetAllMocks();
  });

  it('should not acquire lock when disabled', async () => {
    const { result } = renderHook(
      () => useEntityLock('TestEntity', 1, { enabled: false }),
      { wrapper: TestWrapper }
    );

    expect(result.current.lockStatus).toBe('idle');
    expect(client.tryLockEntity).not.toHaveBeenCalled();
  });

  it('should not acquire lock when entityId is undefined', async () => {
    const { result } = renderHook(
      () => useEntityLock('TestEntity', undefined, { enabled: true }),
      { wrapper: TestWrapper }
    );

    expect(result.current.lockStatus).toBe('idle');
    expect(client.tryLockEntity).not.toHaveBeenCalled();
  });

  it('should acquire lock successfully', async () => {
    vi.mocked(client.tryLockEntity).mockResolvedValue({});

    const { result } = renderHook(
      () => useEntityLock('TestEntity', 1, { enabled: true }),
      { wrapper: TestWrapper }
    );

    await waitFor(() => expect(result.current.lockStatus).toBe('locked'));

    // Should pass tab_id as third argument
    expect(client.tryLockEntity).toHaveBeenCalledWith('TestEntity', 1, expect.any(String));
  });

  it('should handle lock failure', async () => {
    vi.mocked(client.tryLockEntity).mockResolvedValue({
      error: 'Entity is locked by another user',
    });

    const { result } = renderHook(
      () => useEntityLock('TestEntity', 1, { enabled: true }),
      { wrapper: TestWrapper }
    );

    await waitFor(() => expect(result.current.lockStatus).toBe('failed'));
  });

  it('should release lock on unmount', async () => {
    vi.mocked(client.tryLockEntity).mockResolvedValue({});
    vi.mocked(client.unlockEntity).mockResolvedValue({});

    const { result, unmount } = renderHook(
      () => useEntityLock('TestEntity', 1, { enabled: true }),
      { wrapper: TestWrapper }
    );

    await waitFor(() => expect(result.current.lockStatus).toBe('locked'));

    unmount();

    // Should pass tab_id as third argument
    expect(client.unlockEntity).toHaveBeenCalledWith('TestEntity', 1, expect.any(String));
  });

  it('should refresh lock periodically', async () => {
    vi.mocked(client.tryLockEntity).mockResolvedValue({});

    renderHook(
      () => useEntityLock('TestEntity', 1, { enabled: true }),
      { wrapper: TestWrapper }
    );

    // First call on mount
    await waitFor(() => {
      expect(client.tryLockEntity).toHaveBeenCalledTimes(1);
    });

    // Advance timers to trigger heartbeat check (every 15 seconds)
    await act(async () => {
      vi.advanceTimersByTime(15000);
    });

    // Should have heartbeat-refreshed (initial + 1 heartbeat)
    expect(client.tryLockEntity).toHaveBeenCalledTimes(2);
  });

  // ── Per-tab lock isolation ──────────────────────────────────────

  it('should pass the same tab_id across lock and unlock calls', async () => {
    vi.mocked(client.tryLockEntity).mockResolvedValue({});
    vi.mocked(client.unlockEntity).mockResolvedValue({});

    const { unmount } = renderHook(
      () => useEntityLock('TestEntity', 1, { enabled: true }),
      { wrapper: TestWrapper }
    );

    await waitFor(() => {
      expect(client.tryLockEntity).toHaveBeenCalledTimes(1);
    });

    const lockTabId = vi.mocked(client.tryLockEntity).mock.calls[0][2];
    expect(lockTabId).toBeTruthy();

    unmount();

    const unlockTabId = vi.mocked(client.unlockEntity).mock.calls[0][2];
    expect(unlockTabId).toBe(lockTabId);
  });

  it('should pass tab_id on heartbeat refresh too', async () => {
    vi.mocked(client.tryLockEntity).mockResolvedValue({});

    renderHook(
      () => useEntityLock('TestEntity', 1, { enabled: true }),
      { wrapper: TestWrapper }
    );

    await waitFor(() => {
      expect(client.tryLockEntity).toHaveBeenCalledTimes(1);
    });

    const initialTabId = vi.mocked(client.tryLockEntity).mock.calls[0][2];

    // Advance timers to trigger heartbeat
    await act(async () => {
      vi.advanceTimersByTime(15000);
    });

    const heartbeatTabId = vi.mocked(client.tryLockEntity).mock.calls[1][2];
    expect(heartbeatTabId).toBe(initialTabId);
  });

  it('should fail lock when same user opens second tab', async () => {
    // First call succeeds, then second tab's request fails with lock conflict
    vi.mocked(client.tryLockEntity)
      .mockResolvedValueOnce({}) // first tab
      .mockResolvedValueOnce({ error: 'Lock is held by you in another tab/window.' });

    const { result: result1 } = renderHook(
      () => useEntityLock('TestEntity', 1, { enabled: true }),
      { wrapper: TestWrapper }
    );

    await waitFor(() => expect(result1.current.lockStatus).toBe('locked'));

    // Simulate second tab with different sessionStorage
    sessionStorageMock.clear(); // forces new tab_id

    const { result: result2 } = renderHook(
      () => useEntityLock('TestEntity', 1, { enabled: true }),
      { wrapper: TestWrapper }
    );

    await waitFor(() => expect(result2.current.lockStatus).toBe('failed'));

    // The two tab_ids should be different
    const tab1Id = vi.mocked(client.tryLockEntity).mock.calls[0][2];
    const tab2Id = vi.mocked(client.tryLockEntity).mock.calls[1][2];
    expect(tab1Id).not.toBe(tab2Id);
  });

  it('should reuse tab_id from sessionStorage across hook re-renders (page refresh)', async () => {
    // Pre-set a tab_id in sessionStorage to simulate F5 refresh persistence
    sessionStorageMock.setItem('__rf_tab_id', 'persistent-id');
    vi.mocked(client.tryLockEntity).mockResolvedValue({});

    renderHook(
      () => useEntityLock('TestEntity', 1, { enabled: true }),
      { wrapper: TestWrapper }
    );

    await waitFor(() => {
      expect(client.tryLockEntity).toHaveBeenCalledTimes(1);
    });

    expect(vi.mocked(client.tryLockEntity).mock.calls[0][2]).toBe('persistent-id');
  });

  it('should handle inactivity timeout and release lock with correct tab_id', async () => {
    vi.mocked(client.tryLockEntity).mockResolvedValue({});
    vi.mocked(client.unlockEntity).mockResolvedValue({});

    const onLockLost = vi.fn();
    renderHook(
      () => useEntityLock('TestEntity', 1, { enabled: true, onLockLost }),
      { wrapper: TestWrapper }
    );

    await waitFor(() => {
      expect(client.tryLockEntity).toHaveBeenCalledTimes(1);
    });

    const tabId = vi.mocked(client.tryLockEntity).mock.calls[0][2];

    // Advance past inactivity timeout (60s) + heartbeat interval (15s)
    await act(async () => {
      vi.advanceTimersByTime(75000);
    });

    // Lock should have been released due to inactivity
    expect(client.unlockEntity).toHaveBeenCalledWith('TestEntity', 1, tabId);
    expect(onLockLost).toHaveBeenCalled();
  });
});
