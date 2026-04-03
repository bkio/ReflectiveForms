import { useState, useRef, useEffect, useCallback, useMemo } from 'react';
import { ChevronDown, Search, X } from 'lucide-react';

export interface ChoiceOption {
  value: string;
  label: string;
}

interface SearchableChoicesSelectProps {
  choices: ChoiceOption[];
  value: string;
  onChange: (value: string) => void;
  disabled?: boolean;
  placeholder?: string;
  hasError?: boolean;
}

export function SearchableChoicesSelect({
  choices,
  value,
  onChange,
  disabled = false,
  placeholder = '-- Select --',
  hasError = false,
}: SearchableChoicesSelectProps) {
  const [isOpen, setIsOpen] = useState(false);
  const [search, setSearch] = useState('');
  const containerRef = useRef<HTMLDivElement>(null);
  const searchInputRef = useRef<HTMLInputElement>(null);
  const listRef = useRef<HTMLDivElement>(null);
  const [highlightedIndex, setHighlightedIndex] = useState(-1);
  const [dropUp, setDropUp] = useState(false);

  // Filter choices by search term (client-side)
  const filteredChoices = useMemo(() => {
    if (!search.trim()) return choices;
    const lower = search.toLowerCase();
    return choices.filter((c) => c.label.toLowerCase().includes(lower));
  }, [choices, search]);

  // Get display label for current value
  const selectedLabel = useMemo(() => {
    const found = choices.find((c) => c.value === value);
    return found?.label ?? '';
  }, [choices, value]);

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

  // Determine dropdown direction and focus search input when dropdown opens
  useEffect(() => {
    if (isOpen) {
      if (containerRef.current) {
        const rect = containerRef.current.getBoundingClientRect();
        const spaceBelow = window.innerHeight - rect.bottom;
        setDropUp(spaceBelow < 300);
      }
      searchInputRef.current?.focus();
    }
  }, [isOpen]);

  // Reset highlighted index when search changes
  useEffect(() => {
    setHighlightedIndex(-1);
  }, [search]);

  const handleSelect = useCallback(
    (val: string) => {
      onChange(val);
      setIsOpen(false);
      setSearch('');
    },
    [onChange]
  );

  const handleClear = useCallback(
    (e: React.MouseEvent) => {
      e.stopPropagation();
      onChange(choices[0]?.value ?? '');
      setSearch('');
    },
    [onChange, choices]
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
            prev < filteredChoices.length - 1 ? prev + 1 : prev
          );
          break;
        case 'ArrowUp':
          e.preventDefault();
          setHighlightedIndex((prev) => (prev > 0 ? prev - 1 : 0));
          break;
        case 'Enter':
          e.preventDefault();
          if (highlightedIndex >= 0 && highlightedIndex < filteredChoices.length) {
            handleSelect(filteredChoices[highlightedIndex].value);
          }
          break;
        case 'Escape':
          setIsOpen(false);
          setSearch('');
          break;
      }
    },
    [isOpen, filteredChoices, highlightedIndex, handleSelect]
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
          ${hasError ? 'border-red-500' : ''}
        `}
        aria-haspopup="listbox"
        aria-expanded={isOpen}
        data-value={value}
      >
        <span className={selectedLabel ? 'text-gray-900' : 'text-gray-500'}>
          {selectedLabel || placeholder}
        </span>
        <div className="flex items-center gap-1 ml-2 flex-shrink-0">
          {value && value !== choices[0]?.value && !disabled && (
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
        <div className={`absolute z-50 w-full bg-white border border-gray-200 rounded-md shadow-lg ${dropUp ? 'bottom-full mb-1' : 'mt-1'}`}>
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
            className="max-h-60 overflow-y-auto"
            role="listbox"
          >
            {filteredChoices.length === 0 ? (
              <div className="px-3 py-4 text-sm text-gray-500 text-center">
                No matches found
              </div>
            ) : (
              filteredChoices.map((choice, idx) => {
                const isSelected = choice.value === value;
                const isHighlighted = idx === highlightedIndex;
                return (
                  <div
                    key={choice.value}
                    data-option
                    data-value={choice.value}
                    onClick={() => handleSelect(choice.value)}
                    className={`
                      px-3 py-2 text-sm cursor-pointer
                      ${isHighlighted ? 'bg-blue-50' : 'hover:bg-gray-50'}
                      ${isSelected ? 'font-medium text-blue-600' : 'text-gray-900'}
                    `}
                    role="option"
                    aria-selected={isSelected}
                  >
                    {choice.label}
                  </div>
                );
              })
            )}
          </div>
        </div>
      )}
    </div>
  );
}
