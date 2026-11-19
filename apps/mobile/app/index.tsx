import { Redirect } from 'expo-router';

import { useAuth } from '@/auth/AuthContext';
import { entryHref } from '@/auth/entryHref';
import { CheckingRegistration } from '@/features/onboarding/CheckingRegistration';
import { useOnboarding } from '@/features/onboarding/OnboardingContext';
import { useAppLock } from '@/security/AppLockContext';

/**
 * The anchor route: every guard change lands here and is sent on by auth and onboarding state. While the app is locked
 * it shows nothing (the lock screen covers it); after unlocking it returns to the screen the person was on, if any.
 * While the server is asked whether a signed-in person is registered, it shows a loading state.
 */
export default function Index() {
  const { state } = useAuth();
  const onboarding = useOnboarding();
  const { locked, resumeHref } = useAppLock();
  if (locked) {
    return null;
  }
  const href =
    state.status === 'signed_in' && resumeHref && !onboarding.active ? resumeHref : entryHref(state, onboarding);
  if (href) {
    return <Redirect href={href} />;
  }
  return state.status === 'signed_in' ? <CheckingRegistration /> : null;
}
