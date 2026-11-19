import { Pressable, StyleSheet, View } from 'react-native';

import { AppText } from '@/design-system/AppText';
import { Icon } from '@/design-system/Icon';
import { colors, radii, spacing, statusColors } from '@/design-system/tokens';
import { useLocalization } from '@/i18n/useLocalization';

const SIZE = 64;

/** Space to leave under the last card of a screen that shows the floating actions, so they never cover it. */
export const FLOATING_ACTIONS_CLEARANCE = SIZE + spacing.lg;

export interface FloatingActionsProps {
  onEmergency: () => void;
  onAssistant: () => void;
}

/**
 * Two buttons that stay over Home: SOS (emergency numbers) at the bottom start and the assistant at the bottom end.
 * Start and end follow the language, so in Arabic SOS sits at the bottom right.
 */
export function FloatingActions({ onEmergency, onAssistant }: FloatingActionsProps) {
  const { t } = useLocalization();
  return (
    <View style={styles.bar} pointerEvents="box-none">
      <Pressable
        accessibilityRole="button"
        accessibilityLabel={t('home.actions.emergencyLabel')}
        accessibilityHint={t('home.actions.emergencyHint')}
        onPress={onEmergency}
        style={({ pressed }) => [styles.button, styles.emergency, pressed ? styles.emergencyPressed : null]}
        testID="home-sos"
      >
        <AppText variant="bodyStrong" tone="inverse" maxFontSizeMultiplier={1.3} numberOfLines={1}>
          {t('home.actions.emergency')}
        </AppText>
      </Pressable>
      <Pressable
        accessibilityRole="button"
        accessibilityLabel={t('home.actions.assistant')}
        accessibilityHint={t('home.actions.assistantHint')}
        onPress={onAssistant}
        style={({ pressed }) => [styles.button, styles.assistant, pressed ? styles.assistantPressed : null]}
        testID="home-assistant"
      >
        <Icon name="chat" size={26} color={colors.onPrimary} />
        {/* The pill has a fixed height: one line, or the label would split above and below its middle. */}
        <AppText variant="label" tone="inverse" maxFontSizeMultiplier={1.3} numberOfLines={1}>
          {t('home.actions.assistant')}
        </AppText>
      </Pressable>
    </View>
  );
}

const shadow = {
  shadowColor: colors.text,
  shadowOpacity: 0.18,
  shadowRadius: 8,
  shadowOffset: { width: 0, height: 4 },
  elevation: 6,
} as const;

const styles = StyleSheet.create({
  bar: {
    position: 'absolute',
    bottom: spacing.lg,
    start: spacing.xl,
    end: spacing.xl,
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'flex-end',
  },
  button: {
    minWidth: SIZE,
    height: SIZE,
    borderRadius: radii.pill,
    alignItems: 'center',
    justifyContent: 'center',
    ...shadow,
  },
  emergency: { width: SIZE, backgroundColor: statusColors.danger },
  assistant: {
    flexDirection: 'row',
    gap: spacing.sm,
    paddingHorizontal: spacing.lg,
    backgroundColor: colors.primary,
  },
  emergencyPressed: { backgroundColor: statusColors.dangerStrong },
  assistantPressed: { backgroundColor: colors.primaryStrong },
});
