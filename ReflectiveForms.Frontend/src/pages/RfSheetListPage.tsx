import { useState, useEffect } from 'react';
import { Link } from 'react-router-dom';
import { Plus, FileSpreadsheet, Trash2, Lock } from 'lucide-react';
import { useEntityList, useCapabilities } from '../hooks/useEntity';
import { useAiAssistantOptional } from '../lib/AiAssistantContext';
import { useLockedEntities } from '../hooks/useLockedEntities';
import { deleteEntity } from '../api/client';
import { toast } from 'sonner';

export function RfSheetListPage() {
  const { data: sheets, isLoading, error, refetch } = useEntityList('rf-sheets');
  const { data: capabilities } = useCapabilities();
  const lockedEntities = useLockedEntities('rf-sheets');
  const [deletingId, setDeletingId] = useState<number | null>(null);

  // Push context to AI assistant
  const assistant = useAiAssistantOptional();
  useEffect(() => {
    assistant?.setContext({ current_page: 'sheet-list', entity_type: 'rf-sheets', entity_id: undefined });
  }, [assistant]);

  const canCreate = capabilities?.['rf-sheets']?.can_create ?? false;

  const handleDelete = async (id: number, title: string) => {
    if (!confirm(`Delete "${title || `Sheet #${id}`}"?`)) return;
    setDeletingId(id);
    try {
      const result = await deleteEntity('rf-sheets', id);
      if (result.error) {
        toast.error(result.error);
      } else {
        toast.success('Sheet deleted');
        refetch();
      }
    } finally {
      setDeletingId(null);
    }
  };

  if (isLoading) {
    return (
      <div className="space-y-4">
        <div className="flex items-center justify-between">
          <h1 className="text-2xl font-bold text-gray-900 dark:text-gray-100">Sheets</h1>
        </div>
        <div className="space-y-2">
          {[1, 2, 3].map((i) => (
            <div key={i} className="h-16 bg-gray-100 dark:bg-gray-800 rounded animate-pulse" />
          ))}
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="text-red-600 dark:text-red-400">
        Failed to load sheets: {error.message}
      </div>
    );
  }

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-bold text-gray-900 dark:text-gray-100">Sheets</h1>
        {canCreate && (
          <Link
            to="/sheets/new"
            className="inline-flex items-center gap-2 px-4 py-2 bg-primary-600 text-white rounded-lg hover:bg-primary-700 transition-colors"
          >
            <Plus className="w-4 h-4" />
            New Sheet
          </Link>
        )}
      </div>

      {(!sheets || sheets.length === 0) ? (
        <div className="text-center py-12 text-gray-500 dark:text-gray-400">
          <FileSpreadsheet className="w-12 h-12 mx-auto mb-4 opacity-50" />
          <p className="text-lg font-medium">No sheets yet</p>
          <p className="text-sm mt-1">Create your first sheet to get started.</p>
        </div>
      ) : (
        <div className="bg-white dark:bg-gray-800 rounded-lg shadow overflow-hidden">
          <table className="min-w-full divide-y divide-gray-200 dark:divide-gray-700">
            <thead className="bg-gray-50 dark:bg-gray-700">
              <tr>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 dark:text-gray-300 uppercase tracking-wider">
                  Name
                </th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 dark:text-gray-300 uppercase tracking-wider">
                  Author
                </th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 dark:text-gray-300 uppercase tracking-wider">
                  Access
                </th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 dark:text-gray-300 uppercase tracking-wider">
                  Last Modified
                </th>
                <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 dark:text-gray-300 uppercase tracking-wider">
                  Actions
                </th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-200 dark:divide-gray-700">
              {sheets.map((sheet) => {
                const lockInfo = lockedEntities.get(sheet.id);
                return (
                <tr key={sheet.id} className="hover:bg-gray-50 dark:hover:bg-gray-700">
                  <td className="px-6 py-4">
                    <div className="flex items-center">
                      <Link
                        to={`/sheets/${sheet.id}`}
                        className="text-primary-600 hover:text-primary-700 dark:hover:text-white font-medium"
                      >
                        {sheet.title || `Sheet #${sheet.id}`}
                      </Link>
                      {lockInfo && (
                        <span
                          className="inline-flex items-center gap-1 ml-2 px-2 py-0.5 text-xs font-medium text-amber-700 dark:text-amber-300 bg-amber-50 dark:bg-amber-900/30 border border-amber-200 dark:border-amber-800 rounded-full"
                          title={`Being edited by ${lockInfo.locked_by_user_name ?? 'another user'}`}
                        >
                          <Lock className="w-3 h-3" />
                          {lockInfo.locked_by_user_name ?? 'Locked'}
                        </span>
                      )}
                    </div>
                  </td>
                  <td className="px-6 py-4 text-sm text-gray-500 dark:text-gray-400">
                    {sheet.author || '—'}
                  </td>
                  <td className="px-6 py-4 text-sm">
                    {sheet.access_level === 'owner' && (
                      <span className="inline-flex items-center px-2 py-0.5 rounded text-xs font-medium bg-blue-100 text-blue-800 dark:bg-blue-900/30 dark:text-blue-300">
                        Owner
                      </span>
                    )}
                    {sheet.access_level === 'edit' && (
                      <span className="inline-flex items-center px-2 py-0.5 rounded text-xs font-medium bg-green-100 text-green-800 dark:bg-green-900/30 dark:text-green-300">
                        Can Edit
                      </span>
                    )}
                    {sheet.access_level === 'view' && (
                      <span className="inline-flex items-center px-2 py-0.5 rounded text-xs font-medium bg-gray-100 text-gray-600 dark:bg-gray-700 dark:text-gray-300">
                        View Only
                      </span>
                    )}
                  </td>
                  <td className="px-6 py-4 text-sm text-gray-500 dark:text-gray-400">
                    {sheet.modified ? new Date(sheet.modified).toLocaleString() : '—'}
                  </td>
                  <td className="px-6 py-4 text-right">
                    {sheet.access_level === 'owner' && (
                      <button
                        onClick={() => handleDelete(sheet.id, sheet.title ?? '')}
                        disabled={deletingId === sheet.id}
                        className="p-1.5 text-gray-400 hover:text-red-600 dark:hover:text-red-400 hover:bg-red-50 dark:hover:bg-red-900/30 rounded transition-colors disabled:opacity-50"
                        title="Delete sheet"
                      >
                        <Trash2 className="w-4 h-4" />
                      </button>
                    )}
                  </td>
                </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
