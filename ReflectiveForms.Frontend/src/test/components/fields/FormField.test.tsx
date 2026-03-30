import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { FormProvider, useForm } from 'react-hook-form';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { FormField } from '../../../components/fields/FormField';

// Wrap in form provider
function Wrapper({ children }: { children: React.ReactNode }) {
  const methods = useForm({ defaultValues: { fields: {} } });
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return (
    <QueryClientProvider client={queryClient}>
      <FormProvider {...methods}>{children}</FormProvider>
    </QueryClientProvider>
  );
}

const textField = {
  name: 'test_field',
  type: 'Text' as const,
  label: 'Test Field',
  required: false,
  has_dynamic_choices_runtime: false,
  has_dynamic_choices_compile_time: false,
  has_logic_sanity_check: false,
  text_options: { placeholder: '', is_multiline: false },
};

describe('FormField depth-aware styling', () => {
  it('renders card wrapper at depth 0', () => {
    const { container } = render(
      <Wrapper>
        <FormField fieldSchema={textField} depth={0} />
      </Wrapper>,
    );

    const cardDiv = container.querySelector('.bg-white.rounded-lg.shadow-sm');
    expect(cardDiv).toBeTruthy();
  });

  it('does NOT render card wrapper at depth > 0', () => {
    const { container } = render(
      <Wrapper>
        <FormField fieldSchema={textField} depth={1} />
      </Wrapper>,
    );

    const cardDiv = container.querySelector('.bg-white.rounded-lg.shadow-sm');
    expect(cardDiv).toBeNull();

    // Should still render the field label
    expect(screen.getByText('Test Field')).toBeInTheDocument();
  });

  it('renders field label and content at all depths', () => {
    render(
      <Wrapper>
        <FormField fieldSchema={textField} depth={2} />
      </Wrapper>,
    );

    expect(screen.getByText('Test Field')).toBeInTheDocument();
    expect(screen.getByRole('textbox')).toBeInTheDocument();
  });
});
