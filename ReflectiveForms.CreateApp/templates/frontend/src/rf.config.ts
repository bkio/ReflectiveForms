import type { RfConfig } from '@reflectiveforms/frontend';

export const config: RfConfig = {
  apiBaseUrl: import.meta.env.VITE_API_BASE_URL || 'http://localhost:{{BACKEND_PORT}}/rf/api',
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
