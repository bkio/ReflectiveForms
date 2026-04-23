import type { ComponentType } from 'react';

export interface CustomPage {
  path: string;
  label: string;
  icon: ComponentType<{ className?: string }>;
  component: ComponentType;
  section?: string;
}

export interface RfConfig {
  apiBaseUrl: string;
  appName?: string;
  logo?: string | ComponentType<{ className?: string }>;
  primaryColor?: string;
  basePath?: string;
  auth?: {
    mode: 'local' | 'sso';
    ssoLoginUrl?: string;
  };
  customPages?: CustomPage[];
  overrides?: {
    LoginPage?: ComponentType;
    DashboardPage?: ComponentType;
  };
  ai?: {
    /** Override the AI endpoint base. Defaults to apiBaseUrl + '/ai'. */
    aiEndpointBase?: string;
    /** Disable all AI features on the frontend regardless of backend schema flags. */
    disabled?: boolean;
  };
}
