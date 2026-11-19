import type { AuthState } from './types';

export type EntryHref = '/home' | '/welcome' | '/auth/sign-in' | '/confirm-details' | '/account-ready';

/** What the anchor route knows about onboarding (features/onboarding); absent, a signed-in person goes home. */
export interface EntryOnboarding {
  active: boolean;
  finishing: boolean;
  /** `checking`: the server has not said yet whether the person is registered. */
  registration: 'checking' | 'required' | 'registered' | 'unknown' | null;
}

/**
 * Where the anchor route (app/index.tsx) sends someone. The route guards in AppShell return here whenever a guard
 * closes, so an expired session lands on sign-in with an explanation, a first sign-in lands on confirming details,
 * and null means "wait" (start-up, or the registration check on its way).
 */
export function entryHref(state: AuthState, onboarding?: EntryOnboarding): EntryHref | null {
  switch (state.status) {
    case 'initializing':
      return null;
    case 'signed_in':
      if (onboarding?.active) {
        return onboarding.finishing ? '/account-ready' : '/confirm-details';
      }
      return onboarding?.registration === 'checking' ? null : '/home';
    case 'signed_out':
      return state.reason === 'session_expired' ? '/auth/sign-in' : '/welcome';
    case 'unavailable':
      return '/welcome';
  }
}

/** The opening splash may give way: the sign-in state is known (the registration check is not waited for). */
export function isStartupReady(state: AuthState): boolean {
  return state.status !== 'initializing';
}

export function isSignedIn(state: AuthState): boolean {
  return state.status === 'signed_in';
}
