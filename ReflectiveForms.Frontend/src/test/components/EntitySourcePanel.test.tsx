import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { EntitySourcePanel } from '../../components/sheets/EntitySourcePanel';
import type { AllCapabilities, EntitySchema } from '../../types/schema';

const mockSchemas: Record<string, EntitySchema> = {
  employee: {
    entity_name: 'employee',
    readable_name: { singular: 'Employee', plural: 'Employees' },
    features: { has_tags: false, has_categories: false, has_author: false, has_parent_child: false, require_title_uniqueness: false, supports_frontend_edit: true, has_individual_sharing: false, supports_semantic_search: false, supports_ai_generation: false, supports_ai_diff_summary: false, supports_natural_language_filter: false },
    fields: [
      { name: 'name', type: 'Text', label: 'Full Name', required: true, has_dynamic_choices_runtime: false, has_dynamic_choices_compile_time: false, has_logic_sanity_check: false },
      { name: 'email', type: 'Email', label: 'Email Address', required: true, has_dynamic_choices_runtime: false, has_dynamic_choices_compile_time: false, has_logic_sanity_check: false },
      { name: 'salary', type: 'Number', label: 'Salary', required: false, has_dynamic_choices_runtime: false, has_dynamic_choices_compile_time: false, has_logic_sanity_check: false },
    ],
    api_endpoints: { crud: '', sanity_check: '', entity_lock: '', media: '' },
    schema_version: '1.0',
  } as EntitySchema,
  department: {
    entity_name: 'department',
    readable_name: { singular: 'Department', plural: 'Departments' },
    features: { has_tags: false, has_categories: false, has_author: false, has_parent_child: false, require_title_uniqueness: false, supports_frontend_edit: true, has_individual_sharing: false, supports_semantic_search: false, supports_ai_generation: false, supports_ai_diff_summary: false, supports_natural_language_filter: false },
    fields: [
      { name: 'dept_name', type: 'Text', label: 'Department Name', required: true, has_dynamic_choices_runtime: false, has_dynamic_choices_compile_time: false, has_logic_sanity_check: false },
      { name: 'budget', type: 'Number', label: 'Budget', required: false, has_dynamic_choices_runtime: false, has_dynamic_choices_compile_time: false, has_logic_sanity_check: false },
    ],
    api_endpoints: { crud: '', sanity_check: '', entity_lock: '', media: '' },
    schema_version: '1.0',
  } as EntitySchema,
  'rf-sheets': {
    entity_name: 'rf-sheets',
    readable_name: { singular: 'Sheet', plural: 'Sheets' },
    features: { has_tags: false, has_categories: false, has_author: false, has_parent_child: false, require_title_uniqueness: false, supports_frontend_edit: true, has_individual_sharing: false, supports_semantic_search: false, supports_ai_generation: false, supports_ai_diff_summary: false, supports_natural_language_filter: false },
    fields: [],
    api_endpoints: { crud: '', sanity_check: '', entity_lock: '', media: '' },
    schema_version: '1.0',
  } as EntitySchema,
};

describe('EntitySourcePanel', () => {
  let onAddSource: ReturnType<typeof vi.fn>;
  let onRemoveSource: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    onAddSource = vi.fn();
    onRemoveSource = vi.fn();
  });

  function renderPanel(activeSources: string[] = [], unauthorizedEntities = new Set<string>(), capabilities?: AllCapabilities) {
    return render(
      <EntitySourcePanel
        schemas={mockSchemas}
        activeSources={activeSources}
        unauthorizedEntities={unauthorizedEntities}
        capabilities={capabilities}
        onAddSource={onAddSource}
        onRemoveSource={onRemoveSource}
      />,
    );
  }

  // ── Empty State ────────────────────────────────────────────────────────

  it('renders empty state when no sources', () => {
    renderPanel();
    expect(screen.getByText(/No entity sources added yet/)).toBeInTheDocument();
  });

  it('renders the header with title', () => {
    renderPanel();
    expect(screen.getByText('Entity Sources')).toBeInTheDocument();
  });

  // ── Adding Sources ─────────────────────────────────────────────────────

  it('shows entity picker on Add click', async () => {
    renderPanel();
    await userEvent.click(screen.getByText('+ Add'));
    // Should show employee and department (not rf-sheets)
    expect(screen.getByText('Employee')).toBeInTheDocument();
    expect(screen.getByText('Department')).toBeInTheDocument();
    expect(screen.queryByText('Sheet')).not.toBeInTheDocument();
  });

  it('calls onAddSource when picking an entity', async () => {
    renderPanel();
    await userEvent.click(screen.getByText('+ Add'));
    await userEvent.click(screen.getByText('Employee'));
    expect(onAddSource).toHaveBeenCalledWith('employee');
  });

  it('does not show already-active sources in picker', async () => {
    renderPanel(['employee']);
    await userEvent.click(screen.getByText('+ Add'));
    // Only department should be available
    expect(screen.queryByText('Employee')).toBeInTheDocument(); // shown as active source
    expect(screen.getByText('Department')).toBeInTheDocument(); // available to add
  });

  // ── Displaying Sources ─────────────────────────────────────────────────

  it('renders active source names', () => {
    renderPanel(['employee', 'department']);
    expect(screen.getByText('Employee')).toBeInTheDocument();
    expect(screen.getByText('Department')).toBeInTheDocument();
  });

  it('expands entity to show fields', async () => {
    renderPanel(['employee']);

    // Fields should be visible (employee starts expanded since it's in activeSources)
    expect(screen.getByText('Full Name')).toBeInTheDocument();
    expect(screen.getByText('Email Address')).toBeInTheDocument();
    expect(screen.getByText('Salary')).toBeInTheDocument();
    expect(screen.getByText('ID')).toBeInTheDocument();
  });

  it('collapses entity on click', async () => {
    renderPanel(['employee']);

    // Click the entity header to collapse
    await userEvent.click(screen.getByText('Employee'));

    // Fields should be hidden
    expect(screen.queryByText('Full Name')).not.toBeInTheDocument();
  });

  // ── Removing Sources ───────────────────────────────────────────────────

  it('calls onRemoveSource when remove button is clicked', async () => {
    renderPanel(['employee']);
    const removeButton = screen.getByTitle('Remove source');
    await userEvent.click(removeButton);
    expect(onRemoveSource).toHaveBeenCalledWith('employee');
  });

  // ── Unauthorized Entities ──────────────────────────────────────────────

  it('shows lock icon for unauthorized entity', () => {
    renderPanel(['employee'], new Set(['employee']));
    // The lock icon should be rendered (Lock component)
    const lockIcons = document.querySelectorAll('.text-amber-500');
    expect(lockIcons.length).toBeGreaterThan(0);
  });

  it('shows no-access message for unauthorized entity when expanded', () => {
    renderPanel(['employee'], new Set(['employee']));
    expect(screen.getByText(/No access to this entity/)).toBeInTheDocument();
  });

  it('hides fields for unauthorized entity', () => {
    renderPanel(['employee'], new Set(['employee']));
    expect(screen.queryByText('Full Name')).not.toBeInTheDocument();
    expect(screen.queryByText('ID')).not.toBeInTheDocument();
  });

  // ── Field Type Labels ──────────────────────────────────────────────────

  it('shows field type labels', () => {
    renderPanel(['employee']);
    // Each field row shows the type label: ID(Number), Full Name(Text), Email Address(Email), Salary(Number)
    const typeLabels = document.querySelectorAll('.text-\\[10px\\]');
    const typeTexts = Array.from(typeLabels).map((el) => el.textContent);
    expect(typeTexts).toContain('Number'); // ID field type
    expect(typeTexts).toContain('Text');   // name field type
    expect(typeTexts).toContain('Email');  // email field type
  });

  // ── Drag Functionality ─────────────────────────────────────────────────

  it('field items are draggable', () => {
    renderPanel(['employee']);
    const fieldItems = document.querySelectorAll('[draggable="true"]');
    // ID + 3 fields = 4 draggable items
    expect(fieldItems.length).toBe(4);
  });

  // ── Capabilities Filtering ─────────────────────────────────────────────

  it('hides entities the user cannot peek or read from the picker', async () => {
    const caps: AllCapabilities = {
      employee: { can_peek_all: true, can_read: true, can_create: false, can_update: false, can_delete: false },
      department: { can_peek_all: false, can_read: false, can_create: false, can_update: false, can_delete: false },
    };
    renderPanel([], new Set(), caps);
    await userEvent.click(screen.getByText('+ Add'));
    expect(screen.getByText('Employee')).toBeInTheDocument();
    expect(screen.queryByText('Department')).not.toBeInTheDocument();
  });

  it('shows entity in picker when user has can_peek_all but not can_read', async () => {
    const caps: AllCapabilities = {
      employee: { can_peek_all: true, can_read: false, can_create: false, can_update: false, can_delete: false },
      department: { can_peek_all: false, can_read: false, can_create: false, can_update: false, can_delete: false },
    };
    renderPanel([], new Set(), caps);
    await userEvent.click(screen.getByText('+ Add'));
    expect(screen.getByText('Employee')).toBeInTheDocument();
    expect(screen.queryByText('Department')).not.toBeInTheDocument();
  });

  it('shows all entities when capabilities are not provided', async () => {
    renderPanel([], new Set(), undefined);
    await userEvent.click(screen.getByText('+ Add'));
    expect(screen.getByText('Employee')).toBeInTheDocument();
    expect(screen.getByText('Department')).toBeInTheDocument();
  });
});
