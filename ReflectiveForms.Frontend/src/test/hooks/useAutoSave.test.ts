import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import { useAutoSave } from '../../hooks/useAutoSave';

const WAIT = 500; // short waitDuration for tests

/** Advance fake timers AND flush the microtask queue so resolved promises settle. */
async function advanceAndFlush(ms: number) {
  // 1. Advance timers synchronously — fires setTimeout/setInterval callbacks
  act(() => { vi.advanceTimersByTime(ms); });
  // 2. Flush microtask queue — resolves pending promises from timer callbacks
  //    (e.g. startSanityAndCountdown → await onSanityCheck → setState)
  await act(async () => {});
}

describe('useAutoSave', () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('starts in idle state', () => {
    const { result } = renderHook(() =>
      useAutoSave({
        onSanityCheck: vi.fn().mockResolvedValue({ passed: true }),
        onSave: vi.fn(),
      })
    );
    expect(result.current.status).toBe('idle');
  });

  it('transitions to waiting then checking after wait period', async () => {
    let resolveSanity!: (v: { passed: boolean }) => void;
    const sanityPromise = new Promise<{ passed: boolean }>(r => { resolveSanity = r; });

    const { result } = renderHook(() =>
      useAutoSave({
        onSanityCheck: () => sanityPromise,
        onSave: vi.fn(),
        waitDuration: WAIT,
      })
    );

    act(() => { result.current.triggerAutoSave(); });
    expect(result.current.status).toBe('waiting');

    // Advance past wait period
    await advanceAndFlush(WAIT);
    expect(result.current.status).toBe('checking');

    await act(async () => { resolveSanity({ passed: true }); });
    expect(result.current.status).toBe('countdown');
  });

  it('shows validation-error when sanity check fails', async () => {
    const onSanityCheck = vi.fn().mockResolvedValue({
      passed: false,
      errors: ['Title is required'],
    });

    const { result } = renderHook(() =>
      useAutoSave({ onSanityCheck, onSave: vi.fn(), waitDuration: WAIT })
    );

    act(() => { result.current.triggerAutoSave(); });
    expect(result.current.status).toBe('waiting');

    await advanceAndFlush(WAIT);
    expect(result.current.status).toBe('validation-error');
    expect(result.current.validationErrors).toEqual(['Title is required']);
  });

  it('starts countdown after wait and sanity check passes', async () => {
    const onSanityCheck = vi.fn().mockResolvedValue({ passed: true });
    const onSave = vi.fn().mockResolvedValue(undefined);

    const { result } = renderHook(() =>
      useAutoSave({ onSanityCheck, onSave, countdownDuration: 3000, waitDuration: WAIT })
    );

    act(() => { result.current.triggerAutoSave(); });
    await advanceAndFlush(WAIT);
    expect(result.current.status).toBe('countdown');
    expect(result.current.countdownRemaining).toBeGreaterThan(0);
  });

  it('saves after wait and countdown completes', async () => {
    const onSanityCheck = vi.fn().mockResolvedValue({ passed: true });
    const onSave = vi.fn().mockResolvedValue(undefined);

    const { result } = renderHook(() =>
      useAutoSave({ onSanityCheck, onSave, countdownDuration: 1000, waitDuration: WAIT })
    );

    act(() => { result.current.triggerAutoSave(); });
    // Wait period
    await advanceAndFlush(WAIT);
    expect(result.current.status).toBe('countdown');

    // Advance past countdown
    await advanceAndFlush(1100);
    expect(onSave).toHaveBeenCalledOnce();
    expect(result.current.status).toBe('saved');
  });

  it('transitions to idle after saved dismisses', async () => {
    const onSanityCheck = vi.fn().mockResolvedValue({ passed: true });
    const onSave = vi.fn().mockResolvedValue(undefined);

    const { result } = renderHook(() =>
      useAutoSave({ onSanityCheck, onSave, countdownDuration: 500, waitDuration: WAIT })
    );

    act(() => { result.current.triggerAutoSave(); });
    await advanceAndFlush(WAIT);
    await advanceAndFlush(600);
    expect(result.current.status).toBe('saved');

    // Auto-dismiss after 2s
    act(() => { vi.advanceTimersByTime(2100); });
    expect(result.current.status).toBe('idle');
  });

  it('shows error when save fails', async () => {
    const onSanityCheck = vi.fn().mockResolvedValue({ passed: true });
    const onSave = vi.fn().mockRejectedValue(new Error('Network error'));

    const { result } = renderHook(() =>
      useAutoSave({ onSanityCheck, onSave, countdownDuration: 500, waitDuration: WAIT })
    );

    act(() => { result.current.triggerAutoSave(); });
    await advanceAndFlush(WAIT);
    await advanceAndFlush(600);
    expect(result.current.status).toBe('error');
    expect(result.current.error).toBe('Network error');
  });

  it('cancel stops the waiting period', async () => {
    const onSanityCheck = vi.fn();
    const onSave = vi.fn();

    const { result } = renderHook(() =>
      useAutoSave({ onSanityCheck, onSave, countdownDuration: 3000, waitDuration: WAIT })
    );

    act(() => { result.current.triggerAutoSave(); });
    expect(result.current.status).toBe('waiting');

    act(() => { result.current.cancel(); });
    expect(result.current.status).toBe('idle');

    await advanceAndFlush(10000);
    expect(onSanityCheck).not.toHaveBeenCalled();
    expect(onSave).not.toHaveBeenCalled();
  });

  it('cancel stops the countdown', async () => {
    const onSanityCheck = vi.fn().mockResolvedValue({ passed: true });
    const onSave = vi.fn();

    const { result } = renderHook(() =>
      useAutoSave({ onSanityCheck, onSave, countdownDuration: 3000, waitDuration: WAIT })
    );

    act(() => { result.current.triggerAutoSave(); });
    await advanceAndFlush(WAIT);
    expect(result.current.status).toBe('countdown');

    act(() => { result.current.cancel(); });
    expect(result.current.status).toBe('idle');

    await advanceAndFlush(5000);
    expect(onSave).not.toHaveBeenCalled();
  });

  it('saveNow saves immediately without wait or countdown', async () => {
    const onSave = vi.fn().mockResolvedValue(undefined);

    const { result } = renderHook(() =>
      useAutoSave({
        onSanityCheck: vi.fn().mockResolvedValue({ passed: true }),
        onSave,
      })
    );

    await act(async () => { result.current.saveNow(); });
    expect(onSave).toHaveBeenCalledOnce();
    expect(result.current.status).toBe('saved');
  });

  it('dismissValidation resets to idle', async () => {
    const onSanityCheck = vi.fn().mockResolvedValue({
      passed: false,
      errors: ['Error'],
    });

    const { result } = renderHook(() =>
      useAutoSave({ onSanityCheck, onSave: vi.fn(), waitDuration: WAIT })
    );

    act(() => { result.current.triggerAutoSave(); });
    await advanceAndFlush(WAIT);
    expect(result.current.status).toBe('validation-error');

    act(() => { result.current.dismissValidation(); });
    expect(result.current.status).toBe('idle');
  });

  it('does not trigger when disabled', async () => {
    const onSanityCheck = vi.fn();

    const { result } = renderHook(() =>
      useAutoSave({ onSanityCheck, onSave: vi.fn(), enabled: false })
    );

    act(() => { result.current.triggerAutoSave(); });
    expect(onSanityCheck).not.toHaveBeenCalled();
    expect(result.current.status).toBe('idle');
  });

  it('falls back to countdown when sanity check throws', async () => {
    const onSanityCheck = vi.fn().mockRejectedValue(new Error('Network'));
    const onSave = vi.fn().mockResolvedValue(undefined);

    const { result } = renderHook(() =>
      useAutoSave({ onSanityCheck, onSave, countdownDuration: 500, waitDuration: WAIT })
    );

    act(() => { result.current.triggerAutoSave(); });
    await advanceAndFlush(WAIT);
    expect(result.current.status).toBe('countdown');
  });

  it('resets wait timer when triggerAutoSave is called during waiting', async () => {
    const onSanityCheck = vi.fn().mockResolvedValue({ passed: true });
    const onSave = vi.fn().mockResolvedValue(undefined);

    const { result } = renderHook(() =>
      useAutoSave({ onSanityCheck, onSave, countdownDuration: 1000, waitDuration: WAIT })
    );

    act(() => { result.current.triggerAutoSave(); });
    expect(result.current.status).toBe('waiting');

    // Advance partway through wait
    act(() => { vi.advanceTimersByTime(300); });
    expect(result.current.status).toBe('waiting');

    // Trigger again — restarts the wait timer
    act(() => { result.current.triggerAutoSave(); });
    expect(result.current.status).toBe('waiting');

    // Original wait would have elapsed here, but the reset means we're still waiting
    act(() => { vi.advanceTimersByTime(300); });
    expect(result.current.status).toBe('waiting');
    expect(onSanityCheck).not.toHaveBeenCalled();

    // Complete the reset wait
    await advanceAndFlush(200);
    expect(result.current.status).toBe('countdown');
    expect(onSanityCheck).toHaveBeenCalledTimes(1);
  });

  it('resets to waiting when triggerAutoSave is called during countdown', async () => {
    const onSanityCheck = vi.fn().mockResolvedValue({ passed: true });
    const onSave = vi.fn().mockResolvedValue(undefined);

    const { result } = renderHook(() =>
      useAutoSave({ onSanityCheck, onSave, countdownDuration: 3000, waitDuration: WAIT })
    );

    // Start → waiting → checking → countdown
    act(() => { result.current.triggerAutoSave(); });
    await advanceAndFlush(WAIT);
    expect(result.current.status).toBe('countdown');
    expect(onSanityCheck).toHaveBeenCalledTimes(1);

    // Advance 2s into the 3s countdown
    act(() => { vi.advanceTimersByTime(2000); });
    expect(result.current.status).toBe('countdown');

    // Trigger again mid-countdown — should reset to waiting
    act(() => { result.current.triggerAutoSave(); });
    expect(result.current.status).toBe('waiting');

    // Wait period expires → sanity check runs again → countdown
    await advanceAndFlush(WAIT);
    expect(result.current.status).toBe('countdown');
    expect(onSanityCheck).toHaveBeenCalledTimes(2);

    // Complete the countdown
    await advanceAndFlush(3100);
    expect(onSave).toHaveBeenCalledOnce();
    expect(result.current.status).toBe('saved');
  });

  it('does not interrupt saving when triggerAutoSave is called', async () => {
    let resolveSave!: () => void;
    const savePromise = new Promise<void>(r => { resolveSave = r; });
    const onSanityCheck = vi.fn().mockResolvedValue({ passed: true });
    const onSave = vi.fn().mockReturnValue(savePromise);

    const { result } = renderHook(() =>
      useAutoSave({ onSanityCheck, onSave, countdownDuration: 500, waitDuration: WAIT })
    );

    // Start → waiting → checking → countdown → saving
    act(() => { result.current.triggerAutoSave(); });
    await advanceAndFlush(WAIT);
    await advanceAndFlush(600);
    expect(result.current.status).toBe('saving');

    // Trigger again while saving — should be ignored
    act(() => { result.current.triggerAutoSave(); });
    expect(result.current.status).toBe('saving');
    expect(onSanityCheck).toHaveBeenCalledTimes(1);

    // Finish save
    await act(async () => { resolveSave(); });
    expect(result.current.status).toBe('saved');
  });

  it('uses default waitDuration of 5000ms', async () => {
    const onSanityCheck = vi.fn().mockResolvedValue({ passed: true });

    const { result } = renderHook(() =>
      useAutoSave({ onSanityCheck, onSave: vi.fn() })
    );

    act(() => { result.current.triggerAutoSave(); });
    expect(result.current.status).toBe('waiting');

    // Still waiting after 4.9s
    act(() => { vi.advanceTimersByTime(4900); });
    expect(result.current.status).toBe('waiting');
    expect(onSanityCheck).not.toHaveBeenCalled();

    // Transitions after 5s
    await advanceAndFlush(200);
    expect(onSanityCheck).toHaveBeenCalledTimes(1);
  });
});
