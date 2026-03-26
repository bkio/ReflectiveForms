import { useParams, useSearchParams, Link } from 'react-router-dom';
import { Trash2, Edit, Copy, Plus } from 'lucide-react';
import { useSchema, useEntityList, useDeleteEntity } from '../hooks/useEntity';
import { toast } from 'sonner';

export function EntityListPage() {
  const { entityName } = useParams<{ entityName: string }>();
  const [searchParams] = useSearchParams();

  const { data: schema, isLoading: schemaLoading } = useSchema(entityName ?? '');
  const { data: entities, isLoading: entitiesLoading } = useEntityList(entityName ?? '');
  const deleteMutation = useDeleteEntity(entityName ?? '');

  const handleDelete = async (id: number, title: string) => {
    if (!confirm(`Are you sure you want to delete "${title}"?`)) return;

    const result = await deleteMutation.mutateAsync(id);
    if (result.error) {
      toast.error(result.error);
    } else {
      toast.success('Deleted successfully');
    }
  };

  if (schemaLoading || entitiesLoading) {
    return (
      <div className="min-h-screen bg-gray-50 flex items-center justify-center">
        <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-blue-600"></div>
      </div>
    );
  }

  return (
    <div className="min-h-screen bg-gray-50">
      <div className="max-w-6xl mx-auto py-8 px-4">
        {/* Header */}
        <div className="flex justify-between items-center mb-8">
          <h1 className="text-2xl font-bold text-gray-900">
            {schema?.readable_name.plural ?? entityName}
          </h1>
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
          <table className="min-w-full divide-y divide-gray-200">
            <thead className="bg-gray-50">
              <tr>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Title
                </th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Last Modified
                </th>
                <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Actions
                </th>
              </tr>
            </thead>
            <tbody className="bg-white divide-y divide-gray-200">
              {entities?.map((entity) => (
                <tr key={entity.id} className="hover:bg-gray-50">
                  <td className="px-6 py-4 whitespace-nowrap">
                    <Link
                      to={`/entities-admin/${entityName}?id=${entity.id}`}
                      className="text-blue-600 hover:text-blue-800"
                    >
                      {entity.title ?? entity.name ?? `ID: ${entity.id}`}
                    </Link>
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                    {/* Would show modified date if available */}
                    -
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-right text-sm font-medium">
                    <div className="flex justify-end gap-2">
                      <Link
                        to={`/entities-admin/${entityName}?id=${entity.id}`}
                        className="p-2 text-gray-500 hover:text-blue-600"
                        title="Edit"
                      >
                        <Edit className="w-4 h-4" />
                      </Link>
                      <Link
                        to={`/entities-admin/${entityName}?id=clone_from_${entity.id}`}
                        className="p-2 text-gray-500 hover:text-green-600"
                        title="Clone"
                      >
                        <Copy className="w-4 h-4" />
                      </Link>
                      <button
                        onClick={() => handleDelete(entity.id, entity.title ?? entity.name ?? '')}
                        className="p-2 text-gray-500 hover:text-red-600"
                        title="Delete"
                      >
                        <Trash2 className="w-4 h-4" />
                      </button>
                    </div>
                  </td>
                </tr>
              ))}
              {(!entities || entities.length === 0) && (
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
      </div>
    </div>
  );
}
