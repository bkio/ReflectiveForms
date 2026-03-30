import type { RfConfig } from '@reflectiveforms/frontend';

export const config: RfConfig = {
  apiBaseUrl: import.meta.env.VITE_API_URL || 'http://localhost:{{BACKEND_PORT}}/rf/api',
  appName: '{{APP_NAME}}',
  primaryColor: '{{PRIMARY_COLOR}}',
  // logo: '/logo.svg',

  // Uncomment for SSO:
  // auth: {
  //   mode: 'sso',
  //   ssoLoginUrl: '/auth/sso/login',
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
