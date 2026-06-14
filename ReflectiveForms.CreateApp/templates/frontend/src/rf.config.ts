import type { RfConfig } from '@reflective-forms/frontend';

export const config: RfConfig = {
  // Relative path → Vite proxy handles forwarding to the backend in dev.
  // In production, set VITE_API_BASE_URL to your actual API URL (e.g. https://example.com/rf/api).
  apiBaseUrl: import.meta.env.VITE_API_BASE_URL || '/rf/api',
  appName: '{{APP_NAME}}',
  primaryColor: '{{PRIMARY_COLOR}}',
  // logo: '/logo.svg',

  // Auth mode (set during scaffolding):
  // auth: {
  //   mode: 'sso',
  //   ssoLoginUrl: '/auth/sso/login',
  // },

  // AI display settings:
  // ai: {
  //   disabled: false,         // Set true to hide all AI features in the UI
  //   aiEndpointBase: '',      // Override AI endpoint base (defaults to apiBaseUrl + '/ai')
  // },

  // Add custom pages:
  // customPages: [
  //   {
  //     path: '/analytics',
  //     label: 'Analytics',
  //     icon: BarChart3,
  //     component: AnalyticsPage,
  //     section: 'Custom',
  //   },
  // ],
};
