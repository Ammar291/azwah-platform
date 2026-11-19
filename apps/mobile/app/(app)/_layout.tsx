import { Stack } from 'expo-router';

import { RouteErrorBoundary } from '@/components/RouteErrorBoundary';
import { colors } from '@/design-system/tokens';

export const unstable_settings = {
  // A link straight to a household, questionnaire or result still has the main places beneath it.
  initialRouteName: '(drawer)',
};

/**
 * Screens for signed-in people. The main places sit in the side menu's group, `(drawer)` (D-106); everything opened
 * from them (household, questionnaires, results, help) stacks on top. The emergency numbers rise from the bottom as a
 * modal, over whatever was open.
 */
export default function SignedInLayout() {
  return (
    <Stack screenOptions={{ headerShown: false, contentStyle: { backgroundColor: colors.background } }}>
      <Stack.Screen name="(drawer)" />
      <Stack.Screen name="emergency" options={{ presentation: 'modal' }} />
    </Stack>
  );
}

export { RouteErrorBoundary as ErrorBoundary };
