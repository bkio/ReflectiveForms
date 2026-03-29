import { describe, it, expect, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { FormProvider, useForm } from 'react-hook-form';
import { MediaField } from '../../../components/fields/MediaField';
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

// Mock FileReader
class MockFileReader {
  result: string | null = null;
  onload: ((e: ProgressEvent<FileReader>) => void) | null = null;
  onerror: (() => void) | null = null;

  readAsDataURL(_file: File) {
    setTimeout(() => {
      this.result = 'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==';
      if (this.onload) {
        this.onload({ target: this as unknown as FileReader } as ProgressEvent<FileReader>);
      }
    }, 0);
  }
}

describe('MediaField', () => {
  const baseSchema: FieldSchema = {
    name: 'image',
    type: 'MediaSourceBase64',
    label: 'Image',
    required: false,
    has_dynamic_choices_runtime: false,
    has_dynamic_choices_compile_time: false,
    has_logic_sanity_check: false,
    media_options: {
      max_file_size_mb: 8,
      accepted_types: ['image/*'],
      preview_enabled: true,
    },
  };

  beforeEach(() => {
    // Mock FileReader
    (globalThis as any).FileReader = MockFileReader;
  });

  it('should render upload area', () => {
    render(
      <FormWrapper>
        <MediaField schema={baseSchema} path="fields.image" />
      </FormWrapper>
    );

    expect(screen.getByText(/drop an image here/i)).toBeInTheDocument();
  });

  it('should show max file size', () => {
    render(
      <FormWrapper>
        <MediaField schema={baseSchema} path="fields.image" />
      </FormWrapper>
    );

    expect(screen.getByText(/max size: 8mb/i)).toBeInTheDocument();
  });

  it('should show accepted file types', () => {
    render(
      <FormWrapper>
        <MediaField schema={baseSchema} path="fields.image" />
      </FormWrapper>
    );

    expect(screen.getByText(/accepted: image\/\*/i)).toBeInTheDocument();
  });

  it('should have hidden file input', () => {
    render(
      <FormWrapper>
        <MediaField schema={baseSchema} path="fields.image" />
      </FormWrapper>
    );

    const input = document.querySelector('input[type="file"]') as HTMLInputElement;
    expect(input).toBeInTheDocument();
    expect(input).toHaveClass('hidden');
  });

  it('should accept correct file types', () => {
    render(
      <FormWrapper>
        <MediaField schema={baseSchema} path="fields.image" />
      </FormWrapper>
    );

    const input = document.querySelector('input[type="file"]') as HTMLInputElement;
    expect(input.accept).toBe('image/*');
  });

  it('should handle custom accepted types', () => {
    const schemaWithTypes: FieldSchema = {
      ...baseSchema,
      media_options: {
        ...baseSchema.media_options!,
        accepted_types: ['image/png', 'image/jpeg'],
      },
    };

    render(
      <FormWrapper>
        <MediaField schema={schemaWithTypes} path="fields.image" />
      </FormWrapper>
    );

    const input = document.querySelector('input[type="file"]') as HTMLInputElement;
    expect(input.accept).toBe('image/png,image/jpeg');
  });

  it('should show preview when value exists', () => {
    const base64Image = 'data:image/png;base64,iVBORw0KGgo=';

    render(
      <FormWrapper defaultValues={{ fields: { image: base64Image } }}>
        <MediaField schema={baseSchema} path="fields.image" />
      </FormWrapper>
    );

    const img = screen.getByRole('img', { name: /preview/i });
    expect(img).toHaveAttribute('src', base64Image);
  });

  it('should show replace button when image exists', () => {
    const base64Image = 'data:image/png;base64,iVBORw0KGgo=';

    render(
      <FormWrapper defaultValues={{ fields: { image: base64Image } }}>
        <MediaField schema={baseSchema} path="fields.image" />
      </FormWrapper>
    );

    expect(screen.getByText(/replace image/i)).toBeInTheDocument();
  });

  it('should handle drag over state', () => {
    render(
      <FormWrapper>
        <MediaField schema={baseSchema} path="fields.image" />
      </FormWrapper>
    );

    const dropArea = screen.getByText(/drop an image here/i).closest('div');
    expect(dropArea).toBeInTheDocument();

    // Simulate drag over
    fireEvent.dragOver(dropArea!, {
      preventDefault: () => {},
      stopPropagation: () => {},
    });

    expect(screen.getByText(/drop your file here/i)).toBeInTheDocument();
  });

  it('should handle drag leave state', () => {
    render(
      <FormWrapper>
        <MediaField schema={baseSchema} path="fields.image" />
      </FormWrapper>
    );

    const dropArea = screen.getByText(/drop an image here/i).closest('div');

    // Drag over then leave
    fireEvent.dragOver(dropArea!, {
      preventDefault: () => {},
      stopPropagation: () => {},
    });

    fireEvent.dragLeave(dropArea!, {
      preventDefault: () => {},
      stopPropagation: () => {},
    });

    expect(screen.getByText(/drop an image here/i)).toBeInTheDocument();
  });

  it('should validate file size', async () => {
    const smallMaxSchema: FieldSchema = {
      ...baseSchema,
      media_options: {
        ...baseSchema.media_options!,
        max_file_size_mb: 0.001, // 1KB max
      },
    };

    render(
      <FormWrapper>
        <MediaField schema={smallMaxSchema} path="fields.image" />
      </FormWrapper>
    );

    const input = document.querySelector('input[type="file"]') as HTMLInputElement;

    // Create a file larger than 1KB
    const largeFile = new File(['x'.repeat(2000)], 'large.png', { type: 'image/png' });

    fireEvent.change(input, { target: { files: [largeFile] } });

    await waitFor(() => {
      expect(screen.getByText(/file size must be less than/i)).toBeInTheDocument();
    });
  });
});
