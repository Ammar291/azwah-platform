import { useRouter } from 'expo-router';
import { StyleSheet, View } from 'react-native';

import type { AssistantAction } from '@/api/participant/schemas';
import { AppButton } from '@/design-system/AppButton';
import { spacing } from '@/design-system/tokens';
import { useLocalization } from '@/i18n/useLocalization';

import { ACTION_ROUTES, orderedActions } from './suggestedScreens';

export interface SuggestedActionsProps {
  actions: readonly AssistantAction[];
  /** The screen these buttons are on, which is not offered again. */
  here?: AssistantAction;
  /** Runs before the app navigates (the consultant ends its call). */
  onOpen?: (action: AssistantAction) => void;
  /** `start`: buttons as wide as their labels, under a reply; `stretch`: full width, on the call screen. */
  layout?: 'start' | 'stretch';
}

/**
 * The app screens the assistant or the consultant suggested (D-122), as buttons with the app's own translated labels.
 * Nothing navigates by itself: the person chooses. SOS comes first and stands out.
 */
export function SuggestedActions({ actions, here, onOpen, layout = 'start' }: SuggestedActionsProps) {
  const { t } = useLocalization();
  const router = useRouter();
  const shown = orderedActions(actions, here);
  if (shown.length === 0) return null;
  return (
    <View
      style={[styles.actions, layout === 'start' ? styles.start : null]}
      accessibilityLabel={t('assistant.actionsLabel')}
      testID="suggested-actions"
    >
      {shown.map((action) => (
        <AppButton
          key={action}
          label={t(`assistant.actions.${action}`)}
          variant={action === 'sos' ? 'danger' : 'secondary'}
          onPress={() => {
            onOpen?.(action);
            router.push(ACTION_ROUTES[action]);
          }}
          testID={`suggested-${action}`}
        />
      ))}
    </View>
  );
}

const styles = StyleSheet.create({
  actions: { gap: spacing.sm },
  start: { alignItems: 'flex-start' },
});
