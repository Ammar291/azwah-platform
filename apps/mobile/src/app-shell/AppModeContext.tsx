import { createContext, useContext } from 'react';

import type { AppMode } from '@/config';

const AppModeContext = createContext<AppMode>('api');

export const AppModeProvider = AppModeContext.Provider;

/** `development_fixtures` means simulated sign-in and synthetic data; screens must say so. */
export function useAppMode(): AppMode {
  return useContext(AppModeContext);
}
