import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FormProvider, useForm } from 'react-hook-form';
import {
  SelectField,
  CheckboxField,
  NumberField,
  DatePickerField,
} from '../../../components/fields/SelectField';
import { FieldSchema } from '../../../types/schema';
import { createElement, ReactNode } from 'react';

// Wrapper component that provides form context
function FormWrapper({
  children,
  defaultValues = {},
}: {
  children: ReactNode;
  defaultValues?: Record<string, unknown>;
}) {
  const methods = useForm({ defaultValues });
  return createElement(FormProvider, { ...methods, children });
}

describe('SelectField', () => {
  const baseSchema: FieldSchema = {
    name: 'status',
    type: 'Select',
    label: 'Status',
    required: false,
    has_dynamic_choices_runtime: false,
    has_dynamic_choices_compile_time: false,
    has_logic_sanity_check: false,
    select_options: {
      choices: [
        { value: 'draft', label: 'Draft' },
        { value: 'published', label: 'Published' },
        { value: 'archived', label: 'Archived' },
      ],
      allow_multiple: false,
    },
  };

  it('should render searchable select trigger with options', async () => {
    const user = userEvent.setup();

    render(
      <FormWrapper defaultValues={{ fields: { status: 'draft' } }}>
        <SelectField schema={baseSchema} path="fields.status" />
      </FormWrapper>
    );

    // Should show the trigger button (not a native select)
    const trigger = screen.getByRole('button', { name: /draft/i });
    expect(trigger).toBeInTheDocument();
    expect(trigger).toHaveAttribute('aria-haspopup', 'listbox');

    // Open the dropdown to see options
    await user.click(trigger);
    expect(screen.getByRole('listbox')).toBeInTheDocument();
    expect(screen.getAllByRole('option').length).toBe(3);
  });

  it('should allow selecting an option', async () => {
    const user = userEvent.setup();

    render(
      <FormWrapper defaultValues={{ fields: { status: 'draft' } }}>
        <SelectField schema={baseSchema} path="fields.status" />
      </FormWrapper>
    );

    const trigger = screen.getByRole('button', { name: /draft/i });
    await user.click(trigger);

    // Click "Published" option
    const publishedOption = screen.getByRole('option', { name: 'Published' });
    await user.click(publishedOption);

    // Dropdown should close and trigger should show new selection
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: /published/i })).toHaveAttribute('data-value', 'published');
  });
});

describe('CheckboxField', () => {
  const baseSchema: FieldSchema = {
    name: 'isActive',
    type: 'Checkbox',
    label: 'Is Active',
    required: false,
    has_dynamic_choices_runtime: false,
    has_dynamic_choices_compile_time: false,
    has_logic_sanity_check: false,
  };

  it('should render checkbox', () => {
    render(
      <FormWrapper>
        <CheckboxField schema={baseSchema} path="fields.isActive" />
      </FormWrapper>
    );

    expect(screen.getByRole('checkbox')).toBeInTheDocument();
    expect(screen.getByText('Is Active')).toBeInTheDocument();
  });

  it('should toggle on click', async () => {
    const user = userEvent.setup();

    render(
      <FormWrapper>
        <CheckboxField schema={baseSchema} path="fields.isActive" />
      </FormWrapper>
    );

    const checkbox = screen.getByRole('checkbox');
    expect(checkbox).not.toBeChecked();

    await user.click(checkbox);
    expect(checkbox).toBeChecked();

    await user.click(checkbox);
    expect(checkbox).not.toBeChecked();
  });
});

describe('NumberField', () => {
  const baseSchema: FieldSchema = {
    name: 'quantity',
    type: 'Number',
    label: 'Quantity',
    required: false,
    has_dynamic_choices_runtime: false,
    has_dynamic_choices_compile_time: false,
    has_logic_sanity_check: false,
    number_options: {
      min: 0,
      max: 100,
      step: 1,
      is_range: false,
    },
  };

  it('should render number input', () => {
    render(
      <FormWrapper>
        <NumberField schema={baseSchema} path="fields.quantity" />
      </FormWrapper>
    );

    expect(screen.getByRole('spinbutton')).toBeInTheDocument();
  });

  it('should have min/max/step attributes', () => {
    render(
      <FormWrapper>
        <NumberField schema={baseSchema} path="fields.quantity" />
      </FormWrapper>
    );

    const input = screen.getByRole('spinbutton');
    expect(input).toHaveAttribute('min', '0');
    expect(input).toHaveAttribute('max', '100');
    expect(input).toHaveAttribute('step', '1');
  });

  it('should render range input when is_range is true', () => {
    const rangeSchema = {
      ...baseSchema,
      type: 'Range' as const,
      number_options: { ...baseSchema.number_options!, is_range: true },
    };

    render(
      <FormWrapper>
        <NumberField schema={rangeSchema} path="fields.quantity" />
      </FormWrapper>
    );

    expect(screen.getByRole('slider')).toBeInTheDocument();
  });

  it('should accept numeric input', async () => {
    const user = userEvent.setup();

    render(
      <FormWrapper>
        <NumberField schema={baseSchema} path="fields.quantity" />
      </FormWrapper>
    );

    const input = screen.getByRole('spinbutton');
    await user.clear(input);
    await user.type(input, '42');

    expect(input).toHaveValue(42);
  });
});

describe('DatePickerField', () => {
  const baseSchema: FieldSchema = {
    name: 'eventDate',
    type: 'DatePicker',
    label: 'Event Date',
    required: false,
    has_dynamic_choices_runtime: false,
    has_dynamic_choices_compile_time: false,
    has_logic_sanity_check: false,
    date_options: {
      format: 'YYYY-MM-DD',
      include_time: false,
    },
  };

  it('should render date input', () => {
    render(
      <FormWrapper>
        <DatePickerField schema={baseSchema} path="fields.eventDate" />
      </FormWrapper>
    );

    // Date inputs don't have a specific role, so we query by type
    const input = document.querySelector('input[type="date"]');
    expect(input).toBeInTheDocument();
  });

  it('should accept date input', async () => {
    const user = userEvent.setup();

    render(
      <FormWrapper>
        <DatePickerField schema={baseSchema} path="fields.eventDate" />
      </FormWrapper>
    );

    const input = document.querySelector('input[type="date"]') as HTMLInputElement;
    await user.type(input, '2024-12-25');

    expect(input.value).toBe('2024-12-25');
  });
});
