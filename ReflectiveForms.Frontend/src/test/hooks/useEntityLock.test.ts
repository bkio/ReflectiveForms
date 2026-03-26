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

// Simple wrapper - no need for QueryClient since useEntityLock doesn't use React Query
function TestWrapper({ children }: { children: React.ReactNode }) {
  return createElement('div', null, children);
}

describe('useEntityLock', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.useFakeTimers();
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

    expect(client.tryLockEntity).toHaveBeenCalledWith('TestEntity', 1);
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

    expect(client.unlockEntity).toHaveBeenCalledWith('TestEntity', 1);
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

    // Advance timers to trigger refresh (default is 30 seconds)
    await act(async () => {
      vi.advanceTimersByTime(30000);
    });

    // Should have refreshed
    expect(client.tryLockEntity).toHaveBeenCalledTimes(2);
  });
});
