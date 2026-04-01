import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import { useAutoSave } from '../../hooks/useAutoSave';

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

  it('transitions to checking when triggerAutoSave is called', async () => {
    let resolveSanity!: (v: { passed: boolean }) => void;
    const sanityPromise = new Promise<{ passed: boolean }>(r => { resolveSanity = r; });

    const { result } = renderHook(() =>
      useAutoSave({
        onSanityCheck: () => sanityPromise,
        onSave: vi.fn(),
      })
    );

    act(() => { result.current.triggerAutoSave(); });
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
      useAutoSave({ onSanityCheck, onSave: vi.fn() })
    );

    await act(async () => { result.current.triggerAutoSave(); });
    expect(result.current.status).toBe('validation-error');
    expect(result.current.validationErrors).toEqual(['Title is required']);
  });

  it('starts countdown after sanity check passes', async () => {
    const onSanityCheck = vi.fn().mockResolvedValue({ passed: true });
    const onSave = vi.fn().mockResolvedValue(undefined);

    const { result } = renderHook(() =>
      useAutoSave({ onSanityCheck, onSave, countdownDuration: 3000 })
    );

    await act(async () => { result.current.triggerAutoSave(); });
    expect(result.current.status).toBe('countdown');
    expect(result.current.countdownRemaining).toBeGreaterThan(0);
  });

  it('saves after countdown completes', async () => {
    const onSanityCheck = vi.fn().mockResolvedValue({ passed: true });
    const onSave = vi.fn().mockResolvedValue(undefined);

    const { result } = renderHook(() =>
      useAutoSave({ onSanityCheck, onSave, countdownDuration: 1000 })
    );

    await act(async () => { result.current.triggerAutoSave(); });
    expect(result.current.status).toBe('countdown');

    // Advance past countdown
    await act(async () => { vi.advanceTimersByTime(1100); });
    expect(onSave).toHaveBeenCalledOnce();
    expect(result.current.status).toBe('saved');
  });

  it('transitions to idle after saved dismisses', async () => {
    const onSanityCheck = vi.fn().mockResolvedValue({ passed: true });
    const onSave = vi.fn().mockResolvedValue(undefined);

    const { result } = renderHook(() =>
      useAutoSave({ onSanityCheck, onSave, countdownDuration: 500 })
    );

    await act(async () => { result.current.triggerAutoSave(); });
    await act(async () => { vi.advanceTimersByTime(600); });
    expect(result.current.status).toBe('saved');

    // Auto-dismiss after 2s
    act(() => { vi.advanceTimersByTime(2100); });
    expect(result.current.status).toBe('idle');
  });

  it('shows error when save fails', async () => {
    const onSanityCheck = vi.fn().mockResolvedValue({ passed: true });
    const onSave = vi.fn().mockRejectedValue(new Error('Network error'));

    const { result } = renderHook(() =>
      useAutoSave({ onSanityCheck, onSave, countdownDuration: 500 })
    );

    await act(async () => { result.current.triggerAutoSave(); });
    await act(async () => { vi.advanceTimersByTime(600); });
    expect(result.current.status).toBe('error');
    expect(result.current.error).toBe('Network error');
  });

  it('cancel stops the countdown', async () => {
    const onSanityCheck = vi.fn().mockResolvedValue({ passed: true });
    const onSave = vi.fn();

    const { result } = renderHook(() =>
      useAutoSave({ onSanityCheck, onSave, countdownDuration: 3000 })
    );

    await act(async () => { result.current.triggerAutoSave(); });
    expect(result.current.status).toBe('countdown');

    act(() => { result.current.cancel(); });
    expect(result.current.status).toBe('idle');

    await act(async () => { vi.advanceTimersByTime(5000); });
    expect(onSave).not.toHaveBeenCalled();
  });

  it('saveNow saves immediately without countdown', async () => {
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
      useAutoSave({ onSanityCheck, onSave: vi.fn() })
    );

    await act(async () => { result.current.triggerAutoSave(); });
    expect(result.current.status).toBe('validation-error');

    act(() => { result.current.dismissValidation(); });
    expect(result.current.status).toBe('idle');
  });

  it('does not trigger when disabled', async () => {
    const onSanityCheck = vi.fn();

    const { result } = renderHook(() =>
      useAutoSave({ onSanityCheck, onSave: vi.fn(), enabled: false })
    );

    await act(async () => { result.current.triggerAutoSave(); });
    expect(onSanityCheck).not.toHaveBeenCalled();
    expect(result.current.status).toBe('idle');
  });

  it('falls back to countdown when sanity check throws', async () => {
    const onSanityCheck = vi.fn().mockRejectedValue(new Error('Network'));
    const onSave = vi.fn().mockResolvedValue(undefined);

    const { result } = renderHook(() =>
      useAutoSave({ onSanityCheck, onSave, countdownDuration: 500 })
    );

    await act(async () => { result.current.triggerAutoSave(); });
    expect(result.current.status).toBe('countdown');
  });

  it('resets countdown when triggerAutoSave is called during countdown', async () => {
    const onSanityCheck = vi.fn().mockResolvedValue({ passed: true });
    const onSave = vi.fn().mockResolvedValue(undefined);

    const { result } = renderHook(() =>
      useAutoSave({ onSanityCheck, onSave, countdownDuration: 3000 })
    );

    // Start first countdown
    await act(async () => { result.current.triggerAutoSave(); });
    expect(result.current.status).toBe('countdown');
    expect(onSanityCheck).toHaveBeenCalledTimes(1);

    // Advance 2s into the 3s countdown
    act(() => { vi.advanceTimersByTime(2000); });
    expect(result.current.status).toBe('countdown');
    expect(result.current.countdownRemaining).toBeLessThanOrEqual(1000);

    // Trigger again mid-countdown — should restart without re-running sanity check
    await act(async () => { result.current.triggerAutoSave(); });
    expect(result.current.status).toBe('countdown');
    expect(result.current.countdownRemaining).toBe(3000);
    // Sanity check should NOT run again (skipped during countdown)
    expect(onSanityCheck).toHaveBeenCalledTimes(1);

    // Original 3s would have already elapsed — should NOT have saved since we reset
    act(() => { vi.advanceTimersByTime(1100); });
    expect(onSave).not.toHaveBeenCalled();

    // Complete the reset countdown
    await act(async () => { vi.advanceTimersByTime(2000); });
    expect(onSave).toHaveBeenCalledOnce();
    expect(result.current.status).toBe('saved');
  });

  it('does not interrupt saving when triggerAutoSave is called', async () => {
    let resolveSave!: () => void;
    const savePromise = new Promise<void>(r => { resolveSave = r; });
    const onSanityCheck = vi.fn().mockResolvedValue({ passed: true });
    const onSave = vi.fn().mockReturnValue(savePromise);

    const { result } = renderHook(() =>
      useAutoSave({ onSanityCheck, onSave, countdownDuration: 500 })
    );

    // Start save
    await act(async () => { result.current.triggerAutoSave(); });
    await act(async () => { vi.advanceTimersByTime(600); });
    expect(result.current.status).toBe('saving');

    // Trigger again while saving — should be ignored
    await act(async () => { result.current.triggerAutoSave(); });
    expect(result.current.status).toBe('saving');
    expect(onSanityCheck).toHaveBeenCalledTimes(1);

    // Finish save
    await act(async () => { resolveSave(); });
    expect(result.current.status).toBe('saved');
  });
});
