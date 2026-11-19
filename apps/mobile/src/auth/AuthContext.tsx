import { createContext, useContext, useSyncExternalStore } from 'react';

import type { AuthProvider, AuthState } from './types';

const AuthContext = createContext<AuthProvider | null>(null);

export const AuthContextProvider = AuthContext.Provider;

export function useAuthProvider(): AuthProvider {
  const provider = useContext(AuthContext);
  if (!provider) {
    throw new Error('useAuth must be used inside AuthContextProvider.');
  }
  return provider;
}

/** The current auth state, re-rendering on every change, and the provider to act on it. */
export function useAuth(): { state: AuthState; provider: AuthProvider } {
  const provider = useAuthProvider();
  const state = useSyncExternalStore(provider.subscribe, provider.getState, provider.getState);
  return { state, provider };
}
