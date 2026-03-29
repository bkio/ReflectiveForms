import { useState, useRef, useEffect, useCallback, useMemo } from 'react';
import { ChevronDown, Search, Loader2, X } from 'lucide-react';
import { usePaginatedEntityList } from '../../hooks/useEntity';
import { PeekEntity } from '../../types/schema';

interface SearchableSelectProps {
  entityName: string;
  value: number;
  onChange: (value: number) => void;
  disabled?: boolean;
  placeholder?: string;
  pageSize?: number;
}

export function SearchableSelect({
  entityName,
  value,
  onChange,
  disabled = false,
  placeholder = '-- Select --',
  pageSize = 20,
}: SearchableSelectProps) {
  const [isOpen, setIsOpen] = useState(false);
  const [search, setSearch] = useState('');
  const containerRef = useRef<HTMLDivElement>(null);
  const searchInputRef = useRef<HTMLInputElement>(null);
  const listRef = useRef<HTMLDivElement>(null);
  const [highlightedIndex, setHighlightedIndex] = useState(-1);

  const {
    data,
    fetchNextPage,
    hasNextPage,
    isFetchingNextPage,
    isLoading,
  } = usePaginatedEntityList(entityName, pageSize);

  // Flatten all loaded pages into a single array of entities
  const allEntities: PeekEntity[] = useMemo(
    () => data?.pages.flatMap((page) => page.items) ?? [],
    [data]
  );

  const totalCount = data?.pages[0]?.total_count ?? null;

  // Filter entities by search term (client-side)
  const filteredEntities = useMemo(() => {
    if (!search.trim()) return allEntities;
    const lower = search.toLowerCase();
    return allEntities.filter((e) => {
      const label = e.title ?? e.name ?? `ID: ${e.id}`;
      return label.toLowerCase().includes(lower);
    });
  }, [allEntities, search]);

  // Get display label for current value
  const selectedLabel = useMemo(() => {
    if (value <= 0) return '';
    const found = allEntities.find((e) => e.id === value);
    return found ? (found.title ?? found.name ?? `ID: ${found.id}`) : `ID: ${value}`;
  }, [allEntities, value]);

  // Close dropdown on outside click
  useEffect(() => {
    const handleClickOutside = (e: MouseEvent) => {
      if (containerRef.current && !containerRef.current.contains(e.target as Node)) {
        setIsOpen(false);
      }
    };
    document.addEventListener('mousedown', handleClickOutside);
    return () => document.removeEventListener('mousedown', handleClickOutside);
  }, []);

  // Focus search input when dropdown opens
  useEffect(() => {
    if (isOpen && searchInputRef.current) {
      searchInputRef.current.focus();
    }
  }, [isOpen]);

  // Reset highlighted index when search changes
  useEffect(() => {
    setHighlightedIndex(-1);
  }, [search]);

  // Infinite scroll: load more when scrolled near bottom
  const handleScroll = useCallback(() => {
    const el = listRef.current;
    if (!el || !hasNextPage || isFetchingNextPage) return;
    if (el.scrollTop + el.clientHeight >= el.scrollHeight - 40) {
      fetchNextPage();
    }
  }, [fetchNextPage, hasNextPage, isFetchingNextPage]);

  const handleSelect = useCallback(
    (id: number) => {
      onChange(id);
      setIsOpen(false);
      setSearch('');
    },
    [onChange]
  );

  const handleClear = useCallback(
    (e: React.MouseEvent) => {
      e.stopPropagation();
      onChange(-1);
      setSearch('');
    },
    [onChange]
  );

  // Keyboard navigation
  const handleKeyDown = useCallback(
    (e: React.KeyboardEvent) => {
      if (!isOpen) {
        if (e.key === 'ArrowDown' || e.key === 'Enter' || e.key === ' ') {
          e.preventDefault();
          setIsOpen(true);
        }
        return;
      }

      switch (e.key) {
        case 'ArrowDown':
          e.preventDefault();
          setHighlightedIndex((prev) =>
            prev < filteredEntities.length - 1 ? prev + 1 : prev
          );
          break;
        case 'ArrowUp':
          e.preventDefault();
          setHighlightedIndex((prev) => (prev > 0 ? prev - 1 : 0));
          break;
        case 'Enter':
          e.preventDefault();
          if (highlightedIndex >= 0 && highlightedIndex < filteredEntities.length) {
            handleSelect(filteredEntities[highlightedIndex].id);
          }
          break;
        case 'Escape':
          setIsOpen(false);
          setSearch('');
          break;
      }
    },
    [isOpen, filteredEntities, highlightedIndex, handleSelect]
  );

  // Scroll highlighted item into view
  useEffect(() => {
    if (highlightedIndex < 0 || !listRef.current) return;
    const items = listRef.current.querySelectorAll('[data-option]');
    items[highlightedIndex]?.scrollIntoView({ block: 'nearest' });
  }, [highlightedIndex]);

  return (
    <div ref={containerRef} className="relative" onKeyDown={handleKeyDown}>
      {/* Trigger button */}
      <button
        type="button"
        onClick={() => !disabled && setIsOpen(!isOpen)}
        disabled={disabled}
        className={`
          w-full flex items-center justify-between px-3 py-2
          border rounded-md shadow-sm text-left
          focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-blue-500
          ${disabled ? 'bg-gray-100 cursor-not-allowed border-gray-300' : 'bg-white border-gray-300 hover:border-gray-400'}
        `}
        aria-haspopup="listbox"
        aria-expanded={isOpen}
      >
        <span className={selectedLabel ? 'text-gray-900' : 'text-gray-500'}>
          {selectedLabel || placeholder}
        </span>
        <div className="flex items-center gap-1 ml-2 flex-shrink-0">
          {value > 0 && !disabled && (
            <span
              role="button"
              tabIndex={-1}
              onClick={handleClear}
              className="p-0.5 hover:bg-gray-200 rounded"
            >
              <X className="w-3.5 h-3.5 text-gray-400" />
            </span>
          )}
          <ChevronDown className={`w-4 h-4 text-gray-400 transition-transform ${isOpen ? 'rotate-180' : ''}`} />
        </div>
      </button>

      {/* Dropdown */}
      {isOpen && (
        <div className="absolute z-50 mt-1 w-full bg-white border border-gray-200 rounded-md shadow-lg">
          {/* Search input */}
          <div className="p-2 border-b border-gray-100">
            <div className="relative">
              <Search className="absolute left-2.5 top-1/2 -translate-y-1/2 w-4 h-4 text-gray-400" />
              <input
                ref={searchInputRef}
                type="text"
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                placeholder="Search..."
                className="w-full pl-8 pr-3 py-1.5 text-sm border border-gray-200 rounded focus:outline-none focus:ring-1 focus:ring-blue-500"
              />
            </div>
          </div>

          {/* Options list */}
          <div
            ref={listRef}
            onScroll={handleScroll}
            className="max-h-60 overflow-y-auto"
            role="listbox"
          >
            {isLoading ? (
              <div className="flex items-center justify-center py-4">
                <Loader2 className="w-5 h-5 animate-spin text-gray-400" />
              </div>
            ) : filteredEntities.length === 0 ? (
              <div className="px-3 py-4 text-sm text-gray-500 text-center">
                {search ? 'No matches found' : 'No options available'}
                {search && hasNextPage && (
                  <button
                    type="button"
                    onClick={() => fetchNextPage()}
                    className="block mx-auto mt-2 text-blue-600 hover:text-blue-800 text-xs"
                  >
                    Load more to search
                  </button>
                )}
              </div>
            ) : (
              <>
                {/* Unselect option */}
                <div
                  data-option
                  onClick={() => handleSelect(-1)}
                  className={`
                    px-3 py-2 text-sm cursor-pointer
                    ${highlightedIndex === -1 ? 'bg-blue-50' : 'hover:bg-gray-50'}
                    ${value <= 0 ? 'font-medium text-blue-600' : 'text-gray-500 italic'}
                  `}
                  role="option"
                  aria-selected={value <= 0}
                >
                  {placeholder}
                </div>
                {filteredEntities.map((entity, idx) => {
                  const label = entity.title ?? entity.name ?? `ID: ${entity.id}`;
                  const isSelected = entity.id === value;
                  const isHighlighted = idx === highlightedIndex;
                  return (
                    <div
                      key={entity.id}
                      data-option
                      onClick={() => handleSelect(entity.id)}
                      className={`
                        px-3 py-2 text-sm cursor-pointer
                        ${isHighlighted ? 'bg-blue-50' : 'hover:bg-gray-50'}
                        ${isSelected ? 'font-medium text-blue-600' : 'text-gray-900'}
                      `}
                      role="option"
                      aria-selected={isSelected}
                    >
                      {label}
                    </div>
                  );
                })}

                {/* Load more / pagination info */}
                {isFetchingNextPage && (
                  <div className="flex items-center justify-center py-2">
                    <Loader2 className="w-4 h-4 animate-spin text-gray-400" />
                  </div>
                )}
                {hasNextPage && !isFetchingNextPage && (
                  <button
                    type="button"
                    onClick={() => fetchNextPage()}
                    className="w-full px-3 py-2 text-xs text-blue-600 hover:bg-blue-50 text-center"
                  >
                    Load more...
                  </button>
                )}
              </>
            )}
          </div>

          {/* Footer with count */}
          {totalCount !== null && (
            <div className="px-3 py-1.5 border-t border-gray-100 text-xs text-gray-400 text-right">
              {allEntities.length} of {totalCount} loaded
            </div>
          )}
        </div>
      )}
    </div>
  );
}
