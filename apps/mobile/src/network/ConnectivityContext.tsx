import { createContext, useContext, useSyncExternalStore } from 'react';

import { createUnknownConnectivity, type Connectivity, type ConnectionState } from './connectivity';

const fallback = createUnknownConnectivity();
const ConnectivityContext = createContext<Connectivity>(fallback);

export const ConnectivityProvider = ConnectivityContext.Provider;

/** The phone's connection, re-rendering when it changes. */
export function useConnectionState(): ConnectionState {
  const connectivity = useContext(ConnectivityContext);
  return useSyncExternalStore(connectivity.subscribe, connectivity.getState, connectivity.getState);
}
