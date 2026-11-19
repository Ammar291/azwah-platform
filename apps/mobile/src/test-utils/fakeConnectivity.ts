import type { Connectivity, ConnectionState } from '@/network/connectivity';

export interface FakeConnectivity extends Connectivity {
  set(state: ConnectionState): void;
}

/** A connection a test switches by hand: `set('offline')`, then `set('online')` to see the app recover. */
export function createFakeConnectivity(initial: ConnectionState = 'online'): FakeConnectivity {
  let state = initial;
  const listeners = new Set<() => void>();
  return {
    getState: () => state,
    subscribe(listener) {
      listeners.add(listener);
      return () => {
        listeners.delete(listener);
      };
    },
    set(next) {
      if (next !== state) {
        state = next;
        [...listeners].forEach((listener) => listener());
      }
    },
  };
}
