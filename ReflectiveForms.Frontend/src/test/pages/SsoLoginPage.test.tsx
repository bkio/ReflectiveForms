import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { BrowserRouter } from 'react-router-dom';
import { SsoLoginPage } from '../../pages/SsoLoginPage';
import { RfConfigProvider } from '../../lib/RfConfigProvider';
import type { RfConfig } from '../../lib/types';

// Mock client
vi.mock('../../api/client', () => ({
  setApiBaseUrl: vi.fn(),
  getApiBaseUrl: vi.fn(() => 'http://test/rf/api'),
}));

function renderSsoLogin(configOverrides: Partial<RfConfig> = {}) {
  const config: RfConfig = {
    apiBaseUrl: 'http://test-api/rf/api',
    auth: { mode: 'sso', ssoLoginUrl: '/auth/sso/login' },
    ...configOverrides,
  };

  return render(
    <RfConfigProvider config={config}>
      <BrowserRouter>
        <SsoLoginPage />
      </BrowserRouter>
    </RfConfigProvider>
  );
}

describe('SsoLoginPage', () => {
  it('renders SSO login button', () => {
    renderSsoLogin();
    expect(screen.getByTestId('sso-login-button')).toBeInTheDocument();
    expect(screen.getByTestId('sso-login-button')).toHaveTextContent('Sign in with SSO');
  });

  it('shows default app name', () => {
    renderSsoLogin();
    expect(screen.getByTestId('sso-app-name')).toHaveTextContent('ReflectiveForms');
  });

  it('shows custom app name from config', () => {
    renderSsoLogin({ appName: 'School Manager' });
    expect(screen.getByTestId('sso-app-name')).toHaveTextContent('School Manager');
  });

  it('renders custom logo as image URL', () => {
    renderSsoLogin({ logo: '/school-logo.png' });
    expect(screen.getByTestId('sso-logo-img')).toBeInTheDocument();
    expect(screen.getByTestId('sso-logo-img')).toHaveAttribute('src', '/school-logo.png');
  });

  it('renders custom logo as React component', () => {
    const CustomLogo = ({ className }: { className?: string }) => (
      <svg data-testid="sso-logo-component" className={className}><circle r="10" /></svg>
    );
    renderSsoLogin({ logo: CustomLogo });
    expect(screen.getByTestId('sso-logo-component')).toBeInTheDocument();
  });

  it('SSO button triggers redirect to correct URL', () => {
    // Mock window.location
    const originalLocation = window.location;
    const mockLocation = { ...originalLocation, href: '' };
    Object.defineProperty(window, 'location', {
      writable: true,
      value: mockLocation,
    });

    renderSsoLogin({
      apiBaseUrl: 'https://api.school.edu/rf/api',
      auth: { mode: 'sso', ssoLoginUrl: '/auth/sso/login' },
    });

    screen.getByTestId('sso-login-button').click();

    expect(mockLocation.href).toBe('https://api.school.edu/rf/api/auth/sso/login');

    // Restore
    Object.defineProperty(window, 'location', {
      writable: true,
      value: originalLocation,
    });
  });
});
