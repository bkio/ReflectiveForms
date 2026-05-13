import { useParams, Link, Navigate } from 'react-router-dom';
import { Trash2, Edit, Copy, Plus, ChevronLeft, ChevronRight, Eye, Search, X, ArrowUp, ArrowDown, ArrowUpDown, ChevronDown as ChevronDownIcon, ChevronRight as ChevronRightIcon, Filter, Lock, Sparkles, ShieldCheck } from 'lucide-react';
import { useSchema, useAllSchemas, useEntityList, useDeleteEntity, useCapabilities } from '../hooks/useEntity';
import { useLockedEntities } from '../hooks/useLockedEntities';
import { toast } from 'sonner';
import { useMemo, useState, useCallback, useRef, useEffect } from 'react';
import { PeekEntity } from '../types/schema';
import { AiNaturalLanguageFilter } from '../components/ai/AiNaturalLanguageFilter';
import { useAiSemanticSearch } from '../hooks/useAi';
import { useAiAssistantOptional } from '../lib/AiAssistantContext';

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

/** Collect IDs of all nodes that have children (for expand-all). */
function collectParentIds(roots: TreeNode[]): Set<number> {
  const ids = new Set<number>();
  function walk(nodes: TreeNode[]) {
    for (const node of nodes) {
      if (node.children.length > 0) {
        ids.add(node.entity.id);
        walk(node.children);
      }
    }
  }
  walk(roots);
  return ids;
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
  const { data: allSchemas } = useAllSchemas();

  const [currentPage, setCurrentPage] = useState(0);
  const [searchTerm, setSearchTerm] = useState('');
  const [sortConfig, setSortConfig] = useState<SortConfig>({ column: 'modified', direction: 'desc' });
  const [expanded, setExpanded] = useState<Set<number>>(new Set());
  const [selectedAuthors, setSelectedAuthors] = useState<Set<string>>(new Set());
  const [selectedCategories, setSelectedCategories] = useState<Set<string>>(new Set());
  const [selectedTags, setSelectedTags] = useState<Set<string>>(new Set());

  const { data: schema, isLoading: schemaLoading } = useSchema(entityName ?? '');
  const { data: allEntities, isLoading: entitiesLoading } = useEntityList(entityName ?? '');
  const { data: capabilities, isSuccess: capabilitiesLoaded } = useCapabilities();
  const deleteMutation = useDeleteEntity(entityName ?? '');
  const lockedEntities = useLockedEntities(entityName ?? '');

  const hasAuthor = schema?.features.has_author ?? false;
  const hasParentChild = schema?.features.has_parent_child ?? false;
  const hasTags = schema?.features.has_tags ?? false;
  const hasCategories = schema?.features.has_categories ?? false;

  // AI feature flags
  const supportsAiGeneration = schema?.features.supports_ai_generation ?? false;
  const supportsNlFilter = schema?.features.supports_natural_language_filter ?? false;
  const supportsSemanticSearch = schema?.features.supports_semantic_search ?? false;
  const [nlFilterDescription, setNlFilterDescription] = useState<string | null>(null);
  const [nlFilterEntityIds, setNlFilterEntityIds] = useState<Set<number> | null>(null);
  const [nlUsedVectorFallback, setNlUsedVectorFallback] = useState(false);
  const [semanticSearchActive, setSemanticSearchActive] = useState(false);
  const [semanticQuery, setSemanticQuery] = useState('');
  const [debouncedSemanticQuery, setDebouncedSemanticQuery] = useState('');
  const semanticTimerRef = useRef<ReturnType<typeof setTimeout>>();
  const [showAiCreateDialog, setShowAiCreateDialog] = useState(false);
  const [aiCreatePrompt, setAiCreatePrompt] = useState('');
  const [aiCreateGenerating, setAiCreateGenerating] = useState(false);

  // Semantic search hook
  const { data: semanticResults, isLoading: semanticLoading } = useAiSemanticSearch(
    debouncedSemanticQuery,
    entityName,
    semanticSearchActive ? schema : null,
  );

  // AI assistant context push
  const assistant = useAiAssistantOptional();
  useEffect(() => {
    assistant?.setContext({
      current_page: 'entity-list',
      entity_type: entityName,
      entity_id: undefined,
      current_fields: undefined,
      errors: undefined,
      selected_field: undefined,
    });
  }, [entityName, assistant]);

  const handleSemanticQueryChange = useCallback((value: string) => {
    setSemanticQuery(value);
    clearTimeout(semanticTimerRef.current);
    semanticTimerRef.current = setTimeout(() => setDebouncedSemanticQuery(value), 300);
  }, []);

  // Extract unique filter values from all entities
  const uniqueAuthors = useMemo(() => {
    if (!allEntities || !hasAuthor) return [];
    const set = new Set<string>();
    for (const e of allEntities) {
      if (e.author) set.add(e.author);
    }
    return [...set].sort((a, b) => a.localeCompare(b));
  }, [allEntities, hasAuthor]);

  const uniqueCategories = useMemo(() => {
    if (!allEntities || !hasCategories) return [];
    const set = new Set<string>();
    for (const e of allEntities) {
      if (e.categories) for (const c of e.categories) set.add(c);
    }
    return [...set].sort((a, b) => a.localeCompare(b));
  }, [allEntities, hasCategories]);

  const uniqueTags = useMemo(() => {
    if (!allEntities || !hasTags) return [];
    const set = new Set<string>();
    for (const e of allEntities) {
      if (e.tags) for (const t of e.tags) set.add(t);
    }
    return [...set].sort((a, b) => a.localeCompare(b));
  }, [allEntities, hasTags]);

  const hasActiveFilters = selectedAuthors.size > 0 || selectedCategories.size > 0 || selectedTags.size > 0;

  const clearAllFilters = useCallback(() => {
    setSelectedAuthors(new Set());
    setSelectedCategories(new Set());
    setSelectedTags(new Set());
    setCurrentPage(0);
  }, []);

  // Filter → Sort → Paginate pipeline
  const filteredAndSorted = useMemo(() => {
    if (!allEntities) return [];
    let result = [...allEntities];

    // NL filter — restrict to matched entity IDs
    if (nlFilterEntityIds) {
      result = result.filter(e => nlFilterEntityIds.has(e.id));
    }

    // Text search filter
    if (searchTerm.trim()) {
      const term = searchTerm.trim().toLowerCase();
      result = result.filter(e => {
        const title = (e.title ?? e.name ?? '').toLowerCase();
        const author = (e.author ?? '').toLowerCase();
        return title.includes(term) || author.includes(term);
      });
    }

    // Author filter
    if (selectedAuthors.size > 0) {
      result = result.filter(e => e.author != null && selectedAuthors.has(e.author));
    }

    // Category filter (entity matches if it has ANY of the selected categories)
    if (selectedCategories.size > 0) {
      result = result.filter(e =>
        e.categories != null && e.categories.some(c => selectedCategories.has(c))
      );
    }

    // Tag filter (entity matches if it has ANY of the selected tags)
    if (selectedTags.size > 0) {
      result = result.filter(e =>
        e.tags != null && e.tags.some(t => selectedTags.has(t))
      );
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
  }, [allEntities, searchTerm, sortConfig, selectedAuthors, selectedCategories, selectedTags, nlFilterEntityIds]);

  // Build tree for hierarchical entities
  const entityTree = useMemo(() => {
    if (!hasParentChild) return null;
    return buildEntityTree(filteredAndSorted);
  }, [hasParentChild, filteredAndSorted]);

  // Expand all parent nodes by default when tree data loads
  const hasSeededExpanded = useRef(false);
  useEffect(() => {
    if (entityTree && !hasSeededExpanded.current) {
      const parentIds = collectParentIds(entityTree);
      if (parentIds.size > 0) {
        setExpanded(parentIds);
      }
      hasSeededExpanded.current = true;
    }
  }, [entityTree]);

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

  // Entities with individual sharing have their own dedicated pages — redirect after all hooks
  const entitySchema = entityName ? allSchemas?.[entityName] : undefined;
  if (entitySchema?.features.has_individual_sharing && entitySchema.features.custom_frontend_list_route) {
    return <Navigate to={entitySchema.features.custom_frontend_list_route} replace />;
  }

  if (schemaLoading || entitiesLoading) {
    return (
      <div className="flex items-center justify-center min-h-[400px]">
        <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-blue-600"></div>
      </div>
    );
  }

  if (!schema) {
    return (
      <div className="flex items-center justify-center min-h-[400px]">
        <div className="text-center">
          <h2 className="text-lg font-semibold text-gray-900 dark:text-gray-100">Entity type not found</h2>
          <p className="mt-1 text-sm text-gray-500 dark:text-gray-400">
            The entity type <code className="px-1.5 py-0.5 bg-gray-100 dark:bg-gray-700 rounded text-sm">{entityName}</code> does not exist.
          </p>
          <Link to="/" className="mt-4 inline-block text-sm text-blue-600 hover:text-blue-700 dark:text-blue-400">
            ← Back to Dashboard
          </Link>
        </div>
      </div>
    );
  }

  const isEditable = schema?.features.supports_frontend_edit ?? true;
  // Only restrict when capabilities have actually loaded successfully with explicit false
  const caps = (capabilitiesLoaded && entityName) ? capabilities?.[entityName] : undefined;
  const canCreate = isEditable && (caps?.can_create ?? true);
  const canRead = caps?.can_read ?? true;
  const canUpdate = isEditable && (caps?.can_update ?? true);
  const canDelete = isEditable && (caps?.can_delete ?? true);

  function SortIcon({ column }: { column: SortColumn }) {
    if (sortConfig.column !== column) return <ArrowUpDown className="w-3.5 h-3.5 ml-1 text-gray-400" />;
    return sortConfig.direction === 'asc'
      ? <ArrowUp className="w-3.5 h-3.5 ml-1 text-blue-600" />
      : <ArrowDown className="w-3.5 h-3.5 ml-1 text-blue-600" />;
  }

  return (
    <div>
      <div className="max-w-6xl mx-auto py-8 px-4">
        {/* Header */}
        <div className="flex justify-between items-center mb-6">
          <div>
            <h1 className="text-2xl font-bold text-gray-900">
              {schema?.readable_name.plural ?? entityName}
            </h1>
            <p className="mt-1 text-sm text-gray-500">{totalAll} total</p>
          </div>
          {canCreate && (
            <div className="flex items-center gap-2">
              {supportsAiGeneration && assistant && (
                <button
                  onClick={() => {
                    setAiCreatePrompt('');
                    setShowAiCreateDialog(true);
                  }}
                  className="flex items-center gap-2 px-4 py-2 bg-purple-600 text-white rounded-md hover:bg-purple-700 transition-colors"
                  data-testid="ai-generate-button"
                >
                  <Sparkles className="w-4 h-4" />
                  Create with AI
                </button>
              )}
              <Link
                to={`/entities-admin/${entityName}?id=new`}
                className="flex items-center gap-2 px-4 py-2 bg-blue-600 text-white rounded-md hover:bg-blue-700 transition-colors"
              >
                <Plus className="w-4 h-4" />
                Add New
              </Link>
            </div>
          )}
        </div>

        {/* AI Natural Language Filter */}
        {supportsNlFilter && caps?.can_peek_all && (
          <div className="mb-4">
            <AiNaturalLanguageFilter
              entityName={entityName!}
              onFilterApplied={(result) => {
                setNlFilterDescription(result.natural_language_interpretation);
                setNlFilterEntityIds(new Set(result.results.map(r => r.id)));
                setNlUsedVectorFallback(result.used_vector_fallback ?? false);
                setCurrentPage(0);
              }}
              onFilterCleared={() => {
                setNlFilterDescription(null);
                setNlFilterEntityIds(null);
                setNlUsedVectorFallback(false);
              }}
            />
            {nlFilterDescription && (
              <p className={`mt-1 text-xs ${nlUsedVectorFallback ? 'text-amber-600 dark:text-amber-400' : 'text-purple-600 dark:text-purple-400'}`} data-testid="nl-filter-description">
                {nlUsedVectorFallback && <span className="font-medium">Semantic fallback: </span>}
                {nlFilterDescription}
              </p>
            )}
          </div>
        )}

        {/* Search */}
        <div className="mb-4 relative">
          <div className="flex items-center gap-2">
            <div className="relative flex-1 sm:flex-none">
              <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-gray-400" />
              <input
                type="text"
                placeholder={semanticSearchActive ? 'AI semantic search...' : 'Search by title or author...'}
                value={semanticSearchActive ? semanticQuery : searchTerm}
                onChange={(e) => semanticSearchActive ? handleSemanticQueryChange(e.target.value) : handleSearchChange(e.target.value)}
                className={`w-full sm:w-80 pl-10 pr-10 py-2 border rounded-md text-sm focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-blue-500 ${semanticSearchActive ? 'border-purple-300 bg-purple-50/50' : 'border-gray-300'}`}
                data-testid="search-input"
              />
              {(semanticSearchActive ? semanticQuery : searchTerm) && (
                <button
                  onClick={() => semanticSearchActive ? handleSemanticQueryChange('') : handleSearchChange('')}
                  className="absolute right-3 top-1/2 -translate-y-1/2 text-gray-400 hover:text-gray-600"
                  data-testid="search-clear"
                >
                  <X className="w-4 h-4" />
                </button>
              )}
            </div>
            {supportsSemanticSearch && caps?.can_peek_all && (
              <button
                onClick={() => {
                  setSemanticSearchActive((prev) => !prev);
                  setSemanticQuery('');
                  setDebouncedSemanticQuery('');
                }}
                className={`inline-flex items-center gap-1.5 px-3 py-2 text-sm rounded-md border transition-colors ${
                  semanticSearchActive
                    ? 'bg-purple-100 border-purple-300 text-purple-700 dark:bg-purple-900/30 dark:border-purple-600 dark:text-purple-300'
                    : 'border-gray-300 text-gray-600 hover:bg-gray-50 dark:border-gray-600 dark:text-gray-400 dark:hover:bg-gray-700'
                }`}
                title={semanticSearchActive ? 'Switch to text search' : 'Switch to AI semantic search'}
                data-testid="semantic-search-toggle"
              >
                <Sparkles className="w-4 h-4" />
                AI
              </button>
            )}
          </div>
          {semanticSearchActive && semanticLoading && (
            <p className="mt-1 text-xs text-purple-500" data-testid="semantic-search-loading">Searching...</p>
          )}
          {semanticSearchActive && semanticResults && semanticResults.length > 0 && (
            <div className="mt-2 bg-white dark:bg-gray-800 border border-purple-200 dark:border-purple-700 rounded-md shadow-sm" data-testid="semantic-search-results">
              {semanticResults.map((r) => (
                <Link
                  key={`${r.entity_name}-${r.entity_id}`}
                  to={`/entities-view/${r.entity_name}?id=${r.entity_id}`}
                  className="flex items-center justify-between px-3 py-2 text-sm hover:bg-purple-50 dark:hover:bg-purple-900/20 border-b border-gray-100 dark:border-gray-700 last:border-b-0"
                >
                  <span className="text-gray-900 dark:text-gray-100">{r.title}</span>
                  <span className="text-xs text-gray-400 tabular-nums">{Math.round(r.score * 100)}%</span>
                </Link>
              ))}
            </div>
          )}
          {semanticSearchActive && semanticResults && semanticResults.length === 0 && debouncedSemanticQuery && !semanticLoading && (
            <p className="mt-1 text-xs text-gray-500" data-testid="semantic-search-empty">No semantic matches found.</p>
          )}
          {!semanticSearchActive && (searchTerm || hasActiveFilters) && (
            <p className="mt-1 text-xs text-gray-500" data-testid="filter-count">
              Showing {totalFiltered} of {totalAll}
            </p>
          )}
        </div>

        {/* Filters */}
        {(hasAuthor || hasCategories || hasTags) && (
          <div className="mb-4 flex flex-wrap items-center gap-3" data-testid="filter-bar">
            <Filter className="w-4 h-4 text-gray-400 flex-shrink-0" />

            {hasAuthor && uniqueAuthors.length > 0 && (
              <FilterDropdown
                label="Author"
                options={uniqueAuthors}
                selected={selectedAuthors}
                onChange={(v) => { setSelectedAuthors(v); setCurrentPage(0); }}
                testId="filter-author"
              />
            )}

            {hasCategories && uniqueCategories.length > 0 && (
              <FilterDropdown
                label="Category"
                options={uniqueCategories}
                selected={selectedCategories}
                onChange={(v) => { setSelectedCategories(v); setCurrentPage(0); }}
                testId="filter-category"
              />
            )}

            {hasTags && uniqueTags.length > 0 && (
              <FilterDropdown
                label="Tag"
                options={uniqueTags}
                selected={selectedTags}
                onChange={(v) => { setSelectedTags(v); setCurrentPage(0); }}
                testId="filter-tag"
              />
            )}

            {hasActiveFilters && (
              <button
                onClick={clearAllFilters}
                className="text-xs text-gray-500 hover:text-gray-700 underline"
                data-testid="filter-clear-all"
              >
                Clear filters
              </button>
            )}
          </div>
        )}

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
                const lockInfo = lockedEntities.get(entity.id);
                const isSystemManaged = entity.is_system_managed === true;

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
                        to={isSystemManaged
                          ? `/entities-view/${entityName}?id=${entity.id}`
                          : canUpdate && !lockInfo
                          ? `/entities-admin/${entityName}?id=${entity.id}`
                          : canRead
                          ? `/entities-view/${entityName}?id=${entity.id}`
                          : `#`}
                        className="text-blue-600 hover:text-blue-800 font-medium"
                      >
                        {entity.title ?? entity.name ?? `ID: ${entity.id}`}
                      </Link>
                      {isSystemManaged && (
                        <span
                          className="inline-flex items-center gap-1 ml-2 px-2 py-0.5 text-xs font-medium text-blue-700 bg-blue-50 border border-blue-200 rounded-full"
                          title="This entity is managed by the system and cannot be modified"
                          data-testid={`system-badge-${entity.id}`}
                        >
                          <ShieldCheck className="w-3 h-3" />
                          System
                        </span>
                      )}
                      {lockInfo && (
                        <span
                          className="inline-flex items-center gap-1 ml-2 px-2 py-0.5 text-xs font-medium text-amber-700 bg-amber-50 border border-amber-200 rounded-full"
                          title={`Being edited by ${lockInfo.locked_by_user_name ?? 'another user'}`}
                          data-testid={`lock-badge-${entity.id}`}
                        >
                          <Lock className="w-3 h-3" />
                          {lockInfo.locked_by_user_name ?? 'Locked'}
                        </span>
                      )}
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
                      {canRead && (
                        <Link
                          to={`/entities-view/${entityName}?id=${entity.id}`}
                          className="p-2 text-gray-500 hover:text-purple-600 rounded-md hover:bg-purple-50 transition-colors"
                          title="View"
                        >
                          <Eye className="w-4 h-4" />
                        </Link>
                      )}
                      {canUpdate && !isSystemManaged && (
                        lockInfo ? (
                          <span
                            className="p-2 text-gray-300 cursor-not-allowed"
                            title={`Locked by ${lockInfo.locked_by_user_name ?? 'another user'}`}
                          >
                            <Edit className="w-4 h-4" />
                          </span>
                        ) : (
                          <Link
                            to={`/entities-admin/${entityName}?id=${entity.id}`}
                            className="p-2 text-gray-500 hover:text-blue-600 rounded-md hover:bg-blue-50 transition-colors"
                            title="Edit"
                          >
                            <Edit className="w-4 h-4" />
                          </Link>
                        )
                      )}
                      {canCreate && !isSystemManaged && (
                          <Link
                            to={`/entities-admin/${entityName}?id=clone_from_${entity.id}`}
                            className="p-2 text-gray-500 hover:text-green-600 rounded-md hover:bg-green-50 transition-colors"
                            title="Clone"
                          >
                            <Copy className="w-4 h-4" />
                          </Link>
                      )}
                      {canDelete && !isSystemManaged && (
                          lockInfo ? (
                            <span
                              className="p-2 text-gray-300 cursor-not-allowed"
                              title={`Locked by ${lockInfo.locked_by_user_name ?? 'another user'}`}
                            >
                              <Trash2 className="w-4 h-4" />
                            </span>
                          ) : (
                            <button
                              onClick={() => handleDelete(entity.id, entity.title ?? entity.name ?? '')}
                              className="p-2 text-gray-500 hover:text-red-600 rounded-md hover:bg-red-50 transition-colors"
                              title="Delete"
                            >
                              <Trash2 className="w-4 h-4" />
                            </button>
                          )
                      )}
                    </div>
                  </td>
                </tr>
              )})}
              {pageItems.length === 0 && (
                <tr>
                  <td colSpan={hasAuthor ? 4 : 3} className="px-6 py-8 text-center text-gray-500">
                    {(searchTerm || hasActiveFilters)
                      ? `No results matching current filters`
                      : <>No {schema?.readable_name.plural.toLowerCase() ?? 'entities'} found.
                        {canCreate && (
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
                {(searchTerm || hasActiveFilters) && <span className="ml-2">({totalFiltered} results)</span>}
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
      {/* AI Create dialog */}
      {showAiCreateDialog && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50" data-testid="ai-generate-dialog">
          <div className="bg-white dark:bg-gray-800 rounded-xl shadow-2xl w-full max-w-md mx-4 p-6">
            <div className="flex items-center gap-2 mb-4">
              <Sparkles className="w-5 h-5 text-purple-600" />
              <h2 className="text-lg font-semibold text-gray-900 dark:text-gray-100">
                Create {schema?.readable_name.singular ?? entityName} with AI
              </h2>
            </div>
            <p className="text-sm text-gray-500 dark:text-gray-400 mb-4">
              Describe the {schema?.readable_name.singular.toLowerCase() ?? 'entity'} you want to create. The AI will generate content based on your description.
            </p>
            <textarea
              value={aiCreatePrompt}
              onChange={e => setAiCreatePrompt(e.target.value)}
              placeholder={`e.g. "A blog post about renewable energy trends in 2026"`}
              rows={3}
              className="w-full px-3 py-2 text-sm border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100 placeholder-gray-400 resize-none focus:outline-none focus:ring-2 focus:ring-purple-500"
              autoFocus
              onKeyDown={e => {
                if (e.key === 'Enter' && !e.shiftKey && aiCreatePrompt.trim() && !aiCreateGenerating) {
                  e.preventDefault();
                  setShowAiCreateDialog(false);
                  assistant!.triggerMessage(
                    `I want to create a new ${schema!.readable_name.singular}. Here is what I want: ${aiCreatePrompt.trim()}`,
                    { current_page: 'entity-list', entity_type: entityName },
                  );
                }
              }}
              data-testid="ai-generate-prompt"
            />
            <div className="flex justify-end gap-2 mt-4">
              <button
                onClick={() => setShowAiCreateDialog(false)}
                className="px-4 py-2 text-sm font-medium text-gray-700 dark:text-gray-300 bg-gray-100 dark:bg-gray-700 rounded-md hover:bg-gray-200 dark:hover:bg-gray-600 transition-colors"
              >
                Cancel
              </button>
              <button
                onClick={async () => {
                  if (!aiCreatePrompt.trim() || aiCreateGenerating) return;
                  setAiCreateGenerating(true);
                  await assistant!.triggerMessage(
                    `I want to create a new ${schema!.readable_name.singular}. Here is what I want: ${aiCreatePrompt.trim()}`,
                    { current_page: 'entity-list', entity_type: entityName },
                  );
                  setAiCreateGenerating(false);
                  setShowAiCreateDialog(false);
                }}
                disabled={!aiCreatePrompt.trim() || aiCreateGenerating}
                className="flex items-center gap-2 px-4 py-2 text-sm font-medium text-white bg-purple-600 rounded-md hover:bg-purple-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
                data-testid="ai-generate-submit"
              >
                <Sparkles className="w-4 h-4" />
                {aiCreateGenerating ? 'Generating…' : 'Generate'}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

// ---- Filter dropdown component ----

interface FilterDropdownProps {
  label: string;
  options: string[];
  selected: Set<string>;
  onChange: (next: Set<string>) => void;
  testId: string;
}

function FilterDropdown({ label, options, selected, onChange, testId }: FilterDropdownProps) {
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);

  // Close on outside click
  useEffect(() => {
    if (!open) return;
    const handler = (e: MouseEvent) => {
      if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false);
    };
    document.addEventListener('mousedown', handler);
    return () => document.removeEventListener('mousedown', handler);
  }, [open]);

  const toggle = (value: string) => {
    const next = new Set(selected);
    if (next.has(value)) next.delete(value); else next.add(value);
    onChange(next);
  };

  const count = selected.size;

  return (
    <div className="relative" ref={ref} data-testid={testId}>
      <button
        type="button"
        onClick={() => setOpen(o => !o)}
        className={`
          inline-flex items-center gap-1.5 px-3 py-1.5 text-sm border rounded-md transition-colors
          ${count > 0
            ? 'border-blue-300 bg-blue-50 text-blue-700 hover:bg-blue-100'
            : 'border-gray-300 bg-white text-gray-700 hover:bg-gray-50'}
        `}
        data-testid={`${testId}-toggle`}
      >
        {label}
        {count > 0 && (
          <span className="inline-flex items-center justify-center w-5 h-5 text-xs font-medium bg-blue-600 text-white rounded-full">
            {count}
          </span>
        )}
        <ChevronDownIcon className="w-3.5 h-3.5" />
      </button>

      {open && (
        <div className="absolute left-0 mt-1 w-56 bg-white border border-gray-200 rounded-md shadow-lg z-20 max-h-60 overflow-y-auto" data-testid={`${testId}-menu`}>
          {options.map(opt => (
            <label
              key={opt}
              className="flex items-center gap-2 px-3 py-2 hover:bg-gray-50 cursor-pointer text-sm text-gray-700"
            >
              <input
                type="checkbox"
                checked={selected.has(opt)}
                onChange={() => toggle(opt)}
                className="rounded border-gray-300 text-blue-600 focus:ring-blue-500"
                data-testid={`${testId}-option-${opt}`}
              />
              <span className="truncate">{opt}</span>
            </label>
          ))}
        </div>
      )}
    </div>
  );
}
