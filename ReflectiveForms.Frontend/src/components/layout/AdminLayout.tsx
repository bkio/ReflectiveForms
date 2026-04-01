import { useState, useEffect, useMemo, useCallback } from 'react';
import { Link, Outlet, useParams, useLocation } from 'react-router-dom';
import { Menu, X, ChevronRight, Home, Sun, Moon, LogOut, FileText, Settings } from 'lucide-react';
import { useAllSchemas, useCapabilities } from '../../hooks/useEntity';
import { useRfConfig } from '../../lib/RfConfigProvider';
import { useAuth } from '../../hooks/useAuth';
import type { CustomPage } from '../../lib/types';

function getCookie(name: string): string | null {
  const match = document.cookie.match(new RegExp('(?:^|; )' + name + '=([^;]*)'));
  return match ? decodeURIComponent(match[1]) : null;
}

function setCookie(name: string, value: string, days = 365) {
  const expires = new Date(Date.now() + days * 864e5).toUTCString();
  document.cookie = `${name}=${encodeURIComponent(value)}; expires=${expires}; path=/; SameSite=Lax`;
}

export function AdminLayout() {
  const [sidebarOpen, setSidebarOpen] = useState(true);
  const [isMobile, setIsMobile] = useState(false);
  const [darkMode, setDarkMode] = useState(() => getCookie('rf_dark_mode') === 'true');

  // Apply dark class to <html>
  useEffect(() => {
    document.documentElement.classList.toggle('dark', darkMode);
    setCookie('rf_dark_mode', String(darkMode));
  }, [darkMode]);

  const toggleDarkMode = useCallback(() => setDarkMode(prev => !prev), []);
  const { entityName: currentEntity } = useParams();
  const location = useLocation();
  const { data: schemas, isLoading } = useAllSchemas();
  const { data: capabilities } = useCapabilities();

  // Auth
  let auth: ReturnType<typeof useAuth> | null = null;
  try {
    auth = useAuth();
  } catch {
    // Not wrapped in AuthProvider
  }

  // Read config — may be absent if AdminLayout is used outside RfConfigProvider
  let config: ReturnType<typeof useRfConfig> | null = null;
  try {
    config = useRfConfig();
  } catch {
    // Not wrapped in RfConfigProvider — use defaults
  }

  const appName = config?.appName ?? 'ReflectiveForms';
  const Logo = config?.logo;

  // Group custom pages by section
  const customPageSections = useMemo(() => {
    const pages = config?.customPages ?? [];
    const sections: Record<string, CustomPage[]> = {};
    for (const page of pages) {
      const section = page.section ?? 'Custom';
      if (!sections[section]) sections[section] = [];
      sections[section].push(page);
    }
    return sections;
  }, [config?.customPages]);

  // Handle responsive behavior
  useEffect(() => {
    const checkMobile = () => {
      const mobile = window.innerWidth < 1024;
      setIsMobile(mobile);
      if (mobile) {
        setSidebarOpen(false);
      }
    };

    checkMobile();
    window.addEventListener('resize', checkMobile);
    return () => window.removeEventListener('resize', checkMobile);
  }, []);

  // Close sidebar on navigation on mobile
  useEffect(() => {
    if (isMobile) {
      setSidebarOpen(false);
    }
  }, [location.pathname, isMobile]);

  const entityTypes = Object.values(schemas ?? {}).filter(
    (s) => !capabilities || capabilities[s.entity_name]?.can_peek_all
  );

  return (
    <div className="min-h-screen bg-gray-100 dark:bg-gray-900 dark:text-gray-100">
      {/* Overlay for mobile */}
      {sidebarOpen && isMobile && (
        <div
          className="fixed inset-0 bg-black bg-opacity-50 z-40"
          onClick={() => setSidebarOpen(false)}
        />
      )}

      {/* Sidebar */}
      <aside
        className={`
          fixed inset-y-0 left-0 z-50 w-64 bg-white dark:bg-gray-800 shadow-lg transform transition-transform duration-200
          ${sidebarOpen ? 'translate-x-0' : '-translate-x-full'}
          lg:translate-x-0
        `}
      >
        {/* Logo/Brand */}
        <div className="flex items-center justify-between h-16 px-4 border-b border-gray-200 bg-gradient-to-r from-primary-600 to-primary-700">
          <Link to="/" className="flex items-center gap-2">
            {typeof Logo === 'string' ? (
              <img src={Logo} alt={appName} className="w-6 h-6" />
            ) : Logo ? (
              <Logo className="w-6 h-6 text-white" />
            ) : (
              <FileText className="w-6 h-6 text-white" />
            )}
            <span className="text-lg font-bold text-white" data-testid="brand-name">{appName}</span>
          </Link>
          <button
            onClick={() => setSidebarOpen(false)}
            className="lg:hidden p-1 text-white/80 hover:text-white"
          >
            <X className="w-5 h-5" />
          </button>
        </div>

        {/* Navigation */}
        <nav className="flex-1 overflow-y-auto p-4">
          {/* Dashboard link */}
          <Link
            to="/"
            className={`
              flex items-center gap-3 px-3 py-2.5 rounded-lg mb-2 transition-colors
              ${location.pathname === '/'
                ? 'bg-primary-50 text-primary-700'
                : 'text-gray-600 hover:bg-gray-100'
              }
            `}
          >
            <Home className="w-5 h-5" />
            <span className="font-medium">Dashboard</span>
          </Link>

          {/* Entity Types Section */}
          <div className="mt-6">
            <p className="text-xs font-semibold text-gray-400 uppercase tracking-wider mb-3 px-3">
              Content Types
            </p>

            {isLoading ? (
              <div className="space-y-2 px-3">
                {[1, 2, 3].map((i) => (
                  <div key={i} className="h-10 bg-gray-100 rounded animate-pulse" />
                ))}
              </div>
            ) : (
              <ul className="space-y-1">
                {entityTypes.map((schema) => {
                  const isActive = currentEntity === schema.entity_name;
                  return (
                    <li key={schema.entity_name}>
                      <Link
                        to={`/entities/${schema.entity_name}`}
                        className={`
                          flex items-center gap-3 px-3 py-2.5 rounded-lg transition-colors
                          ${isActive
                            ? 'bg-primary-50 text-primary-700 font-medium'
                            : 'text-gray-600 hover:bg-gray-100'
                          }
                        `}
                      >
                        <ChevronRight className={`w-4 h-4 transition-transform ${isActive ? 'rotate-90' : ''}`} />
                        <span>{schema.readable_name.plural}</span>
                      </Link>
                    </li>
                  );
                })}

                {entityTypes.length === 0 && !isLoading && (
                  <li className="px-3 py-4 text-center text-gray-400 text-sm">
                    No entity types available
                  </li>
                )}
              </ul>
            )}
          </div>

          {/* Custom Pages Sections */}
          {Object.entries(customPageSections).map(([section, pages]) => (
            <div key={section} className="mt-6" data-testid={`custom-section-${section.toLowerCase().replace(/\s+/g, '-')}`}>
              <p className="text-xs font-semibold text-gray-400 uppercase tracking-wider mb-3 px-3">
                {section}
              </p>
              <ul className="space-y-1">
                {pages.map((page) => {
                  const isActive = location.pathname === page.path;
                  const PageIcon = page.icon;
                  return (
                    <li key={page.path}>
                      <Link
                        to={page.path}
                        className={`
                          flex items-center gap-3 px-3 py-2.5 rounded-lg transition-colors
                          ${isActive
                            ? 'bg-primary-50 text-primary-700 font-medium'
                            : 'text-gray-600 hover:bg-gray-100'
                          }
                        `}
                      >
                        <PageIcon className="w-5 h-5" />
                        <span>{page.label}</span>
                      </Link>
                    </li>
                  );
                })}
              </ul>
            </div>
          ))}
        </nav>

        {/* Footer */}
        <div className="border-t border-gray-200 dark:border-gray-700 p-4">
          <div className="flex items-center gap-3">
            <div className="w-8 h-8 bg-primary-100 rounded-full flex items-center justify-center">
              <span className="text-primary-600 font-medium text-sm">
                {(auth?.user?.name ?? 'A').charAt(0).toUpperCase()}
              </span>
            </div>
            <div className="flex-1 min-w-0">
              <p className="text-sm font-medium text-gray-900 dark:text-gray-100 truncate">{auth?.user?.name || 'Admin'}</p>
              <p className="text-xs text-gray-500 dark:text-gray-400 truncate">{auth?.user?.email || ''}</p>
            </div>
            <button
              onClick={toggleDarkMode}
              className="p-2 text-gray-400 hover:text-gray-600 dark:hover:text-gray-200 rounded-lg hover:bg-gray-100 dark:hover:bg-gray-700"
              title={darkMode ? 'Light mode' : 'Dark mode'}
            >
              {darkMode ? <Sun className="w-4 h-4" /> : <Moon className="w-4 h-4" />}
            </button>
          </div>
        </div>
      </aside>

      {/* Main content area */}
      <div className="lg:ml-64 min-h-screen">
        {/* Top bar */}
        <header className="sticky top-0 z-30 h-16 bg-white dark:bg-gray-800 border-b border-gray-200 dark:border-gray-700 flex items-center px-4 gap-4">
          <button
            onClick={() => setSidebarOpen(!sidebarOpen)}
            className="lg:hidden p-2 text-gray-500 hover:text-gray-700 hover:bg-gray-100 rounded-lg"
          >
            <Menu className="w-5 h-5" />
          </button>

          {/* Breadcrumb */}
          <div className="flex items-center gap-2 text-sm">
            <Link to="/" className="text-gray-500 hover:text-gray-700">
              Home
            </Link>
            {currentEntity && (
              <>
                <ChevronRight className="w-4 h-4 text-gray-400" />
                <span className="text-gray-900 font-medium">
                  {schemas?.[currentEntity]?.readable_name.plural ?? currentEntity}
                </span>
              </>
            )}
          </div>

          <div className="flex-1" />

          {/* Quick actions */}
          <div className="flex items-center gap-2">
            <button
              className="p-2 text-gray-500 dark:text-gray-400 hover:text-gray-700 dark:hover:text-gray-200 hover:bg-gray-100 dark:hover:bg-gray-700 rounded-lg"
              title="Settings"
            >
              <Settings className="w-5 h-5" />
            </button>
            <button
              onClick={toggleDarkMode}
              className="p-2 text-gray-500 dark:text-gray-400 hover:text-gray-700 dark:hover:text-gray-200 hover:bg-gray-100 dark:hover:bg-gray-700 rounded-lg"
              title={darkMode ? 'Light mode' : 'Dark mode'}
            >
              {darkMode ? <Sun className="w-5 h-5" /> : <Moon className="w-5 h-5" />}
            </button>
            <button
              className="p-2 text-gray-500 dark:text-gray-400 hover:text-red-600 hover:bg-red-50 dark:hover:bg-red-900/30 rounded-lg"
              title="Logout"
              onClick={() => auth?.logout()}
            >
              <LogOut className="w-5 h-5" />
            </button>
          </div>
        </header>

        {/* Page content */}
        <main className="p-4 lg:p-6">
          <Outlet />
        </main>
      </div>
    </div>
  );
}
