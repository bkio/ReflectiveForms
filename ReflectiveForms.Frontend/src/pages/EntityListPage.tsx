import { useParams, Link } from 'react-router-dom';
import { Trash2, Edit, Copy, Plus, ChevronLeft, ChevronRight, Loader2, Eye } from 'lucide-react';
import { useSchema, usePaginatedEntityList, useDeleteEntity } from '../hooks/useEntity';
import { toast } from 'sonner';
import { useMemo, useState } from 'react';
import { PeekEntity } from '../types/schema';

const PAGE_SIZE = 20;

export function EntityListPage() {
  const { entityName } = useParams<{ entityName: string }>();
  const [currentPageIndex, setCurrentPageIndex] = useState(0);

  const { data: schema, isLoading: schemaLoading } = useSchema(entityName ?? '');
  const {
    data,
    isLoading: entitiesLoading,
    fetchNextPage,
    hasNextPage,
    isFetchingNextPage,
  } = usePaginatedEntityList(entityName ?? '', PAGE_SIZE);
  const deleteMutation = useDeleteEntity(entityName ?? '');

  const pages = data?.pages ?? [];
  const totalCount = pages[0]?.total_count ?? null;
  const currentPage: PeekEntity[] = pages[currentPageIndex]?.items ?? [];

  const totalPages = useMemo(() => {
    if (totalCount === null) return null;
    return Math.max(1, Math.ceil(totalCount / PAGE_SIZE));
  }, [totalCount]);

  const handleDelete = async (id: number, title: string) => {
    if (!confirm(`Are you sure you want to delete "${title}"?`)) return;

    const result = await deleteMutation.mutateAsync(id);
    if (result.error) {
      toast.error(result.error);
    } else {
      toast.success('Deleted successfully');
    }
  };

  const handleNextPage = async () => {
    const nextIdx = currentPageIndex + 1;
    // If we already fetched this page, just navigate to it
    if (nextIdx < pages.length) {
      setCurrentPageIndex(nextIdx);
    } else if (hasNextPage) {
      await fetchNextPage();
      setCurrentPageIndex(nextIdx);
    }
  };

  const handlePrevPage = () => {
    if (currentPageIndex > 0) {
      setCurrentPageIndex(currentPageIndex - 1);
    }
  };

  if (schemaLoading || entitiesLoading) {
    return (
      <div className="min-h-screen bg-gray-50 flex items-center justify-center">
        <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-blue-600"></div>
      </div>
    );
  }

  const canGoNext = currentPageIndex + 1 < pages.length || hasNextPage;
  const canGoPrev = currentPageIndex > 0;

  return (
    <div className="min-h-screen bg-gray-50">
      <div className="max-w-6xl mx-auto py-8 px-4">
        {/* Header */}
        <div className="flex justify-between items-center mb-8">
          <div>
            <h1 className="text-2xl font-bold text-gray-900">
              {schema?.readable_name.plural ?? entityName}
            </h1>
            {totalCount !== null && (
              <p className="mt-1 text-sm text-gray-500">{totalCount} total</p>
            )}
          </div>
          <Link
            to={`/entities-admin/${entityName}?id=new`}
            className="flex items-center gap-2 px-4 py-2 bg-blue-600 text-white rounded-md hover:bg-blue-700 transition-colors"
          >
            <Plus className="w-4 h-4" />
            Add New
          </Link>
        </div>

        {/* Table */}
        <div className="bg-white rounded-lg shadow overflow-hidden">
          <div className="overflow-x-auto">
          <table className="min-w-full divide-y divide-gray-200">
            <thead className="bg-gray-50">
              <tr>
                <th className="px-4 sm:px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Title
                </th>
                <th className="hidden sm:table-cell px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Last Modified
                </th>
                <th className="px-4 sm:px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Actions
                </th>
              </tr>
            </thead>
            <tbody className="bg-white divide-y divide-gray-200">
              {currentPage.map((entity) => (
                <tr key={entity.id} className="hover:bg-gray-50 transition-colors">
                  <td className="px-4 sm:px-6 py-4 whitespace-nowrap">
                    <Link
                      to={`/entities-admin/${entityName}?id=${entity.id}`}
                      className="text-blue-600 hover:text-blue-800 font-medium"
                    >
                      {entity.title ?? entity.name ?? `ID: ${entity.id}`}
                    </Link>
                  </td>
                  <td className="hidden sm:table-cell px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                    -
                  </td>
                  <td className="px-4 sm:px-6 py-4 whitespace-nowrap text-right text-sm font-medium">
                    <div className="flex justify-end gap-1 sm:gap-2">
                      <Link
                        to={`/entities-view/${entityName}?id=${entity.id}`}
                        className="p-2 text-gray-500 hover:text-purple-600 rounded-md hover:bg-purple-50 transition-colors"
                        title="View"
                      >
                        <Eye className="w-4 h-4" />
                      </Link>
                      <Link
                        to={`/entities-admin/${entityName}?id=${entity.id}`}
                        className="p-2 text-gray-500 hover:text-blue-600 rounded-md hover:bg-blue-50 transition-colors"
                        title="Edit"
                      >
                        <Edit className="w-4 h-4" />
                      </Link>
                      <Link
                        to={`/entities-admin/${entityName}?id=clone_from_${entity.id}`}
                        className="p-2 text-gray-500 hover:text-green-600 rounded-md hover:bg-green-50 transition-colors"
                        title="Clone"
                      >
                        <Copy className="w-4 h-4" />
                      </Link>
                      <button
                        onClick={() => handleDelete(entity.id, entity.title ?? entity.name ?? '')}
                        className="p-2 text-gray-500 hover:text-red-600 rounded-md hover:bg-red-50 transition-colors"
                        title="Delete"
                      >
                        <Trash2 className="w-4 h-4" />
                      </button>
                    </div>
                  </td>
                </tr>
              ))}
              {currentPage.length === 0 && (
                <tr>
                  <td colSpan={3} className="px-6 py-8 text-center text-gray-500">
                    No {schema?.readable_name.plural.toLowerCase() ?? 'entities'} found.
                    <Link
                      to={`/entities-admin/${entityName}?id=new`}
                      className="ml-2 text-blue-600 hover:text-blue-800"
                    >
                      Create one?
                    </Link>
                  </td>
                </tr>
              )}
            </tbody>
          </table>
          </div>

          {/* Pagination controls */}
          {(canGoPrev || canGoNext) && (
            <div className="flex items-center justify-between px-4 sm:px-6 py-3 border-t border-gray-200 bg-gray-50">
              <div className="text-sm text-gray-500">
                Page {currentPageIndex + 1}{totalPages !== null ? ` of ${totalPages}` : ''}
              </div>
              <div className="flex items-center gap-2">
                <button
                  onClick={handlePrevPage}
                  disabled={!canGoPrev}
                  className="inline-flex items-center gap-1 px-3 py-1.5 text-sm border border-gray-300 rounded-md bg-white hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
                >
                  <ChevronLeft className="w-4 h-4" />
                  Previous
                </button>
                <button
                  onClick={handleNextPage}
                  disabled={!canGoNext || isFetchingNextPage}
                  className="inline-flex items-center gap-1 px-3 py-1.5 text-sm border border-gray-300 rounded-md bg-white hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
                >
                  {isFetchingNextPage ? (
                    <Loader2 className="w-4 h-4 animate-spin" />
                  ) : (
                    <>
                      Next
                      <ChevronRight className="w-4 h-4" />
                    </>
                  )}
                </button>
              </div>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
