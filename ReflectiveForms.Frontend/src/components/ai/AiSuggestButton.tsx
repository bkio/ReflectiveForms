import { useCallback } from 'react';
import { Sparkles } from 'lucide-react';
import { useAiAssistantOptional } from '../../lib/AiAssistantContext';

interface AiSuggestButtonProps {
  entityName: string;
  targetField: string;
  currentFields: Record<string, unknown>;
  onSuggestion?: (value: string) => void;
}

export function AiSuggestButton({
  entityName,
  targetField,
  currentFields,
}: AiSuggestButtonProps) {
  const assistant = useAiAssistantOptional();

  const handleClick = useCallback(() => {
    assistant?.triggerMessage(
      `Suggest a value for the "${targetField}" field.`,
      {
        current_page: 'entity-edit',
        entity_type: entityName,
        current_fields: currentFields as Record<string, unknown>,
        selected_field: targetField,
      },
    );
  }, [entityName, targetField, currentFields, assistant]);

  if (!assistant) return null;

  return (
    <button
      type="button"
      onClick={handleClick}
      disabled={assistant.isSending}
      className="inline-flex items-center gap-1 px-2 py-1 text-xs text-purple-600 hover:text-purple-700 hover:bg-purple-50 dark:hover:bg-purple-900/20 rounded transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
      title="AI Suggest"
      data-testid={`ai-suggest-${targetField}`}
    >
      <Sparkles className="w-3.5 h-3.5" />
      Suggest
    </button>
  );
}
