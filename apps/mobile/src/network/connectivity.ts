import { onlineManager } from '@tanstack/react-query';
import type * as ExpoNetwork from 'expo-network';

/** `unknown` until the platform has answered; only `offline` stops the app from trying (decision M-47). */
export type ConnectionState = 'online' | 'offline' | 'unknown';

export interface Connectivity {
  getState(): ConnectionState;
  subscribe(listener: () => void): () => void;
}

/** For builds without expo-network: never claims to be offline, so requests are tried and fail as they will. */
export function createUnknownConnectivity(): Connectivity {
  return { getState: () => 'unknown', subscribe: () => () => undefined };
}

/**
 * The phone's connection from expo-network. Offline only when the platform says it is not connected at all: an
 * unconfirmed internet reachability still lets requests try, so a wrong reading can never stop answers being sent.
 */
export function createExpoConnectivity(): Connectivity {
  // eslint-disable-next-line @typescript-eslint/no-require-imports -- loaded lazily so a build without it falls back
  const network = require('expo-network') as typeof ExpoNetwork;
  let state: ConnectionState = 'unknown';
  const listeners = new Set<() => void>();
  const apply = ({ isConnected }: { isConnected?: boolean }) => {
    const next: ConnectionState = isConnected === false ? 'offline' : isConnected === true ? 'online' : 'unknown';
    if (next !== state) {
      state = next;
      listeners.forEach((listener) => listener());
    }
  };
  // For the life of the app: one subscription, shared by every screen.
  network.addNetworkStateListener(apply);
  void network.getNetworkStateAsync().then(apply, () => undefined);
  return {
    getState: () => state,
    subscribe(listener) {
      listeners.add(listener);
      return () => {
        listeners.delete(listener);
      };
    },
  };
}

/** Tells TanStack Query when the phone is offline, so queries pause instead of failing. Returns the undo. */
export function bindOnlineManager(connectivity: Connectivity): () => void {
  onlineManager.setEventListener((setOnline) => {
    const update = () => setOnline(connectivity.getState() !== 'offline');
    update();
    return connectivity.subscribe(update);
  });
  return () => {
    onlineManager.setEventListener(() => undefined);
    onlineManager.setOnline(true);
  };
}
