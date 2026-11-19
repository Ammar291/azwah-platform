import { createContext, useContext, type ReactNode } from 'react';

import type { RegistrationStatus } from './registration';

export interface OnboardingState {
  /** The signed-in person is in onboarding: their details still need confirming, or they are on "account ready". */
  active: boolean;
  /** Their details were just confirmed and the "account ready" screen is showing. */
  finishing: boolean;
  /** Null while signed out. */
  registration: RegistrationStatus | null;
  /** Keeps onboarding open after registering, so "account ready" can show before Home. */
  beginFinishing(): void;
  /** Leaves onboarding for Home. */
  finish(): void;
}

const OnboardingContext = createContext<OnboardingState | null>(null);

export function OnboardingProvider({ value, children }: { value: OnboardingState; children: ReactNode }) {
  return <OnboardingContext.Provider value={value}>{children}</OnboardingContext.Provider>;
}

/** Screens outside the app shell (tests rendering one screen) see an onboarding that is not running. */
const IDLE: OnboardingState = {
  active: false,
  finishing: false,
  registration: null,
  beginFinishing: () => undefined,
  finish: () => undefined,
};

export function useOnboarding(): OnboardingState {
  return useContext(OnboardingContext) ?? IDLE;
}
