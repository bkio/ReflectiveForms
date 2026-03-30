import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { AutoSaveIndicator } from '../../../components/form/AutoSaveIndicator';

const defaultProps = {
  status: 'idle' as const,
  countdownRemaining: 0,
  countdownTotal: 3000,
  validationErrors: [] as string[],
  error: null as string | null,
  onDismissValidation: vi.fn(),
};

describe('AutoSaveIndicator', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renders nothing when idle', () => {
    const { container } = render(<AutoSaveIndicator {...defaultProps} />);
    expect(container.firstChild).toBeNull();
  });

  it('shows "Validating..." when checking', () => {
    render(<AutoSaveIndicator {...defaultProps} status="checking" />);
    expect(screen.getByTestId('autosave-checking')).toHaveTextContent('Validating...');
  });

  it('shows validation errors with dismiss button', () => {
    const onDismiss = vi.fn();
    render(
      <AutoSaveIndicator
        {...defaultProps}
        status="validation-error"
        validationErrors={['Title is required', 'Content too short']}
        onDismissValidation={onDismiss}
      />
    );
    expect(screen.getByTestId('autosave-validation-error')).toBeInTheDocument();
    expect(screen.getByText('Title is required')).toBeInTheDocument();
    expect(screen.getByText('Content too short')).toBeInTheDocument();
  });

  it('dismiss button calls onDismissValidation', async () => {
    const user = userEvent.setup();
    const onDismiss = vi.fn();
    render(
      <AutoSaveIndicator
        {...defaultProps}
        status="validation-error"
        validationErrors={['Error']}
        onDismissValidation={onDismiss}
      />
    );
    await user.click(screen.getByTestId('autosave-dismiss'));
    expect(onDismiss).toHaveBeenCalledOnce();
  });

  it('shows countdown with seconds remaining', () => {
    render(
      <AutoSaveIndicator
        {...defaultProps}
        status="countdown"
        countdownRemaining={2500}
        countdownTotal={3000}
      />
    );
    expect(screen.getByTestId('autosave-countdown')).toHaveTextContent('Saving in 3s...');
  });

  it('shows progress bar during countdown', () => {
    render(
      <AutoSaveIndicator
        {...defaultProps}
        status="countdown"
        countdownRemaining={1500}
        countdownTotal={3000}
      />
    );
    const bar = screen.getByTestId('autosave-progress');
    expect(bar).toBeInTheDocument();
    expect(bar.style.width).toBe('50%');
  });

  it('shows "Saving..." during save', () => {
    render(<AutoSaveIndicator {...defaultProps} status="saving" />);
    expect(screen.getByTestId('autosave-saving')).toHaveTextContent('Saving...');
  });

  it('shows "Saved!" after save completes', () => {
    render(<AutoSaveIndicator {...defaultProps} status="saved" />);
    expect(screen.getByTestId('autosave-saved')).toHaveTextContent('Saved!');
  });

  it('shows error message on failure', () => {
    render(<AutoSaveIndicator {...defaultProps} status="error" error="Network error" />);
    expect(screen.getByTestId('autosave-error')).toHaveTextContent('Network error');
  });

  it('shows default error message when error is null', () => {
    render(<AutoSaveIndicator {...defaultProps} status="error" error={null} />);
    expect(screen.getByTestId('autosave-error')).toHaveTextContent('Save failed');
  });
});
