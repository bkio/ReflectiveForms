import { Link } from 'react-router-dom';
import { Plus, FileText, ArrowRight, Eye } from 'lucide-react';
import { useAllSchemas, useCapabilities } from '../hooks/useEntity';

export function DashboardPage() {
  const { data: schemas, isLoading, error } = useAllSchemas();
  const { data: capabilities } = useCapabilities();

  if (isLoading) {
    return (
      <div className="flex items-center justify-center min-h-[400px]">
        <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-blue-600"></div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="bg-red-50 text-red-600 p-6 rounded-lg">
        <h2 className="text-lg font-semibold mb-2">Error</h2>
        <p>{error.message}</p>
      </div>
    );
  }

  const entityTypes = Object.values(schemas ?? {}).filter(
    (s) => !s.features.has_individual_sharing && (!capabilities || capabilities[s.entity_name]?.can_peek_all)
  );
  const editableTypes = entityTypes.filter((s) => s.features.supports_frontend_edit);

  return (
    <div className="space-y-8">
      {/* Header */}
      <div>
        <h1 className="text-2xl font-bold text-gray-900">Dashboard</h1>
        <p className="mt-1 text-gray-500">
          Welcome to ReflectiveForms admin panel. Manage your content below.
        </p>
      </div>

      {/* Entity type cards */}
      <div>
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
          {entityTypes.map((schema) => (
            <div
              key={schema.entity_name}
              className="bg-white rounded-lg shadow-sm border border-gray-200 overflow-hidden hover:shadow-md transition-shadow"
            >
              <div className="p-5">
                <h3 className="text-lg font-semibold text-gray-900">
                  {schema.readable_name.plural}
                </h3>
                <p className="mt-1 text-sm text-gray-500">
                  {schema.features.supports_frontend_edit
                    ? `Manage ${schema.readable_name.plural.toLowerCase()}`
                    : `Browse ${schema.readable_name.plural.toLowerCase()}`}
                </p>

                {/* Feature badges */}
                <div className="mt-3 flex flex-wrap gap-2">
                  {!schema.features.supports_frontend_edit && (
                    <span className="px-2 py-1 text-xs bg-gray-200 text-gray-600 rounded">
                      View Only
                    </span>
                  )}
                  {schema.features.has_author && (
                    <span className="px-2 py-1 text-xs bg-purple-100 text-purple-700 rounded">
                      Has Author
                    </span>
                  )}
                  {schema.features.has_tags && (
                    <span className="px-2 py-1 text-xs bg-green-100 text-green-700 rounded">
                      Has Tags
                    </span>
                  )}
                  {schema.features.has_categories && (
                    <span className="px-2 py-1 text-xs bg-yellow-100 text-yellow-700 rounded">
                      Has Categories
                    </span>
                  )}
                  {schema.features.has_parent_child && (
                    <span className="px-2 py-1 text-xs bg-blue-100 text-blue-700 rounded">
                      Hierarchical
                    </span>
                  )}
                </div>
              </div>

              <div className="px-5 py-3 bg-gray-50 border-t border-gray-100 flex gap-3">
                <Link
                  to={`/entities/${schema.entity_name}`}
                  className="flex-1 flex items-center justify-center gap-2 px-3 py-2 text-sm text-gray-700 bg-white border border-gray-200 rounded-lg hover:bg-gray-50 transition-colors"
                >
                  {schema.features.supports_frontend_edit
                    ? <><ArrowRight className="w-4 h-4" /> View All</>
                    : <><Eye className="w-4 h-4" /> Browse</>
                  }
                </Link>
                {schema.features.supports_frontend_edit && (capabilities?.[schema.entity_name]?.can_create ?? true) && (
                  <Link
                    to={`/entities-admin/${schema.entity_name}?id=new`}
                    className="flex-1 flex items-center justify-center gap-2 px-3 py-2 text-sm text-white bg-blue-600 rounded-lg hover:bg-blue-700 transition-colors"
                  >
                    <Plus className="w-4 h-4" />
                    Create New
                  </Link>
                )}
              </div>
            </div>
          ))}

          {entityTypes.length === 0 && (
            <div className="col-span-full bg-gray-50 rounded-lg p-8 text-center text-gray-500">
              <FileText className="w-12 h-12 mx-auto text-gray-400 mb-3" />
              <p className="font-medium">No content types available</p>
              <p className="text-sm mt-1">Content types will appear here once configured.</p>
            </div>
          )}
        </div>
      </div>

      {/* Quick links */}
      <div>
        <h2 className="text-lg font-semibold text-gray-900 mb-4">Quick Links</h2>
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
          {editableTypes.slice(0, 4).map((schema) => (
            <Link
              key={schema.entity_name}
              to={`/entities-admin/${schema.entity_name}?id=new`}
              className="flex items-center gap-3 p-4 bg-white rounded-lg border border-gray-200 hover:border-blue-300 hover:shadow-sm transition-all"
            >
              <div className="p-2 bg-blue-50 rounded-lg">
                <Plus className="w-5 h-5 text-blue-600" />
              </div>
              <div>
                <p className="font-medium text-gray-900">
                  New {schema.readable_name.singular}
                </p>
                <p className="text-xs text-gray-500">Create a new entry</p>
              </div>
            </Link>
          ))}
        </div>
      </div>
    </div>
  );
}
