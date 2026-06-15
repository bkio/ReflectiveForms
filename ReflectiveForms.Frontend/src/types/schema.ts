// Schema types matching the backend EntitySchema
// These are automatically generated from the backend schema

export interface EntitySchema {
  entity_name: string;
  readable_name: ReadableName;
  features: EntityFeatures;
  fields: FieldSchema[];
  api_endpoints: ApiEndpoints;
  schema_version: string;
}

export interface ReadableName {
  singular: string;
  plural: string;
}

export interface EntityFeatures {
  has_author: boolean;
  has_tags: boolean;
  has_categories: boolean;
  has_parent_child: boolean;
  require_title_uniqueness: boolean;
  supports_frontend_edit: boolean;
  show_in_navigation: boolean;
  has_individual_sharing: boolean;
  custom_frontend_list_route?: string | null;
  supports_semantic_search: boolean;
  supports_ai_generation: boolean;
  supports_ai_diff_summary: boolean;
  supports_natural_language_filter: boolean;
}

export interface EntityCapabilities {
  can_peek_all: boolean;
  can_read: boolean;
  can_create: boolean;
  can_update: boolean;
  can_delete: boolean;
}

export type AllCapabilities = Record<string, EntityCapabilities>;

/** Global frontend settings served by the backend /frontend_settings endpoint. */
export interface GlobalSettings {
  edit_inactivity_timeout_ms?: number;
  sheets_enabled?: boolean;
  /** Lowercase entity names of reserved types to hide from navigation. e.g. ["tags", "categories"] */
  reserved_entity_types_to_hide_in_navigation?: string[];
}

export interface ApiEndpoints {
  crud: string;
  sanity_check: string;
  entity_lock: string;
  media: string;
  ai?: AiApiEndpoints;
  openapi?: string;
}

export interface AiApiEndpoints {
  semantic_search: string;
  generate: string;
  suggest: string;
  sanity_check: string;
  diff_summary: string;
  nl_filter: string;
  relation_suggest: string;
  reindex: string;
  chat?: string;
}

export type FieldSchemaType =
  | 'Text'
  | 'TextArea'
  | 'WysiwygEditor'
  | 'Number'
  | 'Range'
  | 'Email'
  | 'Url'
  | 'Select'
  | 'Checkbox'
  | 'Relation'
  | 'DatePicker'
  | 'Group'
  | 'Repeater'
  | 'MediaSourceBase64';

export interface FieldSchema {
  name: string;
  type: FieldSchemaType;
  label: string;
  instructions?: string;
  required: boolean;
  default_value?: unknown;
  display_condition?: string;
  has_dynamic_choices_runtime: boolean;
  has_dynamic_choices_compile_time: boolean;
  has_logic_sanity_check: boolean;

  // AI field-level schema
  ai_suggestion?: AiSuggestionSchema;
  ai_sanity_checks?: AiSanityCheckSchema[];
  ai_relation_suggestion?: AiRelationSuggestionSchema;

  // Type-specific options
  text_options?: TextFieldOptions;
  select_options?: SelectFieldOptions;
  number_options?: NumberFieldOptions;
  date_options?: DateFieldOptions;
  relation_options?: RelationFieldOptions;
  repeater_options?: RepeaterFieldOptions;
  group_options?: GroupFieldOptions;
  media_options?: MediaFieldOptions;
}

export interface AiSuggestionSchema {
  prompt: string;
  source_fields: string[];
}

export interface AiSanityCheckSchema {
  prompt: string;
  severity: 'Warning' | 'Error';
}

export interface AiRelationSuggestionSchema {
  top_k: number;
}

export interface TextFieldOptions {
  placeholder?: string;
  max_length?: number;
  is_multiline: boolean;
}

export interface SelectFieldOptions {
  choices?: SelectChoice[];
  allow_multiple: boolean;
  dynamic_choices_js_function?: string;
}

export interface SelectChoice {
  value: string;
  label: string;
}

export interface NumberFieldOptions {
  min?: number;
  max?: number;
  step?: number;
  is_range: boolean;
}

export interface DateFieldOptions {
  format: string;
  include_time: boolean;
}

export interface RelationFieldOptions {
  relation_entity_name: string;
  is_relation_entity_not_exists_ok: boolean;
  allow_multiple: boolean;
}

export interface RepeaterFieldOptions {
  item_schema: FieldSchema[];
  min_items?: number;
  max_items?: number;
  add_button_label: string;
  use_accordion: boolean;
  sticky_title_field?: string;
  render_style: GroupRenderStyle;
}

export interface GroupFieldOptions {
  child_schema: FieldSchema[];
  sticky_title_field?: string;
  render_style: GroupRenderStyle;
}

export type GroupRenderStyle = 'Full' | 'Grid2' | 'Grid3' | 'Grid4' | 'Grid6';

export interface MediaFieldOptions {
  max_file_size_mb: number;
  accepted_types?: string[];
  preview_enabled: boolean;
}

// Entity data types
export interface EntityData {
  id: number;
  slug: string;
  title: { rendered: string };
  date: string;
  date_gmt: string;
  modified: string;
  modified_gmt: string;
  fields: Record<string, unknown>;
  parent?: number;
  author?: number;
  tags?: number[];
  categories?: number[];
  access_level?: 'owner' | 'edit' | 'view';
  is_system_managed?: boolean;
  can_edit_author?: boolean;
}

export interface PeekEntity {
  id: number;
  title?: string;
  name?: string;
  author?: string;
  author_id?: number;
  modified?: string;
  modified_gmt?: string;
  date?: string;
  date_gmt?: string;
  categories?: string[];
  tags?: string[];
  parent?: string;
  parent_id?: number;
  access_level?: 'owner' | 'edit' | 'view';
  is_system_managed?: boolean;
}

export interface PaginatedPeekResponse {
  items: PeekEntity[];
  next_page_token: string | null;
  total_count: number | null;
}

export interface RevisionEntry {
  revision_number: number;
  date: string;
  date_gmt: string;
  modified_by_email: string;
  object: EntityData;
}

export interface EntityRevisionsResponse {
  revisions_count: number;
  revisions: RevisionEntry[];
}

// Bulk Read types (RF Sheets)
export interface BulkReadSource {
  entity: string;
  fields?: string[];
}

export interface BulkReadResultRow {
  id: number;
  fields: Record<string, unknown>;
  [key: string]: unknown;
}

export interface BulkReadResult {
  entity: string;
  total_count: number;
  rows: BulkReadResultRow[];
}

export interface BulkReadResponse {
  results: BulkReadResult[];
  unauthorized: string[];
}
