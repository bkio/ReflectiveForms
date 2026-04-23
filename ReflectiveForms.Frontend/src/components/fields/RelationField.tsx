import { useFormContext, Controller } from 'react-hook-form';
import { FieldComponentProps } from './types';
import { SearchableSelect } from '../form/SearchableSelect';
import { useEntityFormContext } from '../form/DynamicForm';
import { AiRelationSuggestions } from '../ai/AiRelationSuggestions';

export function RelationField({ schema, path }: FieldComponentProps) {
  const { control } = useFormContext();
  const entityFormCtx = useEntityFormContext();
  const relationEntityName = schema.relation_options?.relation_entity_name ?? '';
  const showAiRelationSuggest = !!entityFormCtx?.canUpdate && !!schema.ai_relation_suggestion;

  return (
    <Controller
      name={path}
      control={control}
      render={({ field, fieldState: { error: fieldError } }) => (
        <div>
          <div className="flex items-center gap-2">
            <div className="flex-1">
              <SearchableSelect
                entityName={relationEntityName}
                value={field.value ?? -1}
                onChange={(val) => field.onChange(val)}
              />
            </div>
            {showAiRelationSuggest && (
              <AiRelationSuggestions
                entityName={entityFormCtx!.entityName}
                relationField={schema.name}
                currentText={String(field.value ?? '')}
                onSelect={(id) => field.onChange(id)}
              />
            )}
          </div>
          {fieldError && (
            <p className="mt-1 text-sm text-red-600">{fieldError.message}</p>
          )}
        </div>
      )}
    />
  );
}
