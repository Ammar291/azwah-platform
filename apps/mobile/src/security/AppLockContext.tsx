import { createContext, useContext, useEffect, useMemo, useState, useSyncExternalStore, type ReactNode } from 'react';
import { AppState } from 'react-native';

import { useAuthProvider } from '@/auth/AuthContext';

import type { AuthenticationPrompt, DeviceAuthentication, DeviceAuthenticationOutcome } from './deviceAuthentication';
import { createExpoDeviceAuthentication } from './expoDeviceAuthentication';
import { LockController, type AppStateSource, type LockState, type LockTimers } from './lockController';

/** What the lock needs from the phone. The app uses React Native's AppState and expo-local-authentication. */
export interface AppLockDependencies {
  appState: AppStateSource;
  authentication: DeviceAuthentication;
  timers?: LockTimers;
}

export interface AppLock extends LockState {
  unlock(prompt: AuthenticationPrompt): Promise<DeviceAuthenticationOutcome>;
  isAuthenticationAvailable(): Promise<boolean>;
  /** Call on every touch; it never handles the touch itself. */
  touched(): void;
  /** The signed-in screen on show, so unlocking can return to it. */
  noteLocation(href: string): void;
  /** Read at the last moment before showing something sensitive: unlocked and in the foreground. */
  mayRevealContent(): boolean;
  /** Called each time the lock engages; returns the unsubscribe. */
  onLock(listener: () => void): () => void;
}

const AppLockContext = createContext<LockController | null>(null);

export interface AppLockProviderProps {
  children: ReactNode;
  /** Tests pass fakes; anything left out is the real device. */
  dependencies?: Partial<AppLockDependencies>;
  /** False only for the reload that applies a language the person just chose (bootstrap.ts). */
  lockRestoredSession?: boolean;
}

/**
 * Runs the inactivity lock (spec §4) for whoever is signed in. The controller is made, and told the current sign-in,
 * during the first render, so a restored session is locked before any signed-in screen can mount.
 */
export function AppLockProvider({ children, dependencies, lockRestoredSession = true }: AppLockProviderProps) {
  const auth = useAuthProvider();
  const [{ controller, appState }] = useState(() => {
    const source = dependencies?.appState ?? AppState;
    const created = new LockController({
      authentication: dependencies?.authentication ?? createExpoDeviceAuthentication(),
      appState: source.currentState,
      timers: dependencies?.timers,
      lockRestoredSession,
    });
    created.authStateChanged(auth.getState().status);
    return { controller: created, appState: source };
  });

  useEffect(() => {
    const sync = () => controller.authStateChanged(auth.getState().status);
    sync();
    return auth.subscribe(sync);
  }, [auth, controller]);

  useEffect(() => {
    controller.appStateChanged(appState.currentState);
    const subscription = appState.addEventListener('change', (next) => controller.appStateChanged(next));
    return () => subscription.remove();
  }, [appState, controller]);

  useEffect(() => {
    controller.start();
    return () => controller.stop();
  }, [controller]);

  return <AppLockContext.Provider value={controller}>{children}</AppLockContext.Provider>;
}

const UNLOCKED_STATE: LockState = { locked: false, covered: false, unlocking: false, resumeHref: null };
const noStore = { subscribe: () => () => undefined, getState: () => UNLOCKED_STATE };

/**
 * The lock and what can be done with it. Outside an AppLockProvider (screen tests render screens alone) the app is
 * never locked; the root layout always has one (src/app-shell/AppShell.tsx).
 */
export function useAppLock(): AppLock {
  const controller = useContext(AppLockContext);
  const store = controller ?? noStore;
  const state = useSyncExternalStore(store.subscribe, store.getState, store.getState);
  // Stable functions, so screens can use them as effect dependencies.
  const actions = useMemo<Omit<AppLock, keyof LockState>>(
    () => ({
      unlock: (prompt) => controller?.unlock(prompt) ?? Promise.resolve('success'),
      isAuthenticationAvailable: () => controller?.isAuthenticationAvailable() ?? Promise.resolve(false),
      touched: () => controller?.touched(),
      noteLocation: (href) => controller?.noteLocation(href),
      // Without a lock (screens rendered alone) only the app state can say no.
      mayRevealContent: () => controller?.mayRevealContent() ?? AppState.currentState === 'active',
      onLock: (listener) => controller?.onLock(listener) ?? (() => undefined),
    }),
    [controller],
  );
  return useMemo(() => ({ ...state, ...actions }), [state, actions]);
}
