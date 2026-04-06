import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FormulaAutocomplete } from '../../components/sheets/FormulaAutocomplete';
import type { EntitySchema } from '../../types/schema';

const mockSchemas: Record<string, EntitySchema> = {
  employee: {
    readable_name: { singular: 'Employee', plural: 'Employees' },
    endpoints: { create: '', read: '', update: '', delete: '' },
    features: { has_tags: false, has_categories: false, has_author: false, has_parent_child: false },
    fields: [
      { name: 'name', type: 'Text', label: 'Full Name', required: true, has_dynamic_choices_runtime: false, has_dynamic_choices_compile_time: false, has_logic_sanity_check: false },
      { name: 'salary', type: 'Number', label: 'Salary', required: false, has_dynamic_choices_runtime: false, has_dynamic_choices_compile_time: false, has_logic_sanity_check: false },
    ],
  } as EntitySchema,
  department: {
    readable_name: { singular: 'Department', plural: 'Departments' },
    endpoints: { create: '', read: '', update: '', delete: '' },
    features: { has_tags: false, has_categories: false, has_author: false, has_parent_child: false },
    fields: [
      { name: 'dept_name', type: 'Text', label: 'Department Name', required: true, has_dynamic_choices_runtime: false, has_dynamic_choices_compile_time: false, has_logic_sanity_check: false },
    ],
  } as EntitySchema,
};

describe('FormulaAutocomplete', () => {
  const defaultProps = {
    schemas: mockSchemas,
    position: { x: 100, y: 200 },
    onSelect: vi.fn(),
    onDismiss: vi.fn(),
  };

  // ── Hidden when no match ───────────────────────────────────────────────

  it('renders nothing when inputValue is empty', () => {
    const { container } = render(
      <FormulaAutocomplete {...defaultProps} inputValue="" />,
    );
    expect(container.innerHTML).toBe('');
  });

  it('renders nothing when inputValue has no RF prefix', () => {
    const { container } = render(
      <FormulaAutocomplete {...defaultProps} inputValue="=SUM(A1:A10)" />,
    );
    expect(container.innerHTML).toBe('');
  });

  it('renders nothing when position is null', () => {
    const { container } = render(
      <FormulaAutocomplete {...defaultProps} inputValue="=RF." position={null} />,
    );
    expect(container.innerHTML).toBe('');
  });

  // ── Function suggestions ───────────────────────────────────────────────

  it('shows all RF functions when typing =RF.', () => {
    render(<FormulaAutocomplete {...defaultProps} inputValue="=RF." />);
    expect(screen.getByText('RF.FIELD')).toBeInTheDocument();
    expect(screen.getByText('RF.LIST')).toBeInTheDocument();
    expect(screen.getByText('RF.LOOKUP')).toBeInTheDocument();
    expect(screen.getByText('RF.COUNT')).toBeInTheDocument();
    expect(screen.getByText('RF.SUM')).toBeInTheDocument();
    expect(screen.getByText('RF.AVG')).toBeInTheDocument();
    expect(screen.getByText('RF.TITLE')).toBeInTheDocument();
    expect(screen.getByText('RF.IDS')).toBeInTheDocument();
    expect(screen.getByText('RF.FILTER')).toBeInTheDocument();
    expect(screen.getByText('RF.MATCH')).toBeInTheDocument();
    expect(screen.getByText('RF.MATCHLIST')).toBeInTheDocument();
  });

  it('filters RF functions by partial name', () => {
    render(<FormulaAutocomplete {...defaultProps} inputValue="=RF.S" />);
    expect(screen.getByText('RF.SUM')).toBeInTheDocument();
    expect(screen.queryByText('RF.FIELD')).not.toBeInTheDocument();
    expect(screen.queryByText('RF.LIST')).not.toBeInTheDocument();
  });

  it('shows RF Functions header', () => {
    render(<FormulaAutocomplete {...defaultProps} inputValue="=RF." />);
    expect(screen.getByText('RF Functions')).toBeInTheDocument();
  });

  // ── Entity suggestions ─────────────────────────────────────────────────

  it('shows entity names when inside function parentheses', () => {
    render(<FormulaAutocomplete {...defaultProps} inputValue='=RF.FIELD(' />);
    expect(screen.getByText('employee')).toBeInTheDocument();
    expect(screen.getByText('department')).toBeInTheDocument();
    expect(screen.getByText('Entity Types')).toBeInTheDocument();
  });

  it('filters entity names by partial input', () => {
    render(<FormulaAutocomplete {...defaultProps} inputValue='=RF.FIELD("emp' />);
    expect(screen.getByText('employee')).toBeInTheDocument();
    expect(screen.queryByText('department')).not.toBeInTheDocument();
  });

  // ── Field suggestions ──────────────────────────────────────────────────

  it('shows field names after entity name is specified', () => {
    render(<FormulaAutocomplete {...defaultProps} inputValue='=RF.FIELD("employee", 1, "' />);
    expect(screen.getByText('name')).toBeInTheDocument();
    expect(screen.getByText('salary')).toBeInTheDocument();
  });

  // ── Click selection ────────────────────────────────────────────────────

  it('calls onSelect when clicking a suggestion', async () => {
    const onSelect = vi.fn();
    render(<FormulaAutocomplete {...defaultProps} inputValue="=RF.S" onSelect={onSelect} />);
    await userEvent.click(screen.getByText('RF.SUM'));
    expect(onSelect).toHaveBeenCalled();
  });
});
