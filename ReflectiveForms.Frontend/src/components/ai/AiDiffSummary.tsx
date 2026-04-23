import { useState } from 'react';
import { ChevronDown, ChevronRight, Loader2, Sparkles } from 'lucide-react';
import { useAiDiffSummary } from '../../hooks/useAi';
import type { EntitySchema } from '../../types/schema';

interface AiDiffSummaryProps {
  entityName: string;
  entityId: number;
  revisionIndex: number;
  schema: EntitySchema;
}

export function AiDiffSummary({ entityName, entityId, revisionIndex, schema }: AiDiffSummaryProps) {
  const [expanded, setExpanded] = useState(false);

  // Only fetch when expanded
  const { data, isLoading, error } = useAiDiffSummary(
    entityName,
    expanded ? entityId : undefined,
    expanded ? revisionIndex : undefined,
    schema,
  );

  return (
    <div
      className="border border-purple-200 dark:border-purple-800 rounded-lg overflow-hidden"
      data-testid="ai-diff-summary"
    >
      <button
        type="button"
        onClick={() => setExpanded((v) => !v)}
        className="w-full flex items-center gap-2 px-4 py-3 text-sm font-medium text-purple-700 dark:text-purple-300 bg-purple-50 dark:bg-purple-900/20 hover:bg-purple-100 dark:hover:bg-purple-900/30 transition-colors"
        data-testid="ai-diff-summary-toggle"
      >
        <Sparkles className="w-4 h-4" />
        AI Summary
        {expanded ? (
          <ChevronDown className="w-4 h-4 ml-auto" />
        ) : (
          <ChevronRight className="w-4 h-4 ml-auto" />
        )}
      </button>

      {expanded && (
        <div className="px-4 py-3 text-sm text-gray-700 dark:text-gray-300">
          {isLoading ? (
            <div className="flex items-center gap-2 text-gray-500" data-testid="ai-diff-summary-loading">
              <Loader2 className="w-4 h-4 animate-spin" />
              Generating summary...
            </div>
          ) : error ? (
            <p className="text-red-600 dark:text-red-400" data-testid="ai-diff-summary-error">
              {error.message}
            </p>
          ) : data ? (
            <p data-testid="ai-diff-summary-content">{data.summary}</p>
          ) : null}
        </div>
      )}
    </div>
  );
}
