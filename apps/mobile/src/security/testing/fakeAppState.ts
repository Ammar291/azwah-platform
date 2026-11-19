import type { AppStateSource, AppStateValue } from '../lockController';

/** React Native's AppState for tests: `change` moves the app between the foreground and the background. */
export function createFakeAppState(initial: AppStateValue = 'active') {
  const listeners = new Set<(state: AppStateValue) => void>();
  const source = {
    currentState: initial,
    addEventListener(_type: 'change', listener: (state: AppStateValue) => void) {
      listeners.add(listener);
      return { remove: () => void listeners.delete(listener) };
    },
    change(next: AppStateValue) {
      source.currentState = next;
      [...listeners].forEach((listener) => listener(next));
    },
    get listenerCount() {
      return listeners.size;
    },
  };
  return source satisfies AppStateSource;
}

export type FakeAppState = ReturnType<typeof createFakeAppState>;
