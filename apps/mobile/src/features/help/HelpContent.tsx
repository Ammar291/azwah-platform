import { StyleSheet, View } from 'react-native';

import { AppText } from '@/design-system/AppText';
import { borderWidths, colors, spacing } from '@/design-system/tokens';
import { useLocalization } from '@/i18n/useLocalization';

const TOPICS = ['about', 'answering', 'saving', 'language', 'privacy'] as const;

/**
 * Plain answers to common questions. No support contact is set up yet, and the screen shows no placeholder for one
 * (D-142); the connection to the server is not shown either.
 */
export function HelpContent() {
  const { t } = useLocalization();

  return (
    // Answers read as one page of text, divided by hairlines, rather than a stack of identical cards.
    <View>
      {TOPICS.map((topic, index) => (
        <View key={topic} style={[styles.topic, index > 0 ? styles.divided : null]}>
          <AppText variant="heading" accessibilityRole="header">
            {t(`help.topics.${topic}.title`)}
          </AppText>
          <AppText tone="secondary">{t(`help.topics.${topic}.body`)}</AppText>
        </View>
      ))}
    </View>
  );
}

const styles = StyleSheet.create({
  topic: { gap: spacing.xs, paddingVertical: spacing.lg },
  divided: { borderTopWidth: borderWidths.hairline, borderTopColor: colors.border },
});
