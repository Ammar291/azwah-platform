import { Stack } from 'expo-router';

import { RouteErrorBoundary } from '@/components/RouteErrorBoundary';
import { colors } from '@/design-system/tokens';

/**
 * First sign-in only (D-97): confirm the UAE PASS details, then "account ready". The group's guard (AppShell) is open
 * only while the server says the person is not registered yet, or while "account ready" shows. No back gesture: the
 * way out is confirming, or signing out from "These aren't my details".
 */
export default function OnboardingLayout() {
  return (
    <Stack
      screenOptions={{ headerShown: false, gestureEnabled: false, contentStyle: { backgroundColor: colors.background } }}
    />
  );
}

export { RouteErrorBoundary as ErrorBoundary };
