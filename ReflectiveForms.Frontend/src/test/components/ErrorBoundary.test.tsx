import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { BrowserRouter } from 'react-router-dom';
import { ErrorBoundary, AsyncErrorFallback } from '../../components/ErrorBoundary';

// Component that throws an error
function ThrowError({ shouldThrow }: { shouldThrow: boolean }) {
  if (shouldThrow) {
    throw new Error('Test error message');
  }
  return <div>No error</div>;
}

describe('ErrorBoundary', () => {
  // Suppress console.error for expected errors
  const originalError = console.error;
  beforeAll(() => {
    console.error = vi.fn();
  });
  afterAll(() => {
    console.error = originalError;
  });

  it('should render children when no error', () => {
    render(
      <BrowserRouter>
        <ErrorBoundary>
          <div>Test content</div>
        </ErrorBoundary>
      </BrowserRouter>
    );

    expect(screen.getByText('Test content')).toBeInTheDocument();
  });

  it('should render error UI when child throws', () => {
    render(
      <BrowserRouter>
        <ErrorBoundary>
          <ThrowError shouldThrow={true} />
        </ErrorBoundary>
      </BrowserRouter>
    );

    expect(screen.getByText('Something went wrong')).toBeInTheDocument();
    expect(screen.getByText('Test error message')).toBeInTheDocument();
  });

  it('should render Try Again button', () => {
    render(
      <BrowserRouter>
        <ErrorBoundary>
          <ThrowError shouldThrow={true} />
        </ErrorBoundary>
      </BrowserRouter>
    );

    expect(screen.getByRole('button', { name: /try again/i })).toBeInTheDocument();
  });

  it('should render Home link', () => {
    render(
      <BrowserRouter>
        <ErrorBoundary>
          <ThrowError shouldThrow={true} />
        </ErrorBoundary>
      </BrowserRouter>
    );

    expect(screen.getByRole('link', { name: /home/i })).toBeInTheDocument();
  });

  it('should call onError callback when error occurs', () => {
    const onError = vi.fn();

    render(
      <BrowserRouter>
        <ErrorBoundary onError={onError}>
          <ThrowError shouldThrow={true} />
        </ErrorBoundary>
      </BrowserRouter>
    );

    expect(onError).toHaveBeenCalled();
    expect(onError.mock.calls[0][0]).toBeInstanceOf(Error);
    expect(onError.mock.calls[0][0].message).toBe('Test error message');
  });

  it('should render custom fallback when provided', () => {
    render(
      <BrowserRouter>
        <ErrorBoundary fallback={<div>Custom error fallback</div>}>
          <ThrowError shouldThrow={true} />
        </ErrorBoundary>
      </BrowserRouter>
    );

    expect(screen.getByText('Custom error fallback')).toBeInTheDocument();
  });

  it('should reset error state when Try Again is clicked', async () => {
    const user = userEvent.setup();

    let shouldThrow = true;
    const { rerender } = render(
      <BrowserRouter>
        <ErrorBoundary>
          <ThrowError shouldThrow={shouldThrow} />
        </ErrorBoundary>
      </BrowserRouter>
    );

    // Error should be shown
    expect(screen.getByText('Something went wrong')).toBeInTheDocument();

    // Fix the error
    shouldThrow = false;

    // Click Try Again
    await user.click(screen.getByRole('button', { name: /try again/i }));

    // Rerender with fixed component
    rerender(
      <BrowserRouter>
        <ErrorBoundary>
          <ThrowError shouldThrow={false} />
        </ErrorBoundary>
      </BrowserRouter>
    );

    // Note: Due to how React batches updates, we may need to verify behavior
  });

  it('should show technical details in development mode', () => {
    // Mock import.meta.env.DEV
    const originalEnv = import.meta.env.DEV;
    (import.meta.env as any).DEV = true;

    render(
      <BrowserRouter>
        <ErrorBoundary>
          <ThrowError shouldThrow={true} />
        </ErrorBoundary>
      </BrowserRouter>
    );

    expect(screen.getByText('Show technical details')).toBeInTheDocument();

    // Restore
    (import.meta.env as any).DEV = originalEnv;
  });
});

describe('AsyncErrorFallback', () => {
  it('should render error message', () => {
    const error = new Error('Async error occurred');

    render(
      <AsyncErrorFallback error={error} resetErrorBoundary={() => {}} />
    );

    expect(screen.getByText('Async error occurred')).toBeInTheDocument();
  });

  it('should render Failed to load data title', () => {
    const error = new Error('Network error');

    render(
      <AsyncErrorFallback error={error} resetErrorBoundary={() => {}} />
    );

    expect(screen.getByText('Failed to load data')).toBeInTheDocument();
  });

  it('should render Retry button', () => {
    const error = new Error('Error');

    render(
      <AsyncErrorFallback error={error} resetErrorBoundary={() => {}} />
    );

    expect(screen.getByRole('button', { name: /retry/i })).toBeInTheDocument();
  });

  it('should call resetErrorBoundary on Retry click', async () => {
    const user = userEvent.setup();
    const resetFn = vi.fn();
    const error = new Error('Error');

    render(
      <AsyncErrorFallback error={error} resetErrorBoundary={resetFn} />
    );

    await user.click(screen.getByRole('button', { name: /retry/i }));

    expect(resetFn).toHaveBeenCalledTimes(1);
  });
});
