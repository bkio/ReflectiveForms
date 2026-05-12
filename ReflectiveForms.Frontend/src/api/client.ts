import { EntitySchema, EntityData, PeekEntity, PaginatedPeekResponse, AllCapabilities, EntityRevisionsResponse, BulkReadSource, BulkReadResponse } from '../types/schema';

// AI response types
export interface AiSemanticSearchResult {
  entity_id: number;
  title: string;
  entity_name: string;
  score: number;
}

export interface AiChatMessage {
  role: 'user' | 'assistant';
  content: string;
  /** Tool names used during this assistant turn (frontend-only, not sent to backend) */
  tools_used?: string[];
}

export interface AiSanityCheckResult {
  field: string;
  passed: boolean;
  message?: string;
  severity: 'Warning' | 'Error';
}

export interface AiDiffSummaryResult {
  summary: string;
}

export interface AiNaturalLanguageFilterResult {
  interpreted_filters: { field: string; operator: string; value: string }[];
  combination: string;
  natural_language_interpretation: string;
  results: { id: number; title: string; modified_gmt: string }[];
  used_vector_fallback: boolean;
}

export interface AiRelationSuggestResult {
  id: number;
  title: string;
  score: number;
}

export interface AiAgentChatToolCall {
  tool: string;
  arguments: Record<string, unknown>;
  result_preview: string;
}

export interface AiProposedAction {
  action_id: string;
  action_type: 'create_entity' | 'update_entity' | 'delete_entity' | 'set_field' | 'navigate' | 'show_quality_report' | 'sheet_edit' | 'sheet_add_source';
  description: string;
  requires_approval: boolean;
  entity_type?: string;
  entity_id?: number;
  payload?: Record<string, unknown>;
}

export interface AiExecutedAction {
  action_id: string;
  action_type?: string;
  entity_type?: string;
  entity_id?: number;
  success: boolean;
  message: string;
  result?: Record<string, unknown>;
}

export interface AiAgentChatResponse {
  response: string;
  tool_calls_made: AiAgentChatToolCall[];
  proposed_actions: AiProposedAction[];
  executed_actions?: AiExecutedAction[];
}

export interface AiAgentContext {
  current_page?: string;
  entity_type?: string;
  entity_id?: number;
  current_fields?: Record<string, unknown>;
  errors?: string[];
  selected_field?: string;
  sheet_sources?: string[];
  selected_cell?: string;
}

export interface AiActionConfirmation {
  action_id: string;
  approved: boolean;
  action: AiProposedAction;
}

let _apiBaseUrl = import.meta.env.VITE_API_BASE_URL || 'http://localhost:9000/rf/api';
let _aiBaseUrl: string | null = null;
let _aiDisabled = false;

export function setApiBaseUrl(url: string) {
  _apiBaseUrl = url;
}

export function getApiBaseUrl(): string {
  return _apiBaseUrl;
}

export function setAiBaseUrl(url: string | null) {
  _aiBaseUrl = url;
}

export function setAiDisabled(disabled: boolean) {
  _aiDisabled = disabled;
}

export function isAiDisabled(): boolean {
  return _aiDisabled;
}

interface ApiResponse<T> {
  data?: T;
  error?: string;
}

// Global 401 listener — AuthProvider subscribes to this
type UnauthorizedListener = () => void;
let _onUnauthorized: UnauthorizedListener | null = null;

export function onUnauthorized(listener: UnauthorizedListener | null) {
  _onUnauthorized = listener;
}

async function fetchApi<T>(
  endpoint: string,
  options: RequestInit = {},
  absoluteUrl = false,
  timeoutMs = 20000,
): Promise<ApiResponse<T>> {
  const controller = new AbortController();
  const timeoutId = setTimeout(() => controller.abort(), timeoutMs);
  try {
    const url = absoluteUrl ? endpoint : `${_apiBaseUrl}${endpoint}`;
    const response = await fetch(url, {
      credentials: 'include',
      headers: {
        'Content-Type': 'application/json',
        ...options.headers,
      },
      ...options,
      signal: options.signal ?? controller.signal,
    });

    if (!response.ok) {
      if (response.status === 401 && _onUnauthorized) {
        _onUnauthorized();
      }
      const errorData = await response.json().catch(() => ({ message: 'Request failed' }));
      const msg = typeof errorData === 'string' ? errorData : (errorData.detail || errorData.message);
      return { error: msg || `HTTP ${response.status}` };
    }

    const data = await response.json();
    return { data };
  } catch (error) {
    if (error instanceof DOMException && error.name === 'AbortError') {
      return { error: 'Request timed out' };
    }
    return { error: error instanceof Error ? error.message : 'Network error' };
  } finally {
    clearTimeout(timeoutId);
  }
}

// Schema API
export async function fetchSchema(entityName: string): Promise<ApiResponse<EntitySchema>> {
  return fetchApi<EntitySchema>(`/schema?type=${encodeURIComponent(entityName)}`);
}

export async function fetchAllSchemas(): Promise<ApiResponse<Record<string, EntitySchema>>> {
  return fetchApi<Record<string, EntitySchema>>('/schema');
}

// CRUD API
export async function readEntity(
  entityName: string,
  id: number
): Promise<ApiResponse<EntityData>> {
  return fetchApi<EntityData>(`/crud?operation=READ&type=${encodeURIComponent(entityName)}`, {
    method: 'POST',
    body: JSON.stringify({ id }),
  });
}

export async function peekAllEntities(
  entityName: string
): Promise<ApiResponse<PeekEntity[]>> {
  return fetchApi<PeekEntity[]>(`/crud?operation=PEEK_ALL&type=${encodeURIComponent(entityName)}`, {
    method: 'POST',
    body: '{}',
  });
}

export async function peekAllEntitiesPaginated(
  entityName: string,
  pageSize: number = 20,
  pageToken?: string | null
): Promise<ApiResponse<PaginatedPeekResponse>> {
  let url = `/crud?operation=PEEK_ALL_PAGINATED&type=${encodeURIComponent(entityName)}&page_size=${pageSize}`;
  if (pageToken) {
    url += `&page_token=${encodeURIComponent(pageToken)}`;
  }
  return fetchApi<PaginatedPeekResponse>(url, {
    method: 'POST',
    body: '{}',
  });
}

export async function createEntity(
  entityName: string,
  data: Partial<EntityData>
): Promise<ApiResponse<EntityData>> {
  return fetchApi<EntityData>(`/crud?operation=CREATE&type=${encodeURIComponent(entityName)}`, {
    method: 'POST',
    body: JSON.stringify(data),
  });
}

export async function updateEntity(
  entityName: string,
  data: Partial<EntityData>
): Promise<ApiResponse<EntityData>> {
  return fetchApi<EntityData>(`/crud?operation=UPDATE&type=${encodeURIComponent(entityName)}`, {
    method: 'POST',
    body: JSON.stringify(data),
  });
}

export async function deleteEntity(
  entityName: string,
  id: number
): Promise<ApiResponse<EntityData>> {
  return fetchApi<EntityData>(`/crud?operation=DELETE&type=${encodeURIComponent(entityName)}`, {
    method: 'POST',
    body: JSON.stringify({ id }),
  });
}

// Sanity Check API
export async function sanityCheck(
  entityName: string,
  data: Partial<EntityData>
): Promise<ApiResponse<{ message: string }>> {
  return fetchApi<{ message: string }>(`/sanity_check?type=${encodeURIComponent(entityName)}`, {
    method: 'POST',
    body: JSON.stringify(data),
  });
}

// Sharing Candidates API
export interface SharingCandidate {
  id: number;
  name: string;
  max_permission: 'view' | 'edit';
}

export interface SharingCandidatesResponse {
  users: SharingCandidate[];
  roles: SharingCandidate[];
}

export async function fetchSharingCandidates(
  entityName: string
): Promise<ApiResponse<SharingCandidatesResponse>> {
  return fetchApi<SharingCandidatesResponse>(
    `/crud?operation=SHARING_CANDIDATES&type=${encodeURIComponent(entityName)}`,
    { method: 'POST', body: '{}' }
  );
}

// Entity Lock API
export interface LockStatus {
  entity_id: number;
  locked_by_user_id: number;
  locked_by_user_name: string | null;
  locked_by_tab_id?: string | null;
}

export async function tryLockEntity(
  entityName: string,
  id: number,
  tabId?: string,
): Promise<ApiResponse<void>> {
  const tabParam = tabId ? `&tab_id=${encodeURIComponent(tabId)}` : '';
  return fetchApi<void>(
    `/entity_lock_control?type=${encodeURIComponent(entityName)}&id=${id}&operation=try_lock${tabParam}`,
    { method: 'POST', body: '{}' }
  );
}

export async function unlockEntity(
  entityName: string,
  id: number,
  tabId?: string,
): Promise<ApiResponse<void>> {
  const tabParam = tabId ? `&tab_id=${encodeURIComponent(tabId)}` : '';
  return fetchApi<void>(
    `/entity_lock_control?type=${encodeURIComponent(entityName)}&id=${id}&operation=try_unlock${tabParam}`,
    { method: 'POST', body: '{}' }
  );
}

export async function fetchLockStatus(
  entityName: string,
  id: number
): Promise<ApiResponse<LockStatus>> {
  return fetchApi<LockStatus>(
    `/entity_lock_control?type=${encodeURIComponent(entityName)}&id=${id}&operation=status_one`
  );
}

export async function fetchAllLocked(
  entityName: string
): Promise<ApiResponse<LockStatus[]>> {
  return fetchApi<LockStatus[]>(
    `/entity_lock_control?type=${encodeURIComponent(entityName)}&operation=all_locked`
  );
}

// Auth API
export async function login(
  email: string,
  password: string
): Promise<ApiResponse<{ token: string }>> {
  return fetchApi<{ token: string }>('/login', {
    method: 'POST',
    body: JSON.stringify({ email, password }),
  });
}

export async function logout(): Promise<ApiResponse<{ message: string }>> {
  return fetchApi<{ message: string }>('/logout', {
    method: 'POST',
  });
}

export async function checkAuth(): Promise<boolean> {
  try {
    const response = await fetch(`${_apiBaseUrl}/auth_check`, {
      method: 'POST',
      credentials: 'include',
    });
    return response.ok;
  } catch {
    return false;
  }
}

// Capabilities API
export async function fetchCapabilities(): Promise<ApiResponse<AllCapabilities>> {
  return fetchApi<AllCapabilities>('/capabilities', {
    method: 'POST',
  });
}

export interface FrontendSettingsResponse {
  edit_inactivity_timeout_ms?: number;
  sheets_enabled?: boolean;
}

export async function fetchFrontendSettings(): Promise<ApiResponse<FrontendSettingsResponse>> {
  return fetchApi<FrontendSettingsResponse>('/frontend_settings', {
    method: 'POST',
  });
}

// History/Revisions API
export async function fetchEntityHistory(
  entityName: string,
  id: number
): Promise<ApiResponse<EntityRevisionsResponse>> {
  return fetchApi<EntityRevisionsResponse>(`/crud?operation=HISTORY&type=${encodeURIComponent(entityName)}`, {
    method: 'POST',
    body: JSON.stringify({ id }),
  });
}

// Bulk Read API (RF Sheets)
export async function bulkRead(
  sources: BulkReadSource[]
): Promise<ApiResponse<BulkReadResponse>> {
  return fetchApi<BulkReadResponse>('/bulk_read', {
    method: 'POST',
    body: JSON.stringify({ sources }),
  });
}

// AI API — only called when backend has AI enabled

/**
 * Fetch helper for AI endpoints. Uses the AI base URL override when configured.
 * Returns a disabled error when AI is globally disabled.
 */
async function fetchAiApi<T>(
  endpoint: string,
  options: RequestInit = {},
  timeoutMs = 20000,
): Promise<ApiResponse<T>> {
  if (_aiDisabled) {
    return { error: 'AI features are disabled' };
  }
  const base = _aiBaseUrl ?? `${_apiBaseUrl}/ai`;
  return fetchApi<T>(`${base}${endpoint}`, options, true, timeoutMs);
}

export async function aiSemanticSearch(
  query: string,
  entityName?: string,
  topK?: number
): Promise<ApiResponse<AiSemanticSearchResult[]>> {
  const body: Record<string, unknown> = { query };
  if (entityName) body.entity_name = entityName;
  if (topK) body.top_k = topK;
  const resp = await fetchAiApi<{ results: AiSemanticSearchResult[] }>('/semantic_search', {
    method: 'POST',
    body: JSON.stringify(body),
  });
  if (resp.error) return { error: resp.error };
  return { data: resp.data?.results ?? [] };
}

export async function aiSanityCheck(
  entityName: string,
  fieldName: string,
  fieldValue: unknown
): Promise<ApiResponse<AiSanityCheckResult[]>> {
  return fetchAiApi<AiSanityCheckResult[]>(`/sanity_check?type=${encodeURIComponent(entityName)}`, {
    method: 'POST',
    body: JSON.stringify({ field_name: fieldName, field_value: fieldValue }),
  });
}

export async function aiDiffSummary(
  entityName: string,
  entityId: number,
  revisionIndex: number
): Promise<ApiResponse<AiDiffSummaryResult>> {
  return fetchAiApi<AiDiffSummaryResult>(`/diff_summary?type=${encodeURIComponent(entityName)}`, {
    method: 'POST',
    body: JSON.stringify({ entity_id: entityId, revision_index: revisionIndex }),
  });
}

export async function aiNaturalLanguageFilter(
  entityName: string,
  query: string
): Promise<ApiResponse<AiNaturalLanguageFilterResult>> {
  return fetchAiApi<AiNaturalLanguageFilterResult>(`/nl_filter?type=${encodeURIComponent(entityName)}`, {
    method: 'POST',
    body: JSON.stringify({ query }),
  });
}

export async function aiRelationSuggest(
  entityName: string,
  relationField: string,
  currentText: string
): Promise<ApiResponse<AiRelationSuggestResult[]>> {
  return fetchAiApi<AiRelationSuggestResult[]>(`/relation_suggest?type=${encodeURIComponent(entityName)}`, {
    method: 'POST',
    body: JSON.stringify({ relation_field: relationField, current_text: currentText }),
  });
}

export async function aiAgentChat(
  message: string,
  context?: AiAgentContext,
  confirmedActions?: AiActionConfirmation[],
  history?: { role: string; content: string }[]
): Promise<ApiResponse<AiAgentChatResponse>> {
  const body: Record<string, unknown> = { message };
  if (context) body.context = context;
  if (confirmedActions?.length) body.confirmed_actions = confirmedActions;
  if (history?.length) body.history = history;
  return fetchAiApi<AiAgentChatResponse>('/chat', {
    method: 'POST',
    body: JSON.stringify(body),
  }, 120000);
}
