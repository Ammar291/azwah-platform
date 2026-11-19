import { useEffect, type EffectCallback } from 'react';

/**
 * A stand-in for expo-router in screen tests: `jest.mock('expo-router', () => jest.requireActual('@/test-utils/expoRouterMock'))`.
 * Navigation calls are recorded; route parameters are set with setSearchParams. `router.back()` first fires
 * `beforeRemove` on the screen's navigation, as the real stack does, so a screen can hold it (unsaved changes).
 * A rendered screen counts as focused until it unmounts (useFocusEffect).
 */
type BeforeRemoveEvent = { preventDefault: () => void; data: { action: { type: string } } };
type Listener = (event: BeforeRemoveEvent) => void;

const listeners = new Set<Listener>();

/** Fires `beforeRemove` with `action`; true when a listener held it. */
export function emitBeforeRemove(action: { type: string } = { type: 'GO_BACK' }): boolean {
  let prevented = false;
  const event: BeforeRemoveEvent = { preventDefault: () => (prevented = true), data: { action } };
  [...listeners].forEach((listener) => listener(event));
  return prevented;
}

export const navigation = {
  addListener: jest.fn((_name: 'beforeRemove', listener: Listener) => {
    listeners.add(listener);
    return () => listeners.delete(listener);
  }),
  dispatch: jest.fn(),
  setOptions: jest.fn(),
};

export const router = {
  push: jest.fn(),
  replace: jest.fn(),
  navigate: jest.fn(),
  dismissTo: jest.fn(),
  dismissAll: jest.fn(),
  back: jest.fn(() => {
    emitBeforeRemove();
  }),
  canGoBack: jest.fn(() => true),
};

let params: Record<string, string> = {};
export const redirects: unknown[] = [];

export function setSearchParams(next: Record<string, string>) {
  params = next;
}

export function resetRouterMock() {
  Object.values(router).forEach((fn) => fn.mockClear());
  Object.values(navigation).forEach((fn) => fn.mockClear());
  router.canGoBack.mockImplementation(() => true);
  listeners.clear();
  params = {};
  redirects.length = 0;
}

export const useRouter = () => router;
export const useNavigation = () => navigation;
export const useLocalSearchParams = () => params;
export const useFocusEffect = (effect: EffectCallback) => useEffect(effect, [effect]);

export function Redirect({ href }: { href: unknown }) {
  redirects.push(href);
  return null;
}
