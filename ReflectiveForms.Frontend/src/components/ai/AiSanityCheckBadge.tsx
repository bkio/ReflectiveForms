import { useState, useCallback } from 'react';
import { AlertTriangle, AlertCircle, Loader2, Shield } from 'lucide-react';
import { useAiSanityCheck } from '../../hooks/useAi';
import type { AiSanityCheckSchema } from '../../types/schema';

interface AiSanityCheckBadgeProps {
  entityName: string;
  fieldName: string;
  fieldValue: unknown;
  checks: AiSanityCheckSchema[];
}

export function AiSanityCheckBadge({
  entityName,
  fieldName,
  fieldValue,
  checks,
}: AiSanityCheckBadgeProps) {
  const sanityMutation = useAiSanityCheck();
  const [results, setResults] = useState<
    Array<{ passed: boolean; message?: string; severity: 'Warning' | 'Error' }>
  >([]);

  const handleCheck = useCallback(async () => {
    try {
      const data = await sanityMutation.mutateAsync({
        entityName,
        fieldName,
        fieldValue,
      });
      setResults(data);
    } catch {
      // Error available via sanityMutation.error
    }
  }, [entityName, fieldName, fieldValue, sanityMutation]);

  if (checks.length === 0) return null;

  const failedResults = results.filter((r) => !r.passed);

  return (
    <div className="inline-flex flex-col gap-1" data-testid={`ai-sanity-${fieldName}`}>
      <button
        type="button"
        onClick={handleCheck}
        disabled={sanityMutation.isPending}
        className="inline-flex items-center gap-1 px-2 py-1 text-xs text-gray-500 hover:text-gray-700 hover:bg-gray-50 dark:hover:bg-gray-700 rounded transition-colors disabled:opacity-50"
        title="Run AI sanity check"
        data-testid={`ai-sanity-check-${fieldName}`}
      >
        {sanityMutation.isPending ? (
          <Loader2 className="w-3.5 h-3.5 animate-spin" />
        ) : (
          <Shield className="w-3.5 h-3.5" />
        )}
        Check
      </button>

      {failedResults.length > 0 && (
        <div className="space-y-1">
          {failedResults.map((r, i) => (
            <div
              key={i}
              className={`flex items-start gap-1.5 px-2 py-1 text-xs rounded ${
                r.severity === 'Error'
                  ? 'bg-red-50 text-red-700 dark:bg-red-900/20 dark:text-red-300'
                  : 'bg-amber-50 text-amber-700 dark:bg-amber-900/20 dark:text-amber-300'
              }`}
              data-testid={`ai-sanity-result-${fieldName}-${i}`}
            >
              {r.severity === 'Error' ? (
                <AlertCircle className="w-3.5 h-3.5 mt-0.5 flex-shrink-0" />
              ) : (
                <AlertTriangle className="w-3.5 h-3.5 mt-0.5 flex-shrink-0" />
              )}
              <span>{r.message}</span>
            </div>
          ))}
        </div>
      )}

      {results.length > 0 && failedResults.length === 0 && (
        <span
          className="text-xs text-green-600 dark:text-green-400"
          data-testid={`ai-sanity-passed-${fieldName}`}
        >
          All checks passed
        </span>
      )}
    </div>
  );
}
