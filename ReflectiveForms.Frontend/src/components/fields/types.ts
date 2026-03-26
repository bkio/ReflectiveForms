import { FieldSchema } from '../../types/schema';

export interface FieldComponentProps {
  schema: FieldSchema;
  path: string;
  depth?: number;
}
