import { X } from 'lucide-react';
import type { AutoSaveStatus } from '../../hooks/useAutoSave';

interface AutoSaveIndicatorProps {
  status: AutoSaveStatus;
  countdownRemaining: number;
  countdownTotal: number;
  validationErrors: string[];
  error: string | null;
  onDismissValidation: () => void;
}

export function AutoSaveIndicator({
  status,
  countdownRemaining,
  countdownTotal,
  validationErrors,
  error,
  onDismissValidation,
}: AutoSaveIndicatorProps) {
  if (status === 'idle' || status === 'waiting') return null;

  const progress = countdownTotal > 0 ? ((countdownTotal - countdownRemaining) / countdownTotal) * 100 : 0;
  const secondsLeft = Math.ceil(countdownRemaining / 1000);

  return (
    <div data-testid="autosave-indicator" className="fixed top-20 right-4 z-50 max-w-sm rounded-lg overflow-hidden shadow-lg border">
      {status === 'checking' && (
        <div className="bg-blue-50 border-blue-200 px-4 py-3 text-sm text-blue-700" data-testid="autosave-checking">
          Validating...
        </div>
      )}

      {status === 'validation-error' && (
        <div className="bg-amber-50 border-amber-200 px-4 py-3" data-testid="autosave-validation-error">
          <div className="flex items-start justify-between gap-2">
            <div className="min-w-0 flex-1">
              <p className="text-sm font-medium text-amber-800">Please fix the following before saving:</p>
              <ul className="mt-1 text-sm text-amber-700 list-disc list-inside break-words">
                {validationErrors.map((err, i) => (
                  <li key={i}>{err}</li>
                ))}
              </ul>
            </div>
            <button
              onClick={onDismissValidation}
              className="ml-1 flex-shrink-0 text-amber-400 hover:text-amber-600"
              data-testid="autosave-dismiss"
              aria-label="Dismiss"
            >
              <X className="w-4 h-4" />
            </button>
          </div>
        </div>
      )}

      {status === 'countdown' && (
        <div className="bg-blue-50 border-blue-200" data-testid="autosave-countdown">
          <div className="px-4 py-3 text-sm text-blue-700">
            Saving in {secondsLeft}s...
          </div>
          <div className="h-1 bg-blue-100">
            <div
              className="h-full bg-blue-500 transition-all duration-100 ease-linear"
              style={{ width: `${progress}%` }}
              data-testid="autosave-progress"
            />
          </div>
        </div>
      )}

      {status === 'saving' && (
        <div className="bg-blue-50 border-blue-200 px-4 py-3 text-sm text-blue-700" data-testid="autosave-saving">
          Saving...
        </div>
      )}

      {status === 'saved' && (
        <div className="bg-green-50 border-green-200 px-4 py-3 text-sm text-green-700" data-testid="autosave-saved">
          Saved!
        </div>
      )}

      {status === 'error' && (
        <div className="bg-red-50 border-red-200 px-4 py-3 text-sm text-red-700" data-testid="autosave-error">
          {error || 'Save failed'}
        </div>
      )}
    </div>
  );
}
