import { useState, useCallback, useRef, useEffect } from 'react';
import { Search, X, Loader2 } from 'lucide-react';
import { useAiSemanticSearch } from '../../hooks/useAi';
import { Link } from 'react-router-dom';
import type { EntitySchema } from '../../types/schema';

interface AiGlobalSearchProps {
  schemas: Record<string, EntitySchema>;
  onClose: () => void;
}

export function AiGlobalSearch({ schemas, onClose }: AiGlobalSearchProps) {
  const [query, setQuery] = useState('');
  const [debouncedQuery, setDebouncedQuery] = useState('');
  const inputRef = useRef<HTMLInputElement>(null);
  const timerRef = useRef<ReturnType<typeof setTimeout>>();

  // Find any schema with semantic search to pass as context
  const searchableSchema = Object.values(schemas).find(
    (s) => s.features.supports_semantic_search,
  );

  const { data: results, isLoading } = useAiSemanticSearch(
    debouncedQuery,
    undefined,
    searchableSchema,
  );

  // Focus input on mount
  useEffect(() => {
    inputRef.current?.focus();
  }, []);

  // Close on Escape
  useEffect(() => {
    const handler = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose();
    };
    document.addEventListener('keydown', handler);
    return () => document.removeEventListener('keydown', handler);
  }, [onClose]);

  const handleChange = useCallback((value: string) => {
    setQuery(value);
    clearTimeout(timerRef.current);
    timerRef.current = setTimeout(() => setDebouncedQuery(value), 300);
  }, []);

  return (
    <div className="fixed inset-0 z-50 flex items-start justify-center pt-[15vh]" data-testid="ai-global-search">
      {/* Backdrop */}
      <div className="fixed inset-0 bg-black/50" onClick={onClose} />

      {/* Dialog */}
      <div className="relative w-full max-w-lg bg-white dark:bg-gray-800 rounded-xl shadow-2xl overflow-hidden">
        {/* Search input */}
        <div className="flex items-center gap-3 px-4 py-3 border-b border-gray-200 dark:border-gray-700">
          <Search className="w-5 h-5 text-gray-400 flex-shrink-0" />
          <input
            ref={inputRef}
            type="text"
            value={query}
            onChange={(e) => handleChange(e.target.value)}
            placeholder="Search with AI..."
            className="flex-1 bg-transparent text-gray-900 dark:text-gray-100 placeholder-gray-400 outline-none text-sm"
            data-testid="ai-search-input"
          />
          {isLoading && <Loader2 className="w-4 h-4 text-gray-400 animate-spin" />}
          <button
            onClick={onClose}
            className="p-1 text-gray-400 hover:text-gray-600 dark:hover:text-gray-200"
          >
            <X className="w-4 h-4" />
          </button>
        </div>

        {/* Results */}
        <div className="max-h-80 overflow-y-auto">
          {results && results.length > 0 ? (
            <ul className="py-2">
              {results.map((r) => {
                const entitySchema = schemas[r.entity_name];
                return (
                  <li key={`${r.entity_name}-${r.entity_id}`}>
                    <Link
                      to={`/entities-view/${r.entity_name}?id=${r.entity_id}`}
                      className="flex items-center justify-between px-4 py-2.5 hover:bg-gray-50 dark:hover:bg-gray-700/50 transition-colors"
                      onClick={onClose}
                      data-testid={`ai-search-result-${r.entity_id}`}
                    >
                      <div>
                        <p className="text-sm font-medium text-gray-900 dark:text-gray-100">
                          {r.title}
                        </p>
                        <p className="text-xs text-gray-500 dark:text-gray-400">
                          {entitySchema?.readable_name.singular ?? r.entity_name}
                        </p>
                      </div>
                      <span className="text-xs text-gray-400 tabular-nums">
                        {Math.round(r.score * 100)}%
                      </span>
                    </Link>
                  </li>
                );
              })}
            </ul>
          ) : debouncedQuery && !isLoading ? (
            <div className="px-4 py-8 text-center text-sm text-gray-500" data-testid="ai-search-empty">
              No results found
            </div>
          ) : !debouncedQuery ? (
            <div className="px-4 py-8 text-center text-sm text-gray-400">
              Type to search across all content
            </div>
          ) : null}
        </div>
      </div>
    </div>
  );
}
