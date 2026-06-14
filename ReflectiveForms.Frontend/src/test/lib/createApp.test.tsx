import { describe, it, expect, vi, afterEach } from 'vitest';
import { createReflectiveFormsApp } from '../../lib/createApp';
import type { RfConfig } from '../../lib/types';

// Mock ReactDOM.createRoot to capture render calls without real DOM
const mockRender = vi.fn();
vi.mock('react-dom/client', () => ({
  default: {
    createRoot: vi.fn(() => ({ render: mockRender })),
  },
  createRoot: vi.fn(() => ({ render: mockRender })),
}));

// Mock client setApiBaseUrl
vi.mock('../../api/client', () => ({
  setApiBaseUrl: vi.fn(),
  setAiBaseUrl: vi.fn(),
  setAiDisabled: vi.fn(),
  getApiBaseUrl: vi.fn(() => 'http://test/rf/api'),
}));

// Mock RfRoutes — it calls hooks (useGlobalSettings → useQuery) which require
// a React render context. Since createRoot().render() is mocked, the JSX element
// tree is built but never rendered, so RfRoutes() (a direct function call in JSX)
// would execute hooks outside React's reconciler.
vi.mock('../../lib/RfRoutes', () => ({
  RfRoutes: () => null,
}));

function makeConfig(overrides: Partial<RfConfig> = {}): RfConfig {
  return {
    apiBaseUrl: 'http://test/rf/api',
    ...overrides,
  };
}

describe('createReflectiveFormsApp', () => {
  afterEach(() => {
    vi.clearAllMocks();
    // Clean up any root element
    const root = document.getElementById('root');
    if (root) root.remove();
  });

  function addRootElement() {
    const root = document.createElement('div');
    root.id = 'root';
    document.body.appendChild(root);
    return root;
  }

  it('renders into the #root element', () => {
    addRootElement();
    createReflectiveFormsApp(makeConfig());
    expect(mockRender).toHaveBeenCalledTimes(1);
  });

  it('throws if #root element is missing', () => {
    expect(() => createReflectiveFormsApp(makeConfig())).toThrow('Root element #root not found');
  });

  it('accepts custom basePath', () => {
    addRootElement();
    // Should not throw
    expect(() => createReflectiveFormsApp(makeConfig({ basePath: '/admin' }))).not.toThrow();
    expect(mockRender).toHaveBeenCalledTimes(1);
  });

  it('accepts custom pages in config', () => {
    addRootElement();
    const TestPage = () => null;
    const TestIcon = () => null;

    expect(() => createReflectiveFormsApp(makeConfig({
      customPages: [{
        path: '/analytics',
        label: 'Analytics',
        icon: TestIcon,
        component: TestPage,
        section: 'Reports',
      }],
    }))).not.toThrow();
  });

  it('accepts SSO auth mode', () => {
    addRootElement();
    expect(() => createReflectiveFormsApp(makeConfig({
      auth: { mode: 'sso', ssoLoginUrl: '/auth/sso/login' },
    }))).not.toThrow();
  });

  it('accepts component overrides', () => {
    addRootElement();
    const CustomDashboard = () => null;
    const CustomLogin = () => null;

    expect(() => createReflectiveFormsApp(makeConfig({
      overrides: {
        DashboardPage: CustomDashboard,
        LoginPage: CustomLogin,
      },
    }))).not.toThrow();
  });

  it('accepts branding config', () => {
    addRootElement();
    expect(() => createReflectiveFormsApp(makeConfig({
      appName: 'School Manager',
      logo: '/logo.svg',
      primaryColor: '#e11d48',
    }))).not.toThrow();
  });
});
