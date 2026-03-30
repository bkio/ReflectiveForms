// Library entry point — createReflectiveFormsApp
export { createReflectiveFormsApp } from './createApp';

// Config provider
export { RfConfigProvider, useRfConfig } from './RfConfigProvider';

// Types
export type { RfConfig, CustomPage } from './types';

// Routes
export { RfRoutes } from './RfRoutes';

// Layout
export { AdminLayout } from '../components/layout/AdminLayout';

// Pages
export { DashboardPage } from '../pages/DashboardPage';
export { EntityListPage } from '../pages/EntityListPage';
export { EntityEditPage } from '../pages/EntityEditPage';
export { EntityViewPage } from '../pages/EntityViewPage';
export { LoginPage } from '../pages/LoginPage';
export { SsoLoginPage } from '../pages/SsoLoginPage';

// Hooks
export {
  useSchema,
  useAllSchemas,
  useEntity,
  useEntityList,
  useCreateEntity,
  useUpdateEntity,
  useDeleteEntity,
  useSanityCheck,
} from '../hooks/useEntity';
export { useEntityLock } from '../hooks/useEntityLock';
export { useAutoSave } from '../hooks/useAutoSave';
export { AuthProvider, useAuth } from '../hooks/useAuth';
export type { UserInfo } from '../hooks/useAuth';
export { getFieldTypes, findFieldInSchema, getAllFieldPaths } from '../hooks/useSchema';

// Form utilities
export { getNestedError, generateFieldId, formatErrorMessage } from './formUtils';

// Schema conversion
export { schemaToZod, generateDefaults } from './schemaToZod';

// Condition parsing
export { evaluateCondition, evaluateCompoundCondition } from './conditionParser';

// Sanitization
export { sanitizeHtml, sanitizeWysiwygHtml, stripHtml } from './sanitize';

// Schema types
export type {
  EntitySchema,
  FieldSchema,
  EntityData,
  PeekEntity,
  PaginatedPeekResponse,
} from '../types/schema';
