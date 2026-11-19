import { Stack } from 'expo-router';

import { RouteErrorBoundary } from '@/components/RouteErrorBoundary';
import { colors } from '@/design-system/tokens';

/** Screens for people who are not signed in. */
export default function PublicLayout() {
  return <Stack screenOptions={{ headerShown: false, contentStyle: { backgroundColor: colors.background } }} />;
}

export { RouteErrorBoundary as ErrorBoundary };
