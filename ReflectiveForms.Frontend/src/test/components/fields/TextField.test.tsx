import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FormProvider, useForm } from 'react-hook-form';
import { TextField, TextAreaField } from '../../components/fields/TextField';
import { FieldSchema } from '../../types/schema';
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
  return createElement(FormProvider, { ...methods }, children);
}

describe('TextField', () => {
  const baseSchema: FieldSchema = {
    name: 'testField',
    type: 'Text',
    label: 'Test Field',
    required: false,
    has_dynamic_choices_runtime: false,
    has_dynamic_choices_compile_time: false,
    has_logic_sanity_check: false,
  };

  it('should render text input', () => {
    render(
      <FormWrapper>
        <TextField schema={baseSchema} path="fields.testField" />
      </FormWrapper>
    );

    expect(screen.getByRole('textbox')).toBeInTheDocument();
  });

  it('should render with placeholder', () => {
    const schema = {
      ...baseSchema,
      text_options: {
        placeholder: 'Enter text here',
        is_multiline: false,
      },
    };

    render(
      <FormWrapper>
        <TextField schema={schema} path="fields.testField" />
      </FormWrapper>
    );

    expect(screen.getByPlaceholderText('Enter text here')).toBeInTheDocument();
  });

  it('should render email type for Email field', () => {
    const schema = { ...baseSchema, type: 'Email' as const };

    render(
      <FormWrapper>
        <TextField schema={schema} path="fields.testField" />
      </FormWrapper>
    );

    expect(screen.getByRole('textbox')).toHaveAttribute('type', 'email');
  });

  it('should render url type for Url field', () => {
    const schema = { ...baseSchema, type: 'Url' as const };

    render(
      <FormWrapper>
        <TextField schema={schema} path="fields.testField" />
      </FormWrapper>
    );

    expect(screen.getByRole('textbox')).toHaveAttribute('type', 'url');
  });

  it('should accept user input', async () => {
    const user = userEvent.setup();

    render(
      <FormWrapper>
        <TextField schema={baseSchema} path="fields.testField" />
      </FormWrapper>
    );

    const input = screen.getByRole('textbox');
    await user.type(input, 'Hello World');

    expect(input).toHaveValue('Hello World');
  });

  it('should respect maxLength', () => {
    const schema = {
      ...baseSchema,
      text_options: {
        max_length: 10,
        is_multiline: false,
      },
    };

    render(
      <FormWrapper>
        <TextField schema={schema} path="fields.testField" />
      </FormWrapper>
    );

    expect(screen.getByRole('textbox')).toHaveAttribute('maxlength', '10');
  });
});

describe('TextAreaField', () => {
  const baseSchema: FieldSchema = {
    name: 'testField',
    type: 'TextArea',
    label: 'Test Field',
    required: false,
    has_dynamic_choices_runtime: false,
    has_dynamic_choices_compile_time: false,
    has_logic_sanity_check: false,
    text_options: {
      is_multiline: true,
    },
  };

  it('should render textarea', () => {
    render(
      <FormWrapper>
        <TextAreaField schema={baseSchema} path="fields.testField" />
      </FormWrapper>
    );

    expect(screen.getByRole('textbox')).toBeInTheDocument();
  });

  it('should accept multiline input', async () => {
    const user = userEvent.setup();

    render(
      <FormWrapper>
        <TextAreaField schema={baseSchema} path="fields.testField" />
      </FormWrapper>
    );

    const textarea = screen.getByRole('textbox');
    await user.type(textarea, 'Line 1{enter}Line 2');

    expect(textarea).toHaveValue('Line 1\nLine 2');
  });
});
