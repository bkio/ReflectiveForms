import { useState, useCallback, useRef, useEffect } from 'react';
import { Sparkles, Loader2 } from 'lucide-react';
import { useAiRelationSuggest } from '../../hooks/useAi';
import type { AiRelationSuggestResult } from '../../api/client';

interface AiRelationSuggestionsProps {
  entityName: string;
  relationField: string;
  currentText: string;
  onSelect: (id: number, title: string) => void;
}

export function AiRelationSuggestions({
  entityName,
  relationField,
  currentText,
  onSelect,
}: AiRelationSuggestionsProps) {
  const [suggestions, setSuggestions] = useState<AiRelationSuggestResult[]>([]);
  const [visible, setVisible] = useState(false);
  const suggestMutation = useAiRelationSuggest();
  const containerRef = useRef<HTMLDivElement>(null);

  const handleFetch = useCallback(async () => {
    if (!currentText.trim()) return;
    try {
      const data = await suggestMutation.mutateAsync({
        entityName,
        relationField,
        currentText,
      });
      setSuggestions(data);
      setVisible(true);
    } catch {
      // Error available via suggestMutation.error
    }
  }, [entityName, relationField, currentText, suggestMutation]);

  // Close on outside click
  useEffect(() => {
    if (!visible) return;
    const handler = (e: MouseEvent) => {
      if (containerRef.current && !containerRef.current.contains(e.target as Node)) {
        setVisible(false);
      }
    };
    document.addEventListener('mousedown', handler);
    return () => document.removeEventListener('mousedown', handler);
  }, [visible]);

  return (
    <div className="relative inline-block" ref={containerRef} data-testid={`ai-relation-suggest-${relationField}`}>
      <button
        type="button"
        onClick={handleFetch}
        disabled={suggestMutation.isPending || !currentText.trim()}
        className="inline-flex items-center gap-1 px-2 py-1 text-xs text-purple-600 hover:text-purple-700 hover:bg-purple-50 dark:hover:bg-purple-900/20 rounded transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
        title="Get AI suggestions"
        data-testid={`ai-relation-suggest-button-${relationField}`}
      >
        {suggestMutation.isPending ? (
          <Loader2 className="w-3.5 h-3.5 animate-spin" />
        ) : (
          <Sparkles className="w-3.5 h-3.5" />
        )}
        Suggest
      </button>

      {visible && suggestions.length > 0 && (
        <div
          className="absolute left-0 mt-1 w-64 bg-white dark:bg-gray-800 border border-gray-200 dark:border-gray-700 rounded-md shadow-lg z-20 max-h-48 overflow-y-auto"
          data-testid={`ai-relation-suggest-dropdown-${relationField}`}
        >
          {suggestions.map((s) => (
            <button
              key={s.id}
              type="button"
              className="w-full flex items-center justify-between px-3 py-2 text-sm text-gray-700 dark:text-gray-300 hover:bg-gray-50 dark:hover:bg-gray-700/50 transition-colors"
              onClick={() => {
                onSelect(s.id, s.title);
                setVisible(false);
              }}
              data-testid={`ai-relation-option-${s.id}`}
            >
              <span className="truncate">{s.title}</span>
              <span className="text-xs text-gray-400 ml-2 tabular-nums">
                {Math.round(s.score * 100)}%
              </span>
            </button>
          ))}
        </div>
      )}
    </div>
  );
}
