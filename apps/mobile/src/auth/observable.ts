import type { AuthState } from './types';

/** A tiny store for auth state that React reads with useSyncExternalStore. */
export function createAuthStateStore(initial: AuthState) {
  let state = initial;
  const listeners = new Set<() => void>();
  return {
    get: () => state,
    set(next: AuthState) {
      state = next;
      listeners.forEach((listener) => listener());
    },
    subscribe(listener: () => void) {
      listeners.add(listener);
      return () => {
        listeners.delete(listener);
      };
    },
  };
}
