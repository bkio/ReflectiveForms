import { describe, it, expect, vi } from 'vitest';
import { render, screen, act } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useForm, FormProvider } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { WysiwygField } from '../../components/fields/WysiwygField';

/**
 * WYSIWYG Field Containment Integration Tests
 *
 * Verifies that the WysiwygField component renders with the
 * contain-layout-paint CSS class on the contentEditable element.
 */

function WysiwygWrapper({ defaultValue = '' }: { defaultValue?: string }) {
  const schema = z.object({
    content: z.string().optional(),
  });

  const form = useForm({
    resolver: zodResolver(schema),
    defaultValues: { content: defaultValue },
  });

  return (
    <FormProvider {...form}>
      <WysiwygField
        schema={{
          name: 'content',
          type: 'WysiwygEditor',
          label: 'Content',
          instructions: '',
          required: false,
          default_value: null,
          display_condition: null,
          text_options: { placeholder: 'Write something...' },
          select_options: null,
          number_options: null,
          date_options: null,
          relation_options: null,
          repeater_options: null,
          group_options: null,
          media_options: null,
          has_dynamic_choices_runtime: false,
          has_dynamic_choices_compile_time: false,
          has_logic_sanity_check: false,
          ai_suggestion: null,
          ai_sanity_checks: null,
          ai_relation_suggestion: null,
        }}
        path="content"
      />
    </FormProvider>
  );
}

function renderWysiwyg(defaultValue?: string) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });

  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>
        <WysiwygWrapper defaultValue={defaultValue} />
      </MemoryRouter>
    </QueryClientProvider>
  );
}

describe('WysiwygField CSS Containment', () => {
  it('renders contentEditable with contain-layout-paint class', () => {
    renderWysiwyg();

    const editable = document.querySelector('[contenteditable="true"]');
    expect(editable).not.toBeNull();
    expect(editable!.className).toContain('contain-layout-paint');
  });

  it('renders contentEditable even with empty default value', () => {
    renderWysiwyg('');

    const editable = document.querySelector('[contenteditable="true"]');
    expect(editable).not.toBeNull();
    expect(editable!.className).toContain('contain-layout-paint');
  });

  it('renders contentEditable with pre-filled HTML', () => {
    renderWysiwyg('<p>Pre-filled <strong>content</strong></p>');

    const editable = document.querySelector('[contenteditable="true"]');
    expect(editable).not.toBeNull();
    expect(editable!.className).toContain('contain-layout-paint');
  });

  it('renders source mode textarea (no containment needed — plain text)', async () => {
    renderWysiwyg();

    // Toggle source mode
    const htmlBtn = screen.getByRole('button', { name: /html/i });
    await act(() => {
      htmlBtn.click();
    });

    // Textarea should appear, contentEditable should be gone
    const textarea = document.querySelector('textarea');
    expect(textarea).not.toBeNull();
  });

  it('toolbar buttons are rendered and interactable', () => {
    renderWysiwyg();

    const boldBtn = screen.getByRole('button', { name: /bold/i });
    expect(boldBtn).toBeInTheDocument();
    expect(boldBtn).not.toBeDisabled();

    const italicBtn = screen.getByRole('button', { name: /italic/i });
    expect(italicBtn).toBeInTheDocument();

    const underlineBtn = screen.getByRole('button', { name: /underline/i });
    expect(underlineBtn).toBeInTheDocument();
  });

  it('character count is displayed', () => {
    renderWysiwyg('<p>Hello World</p>');

    // Should show character count (textContent length of "Hello World" = 11)
    expect(screen.getByText(/characters/)).toBeInTheDocument();
  });

  it('contentEditable has data-placeholder attribute', () => {
    renderWysiwyg();

    const editable = document.querySelector('[contenteditable="true"]');
    expect(editable).not.toBeNull();
    expect(editable!.getAttribute('data-placeholder')).toBe('Write something...');
  });

  it('.wysiwyg-editor wrapper class is present', () => {
    renderWysiwyg();

    const wrapper = document.querySelector('.wysiwyg-editor');
    expect(wrapper).not.toBeNull();
  });
});
