import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FormProvider, useForm } from 'react-hook-form';
import { RepeaterField } from '../../components/fields/RepeaterField';
import { FieldSchema } from '../../types/schema';
import { createElement, ReactNode } from 'react';

// Wrapper component that provides form context
function FormWrapper({
  children,
  defaultValues = { fields: { items: [] } },
}: {
  children: ReactNode;
  defaultValues?: Record<string, unknown>;
}) {
  const methods = useForm({ defaultValues });
  return createElement(FormProvider, { ...methods }, children);
}

describe('RepeaterField', () => {
  const baseSchema: FieldSchema = {
    name: 'items',
    type: 'Repeater',
    label: 'Items',
    required: false,
    has_dynamic_choices_runtime: false,
    has_dynamic_choices_compile_time: false,
    has_logic_sanity_check: false,
    repeater_options: {
      item_schema: [
        {
          name: 'name',
          type: 'Text',
          label: 'Name',
          required: false,
          has_dynamic_choices_runtime: false,
          has_dynamic_choices_compile_time: false,
          has_logic_sanity_check: false,
        },
      ],
      add_button_label: 'Add Item',
      use_accordion: false,
    },
  };

  it('should render repeater with add button', () => {
    render(
      <FormWrapper>
        <RepeaterField schema={baseSchema} path="fields.items" />
      </FormWrapper>
    );

    expect(screen.getByRole('button', { name: /add item/i })).toBeInTheDocument();
  });

  it('should add an item when add button is clicked', async () => {
    const user = userEvent.setup();

    render(
      <FormWrapper>
        <RepeaterField schema={baseSchema} path="fields.items" />
      </FormWrapper>
    );

    const addButton = screen.getByRole('button', { name: /add item/i });
    await user.click(addButton);

    // Should have one item now - look for the nested field
    expect(screen.getByText('Name')).toBeInTheDocument();
  });

  it('should add multiple items', async () => {
    const user = userEvent.setup();

    render(
      <FormWrapper>
        <RepeaterField schema={baseSchema} path="fields.items" />
      </FormWrapper>
    );

    const addButton = screen.getByRole('button', { name: /add item/i });

    // Add 3 items
    await user.click(addButton);
    await user.click(addButton);
    await user.click(addButton);

    // Should have 3 "Name" labels
    const nameLabels = screen.getAllByText('Name');
    expect(nameLabels).toHaveLength(3);
  });

  it('should respect maxItems limit', async () => {
    const user = userEvent.setup();

    const schemaWithMax: FieldSchema = {
      ...baseSchema,
      repeater_options: {
        ...baseSchema.repeater_options!,
        max_items: 2,
      },
    };

    render(
      <FormWrapper>
        <RepeaterField schema={schemaWithMax} path="fields.items" />
      </FormWrapper>
    );

    const addButton = screen.getByRole('button', { name: /add item/i });

    // Add max items
    await user.click(addButton);
    await user.click(addButton);

    // Add button should no longer be visible
    expect(screen.queryByRole('button', { name: /add item/i })).not.toBeInTheDocument();
  });

  it('should show remove button for items', async () => {
    const user = userEvent.setup();

    render(
      <FormWrapper>
        <RepeaterField schema={baseSchema} path="fields.items" />
      </FormWrapper>
    );

    // Add an item
    await user.click(screen.getByRole('button', { name: /add item/i }));

    // Should have a delete/remove button
    const deleteButtons = screen.getAllByRole('button').filter(
      btn => btn.querySelector('svg') // Look for icon buttons
    );
    expect(deleteButtons.length).toBeGreaterThan(0);
  });

  it('should render with custom add button label', () => {
    const schemaWithCustomLabel: FieldSchema = {
      ...baseSchema,
      repeater_options: {
        ...baseSchema.repeater_options!,
        add_button_label: 'Add New Entry',
      },
    };

    render(
      <FormWrapper>
        <RepeaterField schema={schemaWithCustomLabel} path="fields.items" />
      </FormWrapper>
    );

    expect(screen.getByRole('button', { name: /add new entry/i })).toBeInTheDocument();
  });

  it('should render nested repeater fields', async () => {
    const user = userEvent.setup();

    const nestedSchema: FieldSchema = {
      ...baseSchema,
      repeater_options: {
        item_schema: [
          {
            name: 'title',
            type: 'Text',
            label: 'Title',
            required: false,
            has_dynamic_choices_runtime: false,
            has_dynamic_choices_compile_time: false,
            has_logic_sanity_check: false,
          },
          {
            name: 'tags',
            type: 'Repeater',
            label: 'Tags',
            required: false,
            has_dynamic_choices_runtime: false,
            has_dynamic_choices_compile_time: false,
            has_logic_sanity_check: false,
            repeater_options: {
              item_schema: [
                {
                  name: 'tagName',
                  type: 'Text',
                  label: 'Tag Name',
                  required: false,
                  has_dynamic_choices_runtime: false,
                  has_dynamic_choices_compile_time: false,
                  has_logic_sanity_check: false,
                },
              ],
              add_button_label: 'Add Tag',
            },
          },
        ],
        add_button_label: 'Add Item',
      },
    };

    render(
      <FormWrapper>
        <RepeaterField schema={nestedSchema} path="fields.items" />
      </FormWrapper>
    );

    // Add parent item
    await user.click(screen.getByRole('button', { name: /add item/i }));

    // Should see parent fields and nested repeater
    expect(screen.getByText('Title')).toBeInTheDocument();
    expect(screen.getByText('Tags')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /add tag/i })).toBeInTheDocument();
  });
});
