import { Link } from 'react-router-dom';
import { Lock, RotateCw } from 'lucide-react';
import type { CustomPage } from '../../lib/types';
import { useCustomPageAccess } from '../../hooks/useCustomPageAccess';

/** Renders a custom page only for users its `canAccess` allows; everyone else gets a "no access" notice. */
export function CustomPageGate({ page }: { page: CustomPage }) {
  const { status, refetch } = useCustomPageAccess(page);
  const Page = page.component;

  if (status === 'allowed') return <Page />;

  if (status === 'loading') {
    return (
      <div className="flex items-center justify-center min-h-[300px]" data-testid="custom-page-access-loading">
        <div className="animate-spin rounded-full h-10 w-10 border-b-2 border-primary-600" />
      </div>
    );
  }

  return (
    <div className="max-w-lg mx-auto mt-12 bg-white dark:bg-gray-800 border border-gray-200 dark:border-gray-700 rounded-lg p-8 text-center"
         data-testid={status === 'denied' ? 'custom-page-no-access' : 'custom-page-access-error'}>
      <Lock className="w-10 h-10 mx-auto text-gray-400 mb-3" />
      {status === 'denied' ? (
        <>
          <h2 className="text-lg font-semibold text-gray-900 dark:text-gray-100">No access</h2>
          <p className="mt-1 text-sm text-gray-500">You don&apos;t have access to {page.label}. Ask an administrator for a role that includes it.</p>
        </>
      ) : (
        <>
          <h2 className="text-lg font-semibold text-gray-900 dark:text-gray-100">Couldn&apos;t check your access</h2>
          <p className="mt-1 text-sm text-gray-500">{page.label} could not verify your permissions.</p>
          <button type="button" onClick={() => refetch()}
                  className="mt-4 inline-flex items-center gap-2 px-3 py-2 text-sm text-gray-700 bg-white border border-gray-200 rounded-lg hover:bg-gray-50">
            <RotateCw className="w-4 h-4" /> Retry
          </button>
        </>
      )}
      <div className="mt-4">
        <Link to="/" className="text-sm text-blue-600 hover:text-blue-800">Back to Dashboard</Link>
      </div>
    </div>
  );
}
