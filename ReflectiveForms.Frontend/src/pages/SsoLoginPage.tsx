import { useRfConfig } from '../lib/RfConfigProvider';
import { FileText } from 'lucide-react';

export function SsoLoginPage() {
  const config = useRfConfig();
  const appName = config.appName ?? 'ReflectiveForms';
  const Logo = config.logo;
  const ssoUrl = `${config.apiBaseUrl}${config.auth?.ssoLoginUrl ?? '/auth/sso/login'}`;

  const handleSsoLogin = () => {
    window.location.href = ssoUrl;
  };

  return (
    <div className="min-h-screen bg-gray-50 flex items-center justify-center px-4">
      <div className="max-w-sm w-full bg-white rounded-lg shadow-sm border border-gray-200 p-8 space-y-6">
        <div className="flex flex-col items-center gap-3">
          {typeof Logo === 'string' ? (
            <img src={Logo} alt={appName} className="w-12 h-12" data-testid="sso-logo-img" />
          ) : Logo ? (
            <Logo className="w-12 h-12 text-primary-600" data-testid="sso-logo-component" />
          ) : (
            <FileText className="w-12 h-12 text-primary-600" />
          )}
          <h1 className="text-2xl font-bold text-gray-900" data-testid="sso-app-name">{appName}</h1>
          <p className="text-sm text-gray-500">Sign in to continue</p>
        </div>

        <button
          onClick={handleSsoLogin}
          data-testid="sso-login-button"
          className="w-full px-4 py-2.5 bg-primary-600 text-white rounded-md hover:bg-primary-700 transition-colors font-medium"
        >
          Sign in with SSO
        </button>
      </div>
    </div>
  );
}
