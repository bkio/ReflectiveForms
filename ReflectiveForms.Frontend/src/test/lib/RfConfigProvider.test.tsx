import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, renderHook } from '@testing-library/react';
import { RfConfigProvider, useRfConfig } from '../../lib/RfConfigProvider';
import { setApiBaseUrl } from '../../api/client';
import type { RfConfig } from '../../lib/types';

// Mock setApiBaseUrl to track calls
vi.mock('../../api/client', () => ({
  setApiBaseUrl: vi.fn(),
  getApiBaseUrl: vi.fn(() => 'http://test-api/rf/api'),
  setAiBaseUrl: vi.fn(),
  setAiDisabled: vi.fn(),
}));

function makeConfig(overrides: Partial<RfConfig> = {}): RfConfig {
  return {
    apiBaseUrl: 'http://test-api/rf/api',
    ...overrides,
  };
}

describe('RfConfigProvider', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  afterEach(() => {
    // Clean up CSS variable
    document.documentElement.style.removeProperty('--rf-primary');
  });

  it('renders children', () => {
    render(
      <RfConfigProvider config={makeConfig()}>
        <div data-testid="child">Hello</div>
      </RfConfigProvider>
    );
    expect(screen.getByTestId('child')).toBeInTheDocument();
  });

  it('provides config via useRfConfig', () => {
    const config = makeConfig({ appName: 'TestApp', primaryColor: '#ff0000' });

    const { result } = renderHook(() => useRfConfig(), {
      wrapper: ({ children }) => (
        <RfConfigProvider config={config}>{children}</RfConfigProvider>
      ),
    });

    expect(result.current.apiBaseUrl).toBe('http://test-api/rf/api');
    expect(result.current.appName).toBe('TestApp');
    expect(result.current.primaryColor).toBe('#ff0000');
  });

  it('throws when useRfConfig is used outside provider', () => {
    // Suppress console.error from React for this test
    const spy = vi.spyOn(console, 'error').mockImplementation(() => {});

    expect(() => {
      renderHook(() => useRfConfig());
    }).toThrow('useRfConfig must be used within a <RfConfigProvider>');

    spy.mockRestore();
  });

  it('calls setApiBaseUrl with the configured URL', () => {
    render(
      <RfConfigProvider config={makeConfig({ apiBaseUrl: 'http://my-api.example.com/rf/api' })}>
        <div />
      </RfConfigProvider>
    );

    expect(setApiBaseUrl).toHaveBeenCalledWith('http://my-api.example.com/rf/api');
  });

  it('updates setApiBaseUrl when apiBaseUrl changes', () => {
    const { rerender } = render(
      <RfConfigProvider config={makeConfig({ apiBaseUrl: 'http://first/api' })}>
        <div />
      </RfConfigProvider>
    );

    expect(setApiBaseUrl).toHaveBeenCalledWith('http://first/api');

    rerender(
      <RfConfigProvider config={makeConfig({ apiBaseUrl: 'http://second/api' })}>
        <div />
      </RfConfigProvider>
    );

    expect(setApiBaseUrl).toHaveBeenCalledWith('http://second/api');
  });

  it('sets --rf-primary CSS variable to primaryColor', () => {
    render(
      <RfConfigProvider config={makeConfig({ primaryColor: '#e11d48' })}>
        <div />
      </RfConfigProvider>
    );

    expect(document.documentElement.style.getPropertyValue('--rf-primary')).toBe('#e11d48');
  });

  it('sets --rf-primary to default blue when primaryColor not provided', () => {
    render(
      <RfConfigProvider config={makeConfig()}>
        <div />
      </RfConfigProvider>
    );

    expect(document.documentElement.style.getPropertyValue('--rf-primary')).toBe('#2563eb');
  });

  it('provides default values for optional config fields', () => {
    const { result } = renderHook(() => useRfConfig(), {
      wrapper: ({ children }) => (
        <RfConfigProvider config={makeConfig()}>{children}</RfConfigProvider>
      ),
    });

    expect(result.current.appName).toBeUndefined();
    expect(result.current.logo).toBeUndefined();
    expect(result.current.primaryColor).toBeUndefined();
    expect(result.current.basePath).toBeUndefined();
    expect(result.current.auth).toBeUndefined();
    expect(result.current.customPages).toBeUndefined();
    expect(result.current.overrides).toBeUndefined();
  });

  it('passes full config with all optional fields', () => {
    const TestIcon = ({ className }: { className?: string }) => <span className={className}>icon</span>;
    const CustomDashboard = () => <div>custom dashboard</div>;
    const CustomLogin = () => <div>custom login</div>;
    const AnalyticsPage = () => <div>analytics</div>;

    const fullConfig = makeConfig({
      appName: 'School Manager',
      logo: '/logo.svg',
      primaryColor: '#059669',
      basePath: '/admin',
      auth: { mode: 'sso', ssoLoginUrl: '/auth/sso/login' },
      customPages: [{
        path: '/analytics',
        label: 'Analytics',
        icon: TestIcon,
        component: AnalyticsPage,
        section: 'Custom',
      }],
      overrides: {
        DashboardPage: CustomDashboard,
        LoginPage: CustomLogin,
      },
    });

    const { result } = renderHook(() => useRfConfig(), {
      wrapper: ({ children }) => (
        <RfConfigProvider config={fullConfig}>{children}</RfConfigProvider>
      ),
    });

    expect(result.current.appName).toBe('School Manager');
    expect(result.current.logo).toBe('/logo.svg');
    expect(result.current.primaryColor).toBe('#059669');
    expect(result.current.basePath).toBe('/admin');
    expect(result.current.auth?.mode).toBe('sso');
    expect(result.current.auth?.ssoLoginUrl).toBe('/auth/sso/login');
    expect(result.current.customPages).toHaveLength(1);
    expect(result.current.customPages![0].label).toBe('Analytics');
    expect(result.current.overrides?.DashboardPage).toBe(CustomDashboard);
  });

  it('provides config to deeply nested children', () => {
    function DeepChild() {
      const config = useRfConfig();
      return <div data-testid="deep">{config.appName}</div>;
    }

    render(
      <RfConfigProvider config={makeConfig({ appName: 'Deep Test' })}>
        <div>
          <div>
            <DeepChild />
          </div>
        </div>
      </RfConfigProvider>
    );

    expect(screen.getByTestId('deep')).toHaveTextContent('Deep Test');
  });

  it('allows logo as a React component', () => {
    const LogoComponent = ({ className }: { className?: string }) => (
      <svg className={className} data-testid="logo-svg"><circle r="10" /></svg>
    );

    const { result } = renderHook(() => useRfConfig(), {
      wrapper: ({ children }) => (
        <RfConfigProvider config={makeConfig({ logo: LogoComponent })}>{children}</RfConfigProvider>
      ),
    });

    expect(typeof result.current.logo).toBe('function');
  });
});
