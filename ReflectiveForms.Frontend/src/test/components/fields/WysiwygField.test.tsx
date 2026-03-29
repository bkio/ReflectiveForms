import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FormProvider, useForm } from 'react-hook-form';
import { WysiwygField } from '../../../components/fields/WysiwygField';
import { FieldSchema } from '../../../types/schema';
import { createElement, ReactNode } from 'react';

// Wrapper component that provides form context
function FormWrapper({
  children,
  defaultValues = { fields: {} },
}: {
  children: ReactNode;
  defaultValues?: Record<string, unknown>;
}) {
  const methods = useForm({ defaultValues });
  return createElement(FormProvider, { ...methods, children });
}

// Mock document.execCommand
beforeEach(() => {
  document.execCommand = vi.fn().mockReturnValue(true);
  // jsdom doesn't support innerText on contentEditable elements
  if (!('innerText' in HTMLDivElement.prototype)) {
    Object.defineProperty(HTMLDivElement.prototype, 'innerText', {
      get() { return this.textContent || ''; },
      set(value: string) { this.textContent = value; },
      configurable: true,
    });
  }
});

describe('WysiwygField', () => {
  const baseSchema: FieldSchema = {
    name: 'content',
    type: 'WysiwygEditor',
    label: 'Content',
    required: false,
    has_dynamic_choices_runtime: false,
    has_dynamic_choices_compile_time: false,
    has_logic_sanity_check: false,
    text_options: {
      placeholder: 'Enter content...',
      is_multiline: true,
    },
  };

  it('should render toolbar with formatting buttons', () => {
    render(
      <FormWrapper>
        <WysiwygField schema={baseSchema} path="fields.content" />
      </FormWrapper>
    );

    // Check for toolbar buttons by their titles
    expect(screen.getByTitle(/bold/i)).toBeInTheDocument();
    expect(screen.getByTitle(/italic/i)).toBeInTheDocument();
    expect(screen.getByTitle(/underline/i)).toBeInTheDocument();
  });

  it('should render heading buttons', () => {
    render(
      <FormWrapper>
        <WysiwygField schema={baseSchema} path="fields.content" />
      </FormWrapper>
    );

    expect(screen.getByTitle(/heading 1/i)).toBeInTheDocument();
    expect(screen.getByTitle(/heading 2/i)).toBeInTheDocument();
  });

  it('should render list buttons', () => {
    render(
      <FormWrapper>
        <WysiwygField schema={baseSchema} path="fields.content" />
      </FormWrapper>
    );

    expect(screen.getByTitle(/bullet list/i)).toBeInTheDocument();
    expect(screen.getByTitle(/numbered list/i)).toBeInTheDocument();
  });

  it('should render link button', () => {
    render(
      <FormWrapper>
        <WysiwygField schema={baseSchema} path="fields.content" />
      </FormWrapper>
    );

    expect(screen.getByTitle(/insert link/i)).toBeInTheDocument();
  });

  it('should render undo/redo buttons', () => {
    render(
      <FormWrapper>
        <WysiwygField schema={baseSchema} path="fields.content" />
      </FormWrapper>
    );

    expect(screen.getByTitle(/undo/i)).toBeInTheDocument();
    expect(screen.getByTitle(/redo/i)).toBeInTheDocument();
  });

  it('should render contentEditable area', () => {
    render(
      <FormWrapper>
        <WysiwygField schema={baseSchema} path="fields.content" />
      </FormWrapper>
    );

    const editor = document.querySelector('[contenteditable="true"]');
    expect(editor).toBeInTheDocument();
  });

  it('should have HTML/Preview toggle', () => {
    render(
      <FormWrapper>
        <WysiwygField schema={baseSchema} path="fields.content" />
      </FormWrapper>
    );

    expect(screen.getByText('HTML')).toBeInTheDocument();
  });

  it('should execute bold command on button click', async () => {
    const user = userEvent.setup();

    render(
      <FormWrapper>
        <WysiwygField schema={baseSchema} path="fields.content" />
      </FormWrapper>
    );

    const boldButton = screen.getByTitle(/bold/i);
    await user.click(boldButton);

    expect(document.execCommand).toHaveBeenCalledWith('bold', false);
  });

  it('should execute italic command on button click', async () => {
    const user = userEvent.setup();

    render(
      <FormWrapper>
        <WysiwygField schema={baseSchema} path="fields.content" />
      </FormWrapper>
    );

    const italicButton = screen.getByTitle(/italic/i);
    await user.click(italicButton);

    expect(document.execCommand).toHaveBeenCalledWith('italic', false);
  });

  it('should toggle to source mode', async () => {
    const user = userEvent.setup();

    render(
      <FormWrapper>
        <WysiwygField schema={baseSchema} path="fields.content" />
      </FormWrapper>
    );

    const htmlToggle = screen.getByText('HTML');
    await user.click(htmlToggle);

    // Should now show Preview button and textarea
    expect(screen.getByText('Preview')).toBeInTheDocument();
    expect(screen.getByPlaceholderText(/enter html/i)).toBeInTheDocument();
  });

  it('should show character count', () => {
    render(
      <FormWrapper>
        <WysiwygField schema={baseSchema} path="fields.content" />
      </FormWrapper>
    );

    expect(screen.getByText(/0 characters/i)).toBeInTheDocument();
  });

  it('should initialize with provided content', () => {
    const initialContent = '<p>Hello World</p>';

    render(
      <FormWrapper defaultValues={{ fields: { content: initialContent } }}>
        <WysiwygField schema={baseSchema} path="fields.content" />
      </FormWrapper>
    );

    const editor = document.querySelector('[contenteditable="true"]');
    expect(editor?.innerHTML).toBe(initialContent);
  });

  it('should disable toolbar buttons in source mode', async () => {
    const user = userEvent.setup();

    render(
      <FormWrapper>
        <WysiwygField schema={baseSchema} path="fields.content" />
      </FormWrapper>
    );

    // Switch to source mode
    await user.click(screen.getByText('HTML'));

    // Toolbar buttons should be disabled
    const boldButton = screen.getByTitle(/bold/i);
    expect(boldButton).toBeDisabled();
  });

  it('should render quote and code buttons', () => {
    render(
      <FormWrapper>
        <WysiwygField schema={baseSchema} path="fields.content" />
      </FormWrapper>
    );

    expect(screen.getByTitle(/quote/i)).toBeInTheDocument();
    expect(screen.getByTitle(/code block/i)).toBeInTheDocument();
  });
});
