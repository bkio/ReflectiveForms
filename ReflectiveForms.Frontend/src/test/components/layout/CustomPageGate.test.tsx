import { describe, it, expect } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { CustomPageGate } from '../../../components/layout/CustomPageGate';
import type { CustomPage } from '../../../lib/types';

const Icon = () => <span />;
const Secret = () => <div>secret content</div>;

function renderGate(page: CustomPage) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter><CustomPageGate page={page} /></MemoryRouter>
    </QueryClientProvider>
  );
}

const base = { path: '/secret', label: 'Secret Page', icon: Icon, component: Secret };

describe('CustomPageGate', () => {
  it('renders the page directly when it has no canAccess', () => {
    renderGate(base);
    expect(screen.getByText('secret content')).toBeInTheDocument();
  });

  it('shows a spinner, then the page, when access is granted', async () => {
    renderGate({ ...base, canAccess: () => Promise.resolve(true) });
    expect(screen.getByTestId('custom-page-access-loading')).toBeInTheDocument();
    await waitFor(() => expect(screen.getByText('secret content')).toBeInTheDocument());
  });

  it('shows "No access" instead of the page when access is denied', async () => {
    renderGate({ ...base, canAccess: () => Promise.resolve(false) });
    await waitFor(() => expect(screen.getByTestId('custom-page-no-access')).toBeInTheDocument());
    expect(screen.getByText(/You don.t have access to Secret Page/)).toBeInTheDocument();
    expect(screen.queryByText('secret content')).not.toBeInTheDocument();
  });

  it('offers a retry when the check fails, and shows the page once it succeeds', async () => {
    let calls = 0;
    renderGate({ ...base, canAccess: () => (++calls <= 2 ? Promise.reject(new Error('down')) : Promise.resolve(true)) });
    // The access check retries once (~1 s) before reporting an error.
    await waitFor(() => expect(screen.getByTestId('custom-page-access-error')).toBeInTheDocument(), { timeout: 4000 });
    expect(screen.queryByText('secret content')).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: /retry/i }));
    await waitFor(() => expect(screen.getByText('secret content')).toBeInTheDocument());
  });
});
