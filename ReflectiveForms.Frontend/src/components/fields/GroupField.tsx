import { FormField } from './FormField';
import { FieldComponentProps } from './types';

export function GroupField({ schema, path, depth = 0 }: FieldComponentProps) {
  const childSchema = schema.group_options?.child_schema ?? [];
  const renderStyle = schema.group_options?.render_style ?? 'Full';

  const gridClass = {
    Full: 'grid-cols-1',
    Grid2: 'grid-cols-1 md:grid-cols-2',
    Grid3: 'grid-cols-1 md:grid-cols-3',
    Grid4: 'grid-cols-1 md:grid-cols-2 lg:grid-cols-4',
    Grid6: 'grid-cols-1 md:grid-cols-2 lg:grid-cols-3 xl:grid-cols-6',
  }[renderStyle];

  return (
    <div className={`grid ${gridClass} gap-4`}>
      {childSchema.map((childField) => (
        <FormField
          key={childField.name}
          fieldSchema={childField}
          basePath={path}
          depth={depth + 1}
        />
      ))}
    </div>
  );
}
