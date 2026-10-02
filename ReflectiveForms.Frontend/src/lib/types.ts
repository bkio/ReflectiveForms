import type { ComponentType } from 'react';

export interface CustomPage {
  path: string;
  label: string;
  icon: ComponentType<{ className?: string }>;
  component: ComponentType;
  section?: string;
  /**
   * Whether the signed-in user may open this page. While it resolves (and if it resolves false
   * or fails) the page is left out of the sidebar and the Dashboard, and its route shows a
   * "no access" message instead of the component. Re-checked every 30 seconds and per user.
   * Omit it to show the page to everyone, as before.
   */
  canAccess?: () => Promise<boolean>;
  /** Also show this page as a card on the Dashboard (for users who may access it). Default false. */
  showOnDashboard?: boolean;
  /** One-line description for the page's Dashboard card. */
  description?: string;
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
