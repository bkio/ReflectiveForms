import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FormProvider, useForm } from 'react-hook-form';
import { RepeaterField, REPEATER_HEADER_HEIGHT, TOP_BAR_HEIGHT } from '../../../components/fields/RepeaterField';
import { FieldSchema } from '../../../types/schema';
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
  return createElement(FormProvider, { ...methods, children });
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
      render_style: 'Full',
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
              use_accordion: false,
              render_style: 'Full',
            },
          },
        ],
        add_button_label: 'Add Item',
        use_accordion: false,
        render_style: 'Full',
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

  describe('sticky header stacking', () => {
    const fieldBase = {
      required: false,
      has_dynamic_choices_runtime: false,
      has_dynamic_choices_compile_time: false,
      has_logic_sanity_check: false,
    };

    /**
     * Build a deeply nested repeater schema with the given depth.
     * Each level has a Text field and a child Repeater.
     */
    function buildNestedSchema(levels: number): FieldSchema {
      function buildLevel(currentLevel: number): FieldSchema {
        const label = `Level${currentLevel}`;
        const childFields: FieldSchema[] = [
          { name: `text_${currentLevel}`, type: 'Text', label: `Text L${currentLevel}`, ...fieldBase },
        ];

        if (currentLevel < levels) {
          childFields.push(buildLevel(currentLevel + 1));
        }

        return {
          name: `repeater_${currentLevel}`,
          type: 'Repeater',
          label,
          ...fieldBase,
          repeater_options: {
            item_schema: childFields,
            add_button_label: `Add ${label}`,
            use_accordion: false,
            render_style: 'Full',
          },
        };
      }
      return buildLevel(1);
    }

    it('should set correct top offset per depth for 6 nested levels', async () => {
      const user = userEvent.setup();
      const schema = buildNestedSchema(6);

      render(
        <FormWrapper>
          <RepeaterField schema={schema} path="fields.items" />
        </FormWrapper>
      );

      // Open one item at each level
      for (let level = 1; level <= 6; level++) {
        const addBtn = screen.getByRole('button', { name: new RegExp(`Add Level${level}`, 'i') });
        await user.click(addBtn);
      }

      // Verify each depth has the correct top offset
      for (let depth = 0; depth < 6; depth++) {
        const headers = screen.getAllByTestId(`repeater-header-depth-${depth}`);
        expect(headers.length).toBeGreaterThanOrEqual(1);

        const expectedTop = `${TOP_BAR_HEIGHT + depth * REPEATER_HEADER_HEIGHT}px`;
        for (const header of headers) {
          expect(header.style.top).toBe(expectedTop);
        }
      }
    });

    it('should set decreasing z-index per depth for 6 nested levels', async () => {
      const user = userEvent.setup();
      const schema = buildNestedSchema(6);

      render(
        <FormWrapper>
          <RepeaterField schema={schema} path="fields.items" />
        </FormWrapper>
      );

      // Open one item at each level
      for (let level = 1; level <= 6; level++) {
        const addBtn = screen.getByRole('button', { name: new RegExp(`Add Level${level}`, 'i') });
        await user.click(addBtn);
      }

      // Verify z-index decreases with depth (parent stays on top)
      for (let depth = 0; depth < 6; depth++) {
        const headers = screen.getAllByTestId(`repeater-header-depth-${depth}`);
        const expectedZ = String(10 - depth);
        for (const header of headers) {
          expect(header.style.zIndex).toBe(expectedZ);
        }
      }
    });

    it('parent header z-index > child header z-index for all 6 levels', async () => {
      const user = userEvent.setup();
      const schema = buildNestedSchema(6);

      render(
        <FormWrapper>
          <RepeaterField schema={schema} path="fields.items" />
        </FormWrapper>
      );

      for (let level = 1; level <= 6; level++) {
        const addBtn = screen.getByRole('button', { name: new RegExp(`Add Level${level}`, 'i') });
        await user.click(addBtn);
      }

      // Each depth N header should have a higher z-index than depth N+1
      for (let depth = 0; depth < 5; depth++) {
        const parentHeader = screen.getAllByTestId(`repeater-header-depth-${depth}`)[0];
        const childHeader = screen.getAllByTestId(`repeater-header-depth-${depth + 1}`)[0];
        expect(Number(parentHeader.style.zIndex)).toBeGreaterThan(Number(childHeader.style.zIndex));
      }
    });

    it('child header top offset is exactly one header-height below parent', async () => {
      const user = userEvent.setup();
      const schema = buildNestedSchema(6);

      render(
        <FormWrapper>
          <RepeaterField schema={schema} path="fields.items" />
        </FormWrapper>
      );

      for (let level = 1; level <= 6; level++) {
        const addBtn = screen.getByRole('button', { name: new RegExp(`Add Level${level}`, 'i') });
        await user.click(addBtn);
      }

      // Verify consecutive depth headers differ by exactly REPEATER_HEADER_HEIGHT
      for (let depth = 0; depth < 5; depth++) {
        const parentHeader = screen.getAllByTestId(`repeater-header-depth-${depth}`)[0];
        const childHeader = screen.getAllByTestId(`repeater-header-depth-${depth + 1}`)[0];
        const parentTop = parseInt(parentHeader.style.top, 10);
        const childTop = parseInt(childHeader.style.top, 10);
        expect(childTop - parentTop).toBe(REPEATER_HEADER_HEIGHT);
      }
    });

    it('depth-0 header starts at TOP_BAR_HEIGHT', async () => {
      const user = userEvent.setup();
      const schema = buildNestedSchema(1);

      render(
        <FormWrapper>
          <RepeaterField schema={schema} path="fields.items" />
        </FormWrapper>
      );

      await user.click(screen.getByRole('button', { name: /Add Level1/i }));

      const header = screen.getByTestId('repeater-header-depth-0');
      expect(header.style.top).toBe(`${TOP_BAR_HEIGHT}px`);
    });

    it('multiple items at same depth share the same top/z-index', async () => {
      const user = userEvent.setup();
      const schema = buildNestedSchema(2);

      render(
        <FormWrapper>
          <RepeaterField schema={schema} path="fields.items" />
        </FormWrapper>
      );

      // Add 3 items at depth 0
      for (let i = 0; i < 3; i++) {
        await user.click(screen.getByRole('button', { name: /Add Level1/i }));
      }

      const depth0Headers = screen.getAllByTestId('repeater-header-depth-0');
      expect(depth0Headers).toHaveLength(3);

      const expectedTop = `${TOP_BAR_HEIGHT}px`;
      const expectedZ = '10';
      for (const header of depth0Headers) {
        expect(header.style.top).toBe(expectedTop);
        expect(header.style.zIndex).toBe(expectedZ);
      }
    });
  });
});
