import { useContext } from 'react';
import { useQueries, useQuery } from '@tanstack/react-query';
import type { CustomPage } from '../lib/types';
import { AuthContext } from './useAuth';

/** How often a page's `canAccess` is re-checked while the app is open (role changes apply without a reload). */
const ACCESS_STALE_MS = 30_000;

export type CustomPageAccessStatus = 'allowed' | 'denied' | 'loading' | 'error';

function accessQuery(page: CustomPage, userId: number | null) {
  return {
    // Keyed by user so a different login in the same tab never sees the previous user's answer
    // (logout also clears the whole query cache).
    queryKey: ['rf-custom-page-access', page.path, userId],
    queryFn: async () => (page.canAccess ? await page.canAccess() : true),
    enabled: !!page.canAccess,
    staleTime: ACCESS_STALE_MS,
    refetchInterval: ACCESS_STALE_MS,
    retry: 1,
  };
}

function toStatus(page: CustomPage, q: { data?: boolean; isError: boolean; isPending: boolean }): CustomPageAccessStatus {
  if (!page.canAccess) return 'allowed';
  if (q.data !== undefined) return q.data ? 'allowed' : 'denied';
  if (q.isError) return 'error';
  return 'loading';
}

function useCurrentUserId(): number | null {
  // Optional: AdminLayout and the Dashboard can render outside an AuthProvider (tests, embedding).
  return useContext(AuthContext)?.user?.id ?? null;
}

/** Access status of one custom page for the signed-in user. Pages without `canAccess` are always allowed. */
export function useCustomPageAccess(page: CustomPage) {
  const userId = useCurrentUserId();
  const q = useQuery(accessQuery(page, userId));
  return { status: toStatus(page, q), refetch: q.refetch };
}

/** The given custom pages the signed-in user may open (still-loading or failed checks are left out). */
export function useAccessibleCustomPages(pages: CustomPage[]): CustomPage[] {
  const userId = useCurrentUserId();
  const results = useQueries({ queries: pages.map((p) => accessQuery(p, userId)) });
  return pages.filter((p, i) => toStatus(p, results[i]) === 'allowed');
}
