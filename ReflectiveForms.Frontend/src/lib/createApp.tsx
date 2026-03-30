import React from 'react';
import ReactDOM from 'react-dom/client';
import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { Toaster } from 'sonner';

import { ErrorBoundary } from '../components/ErrorBoundary';
import { RfConfigProvider } from './RfConfigProvider';
import { AuthProvider, useAuth } from '../hooks/useAuth';
import { AdminLayout } from '../components/layout/AdminLayout';
import { RfRoutes } from './RfRoutes';
import { LoginPage } from '../pages/LoginPage';
import { SsoLoginPage } from '../pages/SsoLoginPage';
import type { RfConfig } from './types';

import '../index.css';

function RequireAuth({ children }: { children: React.ReactElement }) {
  const { isAuthenticated, isLoading } = useAuth();
  if (isLoading) {
    return (
      <div className="min-h-screen flex items-center justify-center">
        <div className="animate-spin rounded-full h-10 w-10 border-b-2 border-primary-600" />
      </div>
    );
  }
  if (!isAuthenticated) return <Navigate to="/login" replace />;
  return children;
}

export function createReflectiveFormsApp(config: RfConfig) {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: {
        staleTime: 1000 * 60 * 5,
        retry: 1,
      },
    },
  });

  const LoginComponent =
    config.auth?.mode === 'sso'
      ? SsoLoginPage
      : config.overrides?.LoginPage ?? LoginPage;

  const customRoutes = config.customPages?.map((page) => (
    <Route key={page.path} path={page.path} element={<page.component />} />
  ));

  const DashboardOverride = config.overrides?.DashboardPage;

  const root = document.getElementById('root');
  if (!root) throw new Error('Root element #root not found');

  ReactDOM.createRoot(root).render(
    <React.StrictMode>
      <ErrorBoundary>
        <RfConfigProvider config={config}>
          <QueryClientProvider client={queryClient}>
            <AuthProvider>
              <BrowserRouter
                basename={config.basePath ?? '/'}
                future={{ v7_startTransition: true, v7_relativeSplatPath: true }}
              >
                <Routes>
                  <Route path="/login" element={<LoginComponent />} />
                  <Route
                    element={
                      <RequireAuth>
                        <AdminLayout />
                      </RequireAuth>
                    }
                  >
                    {DashboardOverride ? (
                      <Route path="/" element={<DashboardOverride />} />
                    ) : null}
                    {RfRoutes()}
                    {customRoutes}
                  </Route>
                </Routes>
              </BrowserRouter>
            </AuthProvider>
            <Toaster position="top-right" richColors />
          </QueryClientProvider>
        </RfConfigProvider>
      </ErrorBoundary>
    </React.StrictMode>
  );
}
