import { createContext, useContext, useState, useEffect, useCallback, type ReactNode } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { checkAuth as apiCheckAuth, logout as apiLogout, onUnauthorized } from '../api/client';

const USER_STORAGE_KEY = 'rf_user';

export interface UserInfo {
  id: number;
  name: string;
  email: string;
}

interface AuthContextValue {
  isAuthenticated: boolean;
  isLoading: boolean;
  user: UserInfo | null;
  login: (jwtToken?: string) => void;
  logout: () => Promise<void>;
}

function parseJwtPayload(token: string): UserInfo | null {
  try {
    const payload = JSON.parse(atob(token.split('.')[1]));
    return {
      id: Number(payload.sub) || 0,
      name: payload.name || payload.unique_name || '',
      email: payload.email || '',
    };
  } catch {
    return null;
  }
}

function loadStoredUser(): UserInfo | null {
  try {
    const raw = localStorage.getItem(USER_STORAGE_KEY);
    return raw ? JSON.parse(raw) : null;
  } catch {
    return null;
  }
}

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient();
  const [isAuthenticated, setIsAuthenticated] = useState(false);
  const [isLoading, setIsLoading] = useState(true);
  const [user, setUser] = useState<UserInfo | null>(loadStoredUser);

  const handleLoggedOut = useCallback(() => {
    setIsAuthenticated(false);
    setIsLoading(false);
  }, []);

  useEffect(() => {
    // Subscribe to global 401 events from fetchApi
    onUnauthorized(handleLoggedOut);
    return () => onUnauthorized(null);
  }, [handleLoggedOut]);

  useEffect(() => {
    let cancelled = false;
    apiCheckAuth().then((ok) => {
      if (!cancelled) {
        setIsAuthenticated(ok);
        if (!ok) {
          setUser(null);
          localStorage.removeItem(USER_STORAGE_KEY);
        }
        setIsLoading(false);
      }
    });
    return () => { cancelled = true; };
  }, []);

  const loginFn = useCallback((jwtToken?: string) => {
    setIsAuthenticated(true);
    if (jwtToken) {
      const info = parseJwtPayload(jwtToken);
      if (info) {
        setUser(info);
        localStorage.setItem(USER_STORAGE_KEY, JSON.stringify(info));
      }
    }
  }, []);

  const logoutFn = useCallback(async () => {
    await apiLogout();
    setIsAuthenticated(false);
    setUser(null);
    localStorage.removeItem(USER_STORAGE_KEY);
    queryClient.clear();
  }, [queryClient]);

  return (
    <AuthContext.Provider value={{ isAuthenticated, isLoading, user, login: loginFn, logout: logoutFn }}>
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used within AuthProvider');
  return ctx;
}
