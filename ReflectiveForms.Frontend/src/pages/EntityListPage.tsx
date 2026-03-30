import { useParams, Link } from 'react-router-dom';
import { Trash2, Edit, Copy, Plus, ChevronLeft, ChevronRight, Eye, Search, X, ArrowUp, ArrowDown, ArrowUpDown, ChevronDown as ChevronDownIcon, ChevronRight as ChevronRightIcon } from 'lucide-react';
import { useSchema, useEntityList, useDeleteEntity } from '../hooks/useEntity';
import { toast } from 'sonner';
import { useMemo, useState, useCallback } from 'react';
import { PeekEntity } from '../types/schema';

const PAGE_SIZE = 20;

type SortColumn = 'title' | 'modified' | 'author';
type SortDirection = 'asc' | 'desc';
interface SortConfig {
  column: SortColumn;
  direction: SortDirection;
}

export interface TreeNode {
  entity: PeekEntity;
  children: TreeNode[];
  depth: number;
}

/**
 * Build a tree from a flat list of entities using parent_id.
 * Entities with no parent_id (or parent_id <= 0) are roots.
 * Orphans (entities whose parent is missing from the list) become roots.
 */
export function buildEntityTree(entities: PeekEntity[]): TreeNode[] {
  const byId = new Map<number, TreeNode>();
  const roots: TreeNode[] = [];

  // Create nodes
  for (const entity of entities) {
    byId.set(entity.id, { entity, children: [], depth: 0 });
  }

  // Wire parent-child
  for (const entity of entities) {
    const node = byId.get(entity.id)!;
    const parentId = entity.parent_id;
    if (parentId != null && parentId > 0 && byId.has(parentId)) {
      byId.get(parentId)!.children.push(node);
    } else {
      roots.push(node);
    }
  }

  // Set depths
  function setDepths(nodes: TreeNode[], depth: number) {
    for (const node of nodes) {
      node.depth = depth;
      setDepths(node.children, depth + 1);
    }
  }
  setDepths(roots, 0);

  return roots;
}

/**
 * Flatten a tree into a pre-order list, respecting expanded/collapsed state.
 */
function flattenTree(roots: TreeNode[], expanded: Set<number>): TreeNode[] {
  const result: TreeNode[] = [];
  function walk(nodes: TreeNode[]) {
    for (const node of nodes) {
      result.push(node);
      if (node.children.length > 0 && expanded.has(node.entity.id)) {
        walk(node.children);
      }
    }
  }
  walk(roots);
  return result;
}

function formatDate(dateStr: string | undefined): string {
  if (!dateStr) return '-';
  try {
    const d = new Date(dateStr);
    if (isNaN(d.getTime())) return dateStr;
    return d.toLocaleDateString('en-US', { year: 'numeric', month: 'short', day: 'numeric' });
  } catch {
    return dateStr;
  }
}

export function EntityListPage() {
  const { entityName } = useParams<{ entityName: string }>();
  const [currentPage, setCurrentPage] = useState(0);
  const [searchTerm, setSearchTerm] = useState('');
  const [sortConfig, setSortConfig] = useState<SortConfig>({ column: 'modified', direction: 'desc' });
  const [expanded, setExpanded] = useState<Set<number>>(new Set());

  const { data: schema, isLoading: schemaLoading } = useSchema(entityName ?? '');
  const { data: allEntities, isLoading: entitiesLoading } = useEntityList(entityName ?? '');
  const deleteMutation = useDeleteEntity(entityName ?? '');

  const hasAuthor = schema?.features.has_author ?? false;
  const hasParentChild = schema?.features.has_parent_child ?? false;

  // Filter → Sort → Paginate pipeline
  const filteredAndSorted = useMemo(() => {
    if (!allEntities) return [];
    let result = [...allEntities];

    // Filter
    if (searchTerm.trim()) {
      const term = searchTerm.trim().toLowerCase();
      result = result.filter(e => {
        const title = (e.title ?? e.name ?? '').toLowerCase();
        const author = (e.author ?? '').toLowerCase();
        return title.includes(term) || author.includes(term);
      });
    }

    // Sort
    result.sort((a, b) => {
      let cmp = 0;
      switch (sortConfig.column) {
        case 'title': {
          const aTitle = (a.title ?? a.name ?? '').toLowerCase();
          const bTitle = (b.title ?? b.name ?? '').toLowerCase();
          cmp = aTitle.localeCompare(bTitle);
          break;
        }
        case 'modified': {
          const aDate = a.modified ?? a.modified_gmt ?? '';
          const bDate = b.modified ?? b.modified_gmt ?? '';
          cmp = aDate.localeCompare(bDate);
          break;
        }
        case 'author': {
          const aAuthor = (a.author ?? '').toLowerCase();
          const bAuthor = (b.author ?? '').toLowerCase();
          cmp = aAuthor.localeCompare(bAuthor);
          break;
        }
      }
      return sortConfig.direction === 'asc' ? cmp : -cmp;
    });

    return result;
  }, [allEntities, searchTerm, sortConfig]);

  // Build tree for hierarchical entities
  const entityTree = useMemo(() => {
    if (!hasParentChild) return null;
    return buildEntityTree(filteredAndSorted);
  }, [hasParentChild, filteredAndSorted]);

  // Flatten tree based on expanded state (for hierarchical rendering)
  const flattenedTree = useMemo(() => {
    if (!entityTree) return null;
    return flattenTree(entityTree, expanded);
  }, [entityTree, expanded]);

  const toggleExpanded = useCallback((id: number) => {
    setExpanded(prev => {
      const next = new Set(prev);
      if (next.has(id)) {
        next.delete(id);
      } else {
        next.add(id);
      }
      return next;
    });
  }, []);

  // Determine what gets paginated: flattened tree in tree mode, flat list otherwise
  const displayList = flattenedTree ?? filteredAndSorted;

  const totalFiltered = displayList.length;
  const totalAll = allEntities?.length ?? 0;
  const totalPages = Math.max(1, Math.ceil(totalFiltered / PAGE_SIZE));
  const pageItems = displayList.slice(currentPage * PAGE_SIZE, (currentPage + 1) * PAGE_SIZE);

  const handleSort = useCallback((column: SortColumn) => {
    setSortConfig(prev => {
      if (prev.column === column) {
        return { column, direction: prev.direction === 'asc' ? 'desc' : 'asc' };
      }
      return { column, direction: 'asc' };
    });
    setCurrentPage(0);
  }, []);

  const handleSearchChange = useCallback((value: string) => {
    setSearchTerm(value);
    setCurrentPage(0);
  }, []);

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

  const isEditable = schema?.features.supports_frontend_edit ?? true;

  function SortIcon({ column }: { column: SortColumn }) {
    if (sortConfig.column !== column) return <ArrowUpDown className="w-3.5 h-3.5 ml-1 text-gray-400" />;
    return sortConfig.direction === 'asc'
      ? <ArrowUp className="w-3.5 h-3.5 ml-1 text-blue-600" />
      : <ArrowDown className="w-3.5 h-3.5 ml-1 text-blue-600" />;
  }

  return (
    <div className="min-h-screen bg-gray-50">
      <div className="max-w-6xl mx-auto py-8 px-4">
        {/* Header */}
        <div className="flex justify-between items-center mb-6">
          <div>
            <h1 className="text-2xl font-bold text-gray-900">
              {schema?.readable_name.plural ?? entityName}
            </h1>
            <p className="mt-1 text-sm text-gray-500">{totalAll} total</p>
          </div>
          {isEditable && (
            <Link
              to={`/entities-admin/${entityName}?id=new`}
              className="flex items-center gap-2 px-4 py-2 bg-blue-600 text-white rounded-md hover:bg-blue-700 transition-colors"
            >
              <Plus className="w-4 h-4" />
              Add New
            </Link>
          )}
        </div>

        {/* Search */}
        <div className="mb-4 relative">
          <div className="relative">
            <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-gray-400" />
            <input
              type="text"
              placeholder="Search by title or author..."
              value={searchTerm}
              onChange={(e) => handleSearchChange(e.target.value)}
              className="w-full sm:w-80 pl-10 pr-10 py-2 border border-gray-300 rounded-md text-sm focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-blue-500"
              data-testid="search-input"
            />
            {searchTerm && (
              <button
                onClick={() => handleSearchChange('')}
                className="absolute right-3 top-1/2 -translate-y-1/2 text-gray-400 hover:text-gray-600"
                data-testid="search-clear"
              >
                <X className="w-4 h-4" />
              </button>
            )}
          </div>
          {searchTerm && (
            <p className="mt-1 text-xs text-gray-500" data-testid="filter-count">
              Showing {totalFiltered} of {totalAll}
            </p>
          )}
        </div>

        {/* Table */}
        <div className="bg-white rounded-lg shadow overflow-hidden">
          <div className="overflow-x-auto">
          <table className="min-w-full divide-y divide-gray-200">
            <thead className="bg-gray-50">
              <tr>
                <th
                  className="px-4 sm:px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider cursor-pointer select-none hover:text-gray-700"
                  onClick={() => handleSort('title')}
                >
                  <span className="inline-flex items-center">
                    Title
                    <SortIcon column="title" />
                  </span>
                </th>
                {hasAuthor && (
                  <th
                    className="hidden md:table-cell px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider cursor-pointer select-none hover:text-gray-700"
                    onClick={() => handleSort('author')}
                  >
                    <span className="inline-flex items-center">
                      Author
                      <SortIcon column="author" />
                    </span>
                  </th>
                )}
                <th
                  className="hidden sm:table-cell px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider cursor-pointer select-none hover:text-gray-700"
                  onClick={() => handleSort('modified')}
                >
                  <span className="inline-flex items-center">
                    Last Modified
                    <SortIcon column="modified" />
                  </span>
                </th>
                <th className="px-4 sm:px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Actions
                </th>
              </tr>
            </thead>
            <tbody className="bg-white divide-y divide-gray-200">
              {pageItems.map((item) => {
                const entity = hasParentChild ? (item as TreeNode).entity : (item as PeekEntity);
                const depth = hasParentChild ? (item as TreeNode).depth : 0;
                const hasChildren = hasParentChild ? (item as TreeNode).children.length > 0 : false;
                const isExpanded = expanded.has(entity.id);

                return (
                <tr key={entity.id} className="hover:bg-gray-50 transition-colors" data-testid={`entity-row-${entity.id}`} data-depth={depth}>
                  <td className="px-4 sm:px-6 py-4 whitespace-nowrap">
                    <div className="flex items-center" style={depth > 0 ? { paddingLeft: `${depth * 24}px` } : undefined}>
                      {hasParentChild && (
                        <button
                          type="button"
                          onClick={() => toggleExpanded(entity.id)}
                          className={`mr-1 p-0.5 text-gray-400 hover:text-gray-600 ${hasChildren ? '' : 'invisible'}`}
                          data-testid={`toggle-${entity.id}`}
                          aria-label={isExpanded ? 'Collapse' : 'Expand'}
                        >
                          {isExpanded
                            ? <ChevronDownIcon className="w-4 h-4" />
                            : <ChevronRightIcon className="w-4 h-4" />}
                        </button>
                      )}
                      <Link
                        to={isEditable
                          ? `/entities-admin/${entityName}?id=${entity.id}`
                          : `/entities-view/${entityName}?id=${entity.id}`}
                        className="text-blue-600 hover:text-blue-800 font-medium"
                      >
                        {entity.title ?? entity.name ?? `ID: ${entity.id}`}
                      </Link>
                    </div>
                  </td>
                  {hasAuthor && (
                    <td className="hidden md:table-cell px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                      {entity.author ?? '-'}
                    </td>
                  )}
                  <td className="hidden sm:table-cell px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                    {formatDate(entity.modified ?? entity.modified_gmt)}
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
                      {isEditable && (
                        <>
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
                        </>
                      )}
                    </div>
                  </td>
                </tr>
              )})}
              {pageItems.length === 0 && (
                <tr>
                  <td colSpan={hasAuthor ? 4 : 3} className="px-6 py-8 text-center text-gray-500">
                    {searchTerm
                      ? `No results for "${searchTerm}"`
                      : <>No {schema?.readable_name.plural.toLowerCase() ?? 'entities'} found.
                        {isEditable && (
                          <Link
                            to={`/entities-admin/${entityName}?id=new`}
                            className="ml-2 text-blue-600 hover:text-blue-800"
                          >
                            Create one?
                          </Link>
                        )}</>}
                  </td>
                </tr>
              )}
            </tbody>
          </table>
          </div>

          {/* Pagination controls */}
          {totalPages > 1 && (
            <div className="flex items-center justify-between px-4 sm:px-6 py-3 border-t border-gray-200 bg-gray-50">
              <div className="text-sm text-gray-500">
                Page {currentPage + 1} of {totalPages}
                {searchTerm && <span className="ml-2">({totalFiltered} results)</span>}
              </div>
              <div className="flex items-center gap-2">
                <button
                  onClick={() => setCurrentPage(p => Math.max(0, p - 1))}
                  disabled={currentPage === 0}
                  className="inline-flex items-center gap-1 px-3 py-1.5 text-sm border border-gray-300 rounded-md bg-white hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
                >
                  <ChevronLeft className="w-4 h-4" />
                  Previous
                </button>
                <button
                  onClick={() => setCurrentPage(p => Math.min(totalPages - 1, p + 1))}
                  disabled={currentPage >= totalPages - 1}
                  className="inline-flex items-center gap-1 px-3 py-1.5 text-sm border border-gray-300 rounded-md bg-white hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
                >
                  Next
                  <ChevronRight className="w-4 h-4" />
                </button>
              </div>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
