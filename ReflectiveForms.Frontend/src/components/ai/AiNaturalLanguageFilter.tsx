import { useState, useCallback } from 'react';
import { Search, Loader2, X } from 'lucide-react';
import { useAiNaturalLanguageFilter } from '../../hooks/useAi';

import type { AiNaturalLanguageFilterResult } from '../../api/client';

interface AiNaturalLanguageFilterProps {
  entityName: string;
  onFilterApplied: (result: AiNaturalLanguageFilterResult) => void;
  onFilterCleared: () => void;
}

export function AiNaturalLanguageFilter({
  entityName,
  onFilterApplied,
  onFilterCleared,
}: AiNaturalLanguageFilterProps) {
  const [query, setQuery] = useState('');
  const filterMutation = useAiNaturalLanguageFilter();

  const handleSubmit = useCallback(
    async (e: React.FormEvent) => {
      e.preventDefault();
      if (!query.trim()) return;
      try {
        const result = await filterMutation.mutateAsync({ entityName, query });
        onFilterApplied(result);
      } catch {
        // Error available via filterMutation.error
      }
    },
    [entityName, query, filterMutation, onFilterApplied],
  );

  return (
    <form
      onSubmit={handleSubmit}
      className="flex items-center gap-2"
      data-testid="ai-nl-filter"
    >
      <div className="relative flex-1">
        <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-purple-400" />
        <input
          type="text"
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          placeholder="Filter with natural language..."
          className="w-full pl-10 pr-10 py-2 border border-purple-200 dark:border-purple-700 rounded-md text-sm bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100 placeholder-gray-400 focus:outline-none focus:ring-2 focus:ring-purple-500"
          data-testid="ai-nl-filter-input"
        />
        {query && (
          <button
            type="button"
            onClick={() => { setQuery(''); onFilterCleared(); }}
            className="absolute right-3 top-1/2 -translate-y-1/2 text-gray-400 hover:text-gray-600"
          >
            <X className="w-4 h-4" />
          </button>
        )}
      </div>
      <button
        type="submit"
        disabled={!query.trim() || filterMutation.isPending}
        className="flex items-center gap-1.5 px-3 py-2 text-sm bg-purple-600 text-white rounded-md hover:bg-purple-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
        data-testid="ai-nl-filter-submit"
      >
        {filterMutation.isPending ? (
          <Loader2 className="w-4 h-4 animate-spin" />
        ) : (
          <Search className="w-4 h-4" />
        )}
        Filter
      </button>

      {filterMutation.error && (
        <span className="text-xs text-red-600" data-testid="ai-nl-filter-error">
          {filterMutation.error.message}
        </span>
      )}
    </form>
  );
}
