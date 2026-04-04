import { EntitySchema, EntityData, PeekEntity, PaginatedPeekResponse, AllCapabilities, EntityRevisionsResponse } from '../types/schema';

let _apiBaseUrl = import.meta.env.VITE_API_BASE_URL || 'http://localhost:9000/rf/api';

export function setApiBaseUrl(url: string) {
  _apiBaseUrl = url;
}

export function getApiBaseUrl(): string {
  return _apiBaseUrl;
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
  options: RequestInit = {}
): Promise<ApiResponse<T>> {
  try {
    const response = await fetch(`${_apiBaseUrl}${endpoint}`, {
      credentials: 'include',
      headers: {
        'Content-Type': 'application/json',
        ...options.headers,
      },
      ...options,
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
    return { error: error instanceof Error ? error.message : 'Network error' };
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

// Entity Lock API
export async function tryLockEntity(
  entityName: string,
  id: number
): Promise<ApiResponse<void>> {
  return fetchApi<void>(
    `/entity_lock_control?type=${encodeURIComponent(entityName)}&id=${id}&operation=try_lock`,
    { method: 'POST', body: '{}' }
  );
}

export async function unlockEntity(
  entityName: string,
  id: number
): Promise<ApiResponse<void>> {
  return fetchApi<void>(
    `/entity_lock_control?type=${encodeURIComponent(entityName)}&id=${id}&operation=unlock`,
    { method: 'POST', body: '{}' }
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
