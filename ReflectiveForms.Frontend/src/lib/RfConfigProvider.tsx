import React, { createContext, useContext, useEffect } from 'react';
import type { RfConfig } from './types';
import { setApiBaseUrl, setAiBaseUrl, setAiDisabled } from '../api/client';

const RfConfigContext = createContext<RfConfig | null>(null);

export function useRfConfig(): RfConfig {
  const config = useContext(RfConfigContext);
  if (!config) {
    throw new Error('useRfConfig must be used within a <RfConfigProvider>');
  }
  return config;
}

interface RfConfigProviderProps {
  config: RfConfig;
  children: React.ReactNode;
}

export function RfConfigProvider({ config, children }: RfConfigProviderProps) {
  useEffect(() => {
    setApiBaseUrl(config.apiBaseUrl);
  }, [config.apiBaseUrl]);

  // Wire AI configuration
  useEffect(() => {
    setAiBaseUrl(config.ai?.aiEndpointBase ?? null);
    setAiDisabled(config.ai?.disabled ?? false);
  }, [config.ai?.aiEndpointBase, config.ai?.disabled]);

  // Apply primary color CSS variable
  useEffect(() => {
    const color = config.primaryColor ?? '#2563eb';
    document.documentElement.style.setProperty('--rf-primary', color);
  }, [config.primaryColor]);

  return (
    <RfConfigContext.Provider value={config}>
      {children}
    </RfConfigContext.Provider>
  );
}
