import { StyleSheet, View } from 'react-native';

import { AppText } from './AppText';
import { colors, radii, spacing } from './tokens';

export interface ProgressIndicatorProps {
  current: number;
  total: number;
  /** Already formatted for the language, for example "Question 3 of 10". Shown and read aloud. */
  label: string;
}

/** Position in a sequence as text and a bar that fills from the start edge. */
export function ProgressIndicator({ current, total, label }: ProgressIndicatorProps) {
  const safeTotal = Math.max(total, 1);
  const now = Math.min(Math.max(current, 0), safeTotal);
  return (
    <View
      accessible
      accessibilityRole="progressbar"
      accessibilityLabel={label}
      accessibilityValue={{ min: 0, max: safeTotal, now, text: label }}
      style={styles.container}
    >
      <AppText variant="label" tone="secondary">
        {label}
      </AppText>
      <View style={styles.track}>
        <View testID="progress-fill" style={[styles.fill, { width: `${(now / safeTotal) * 100}%` }]} />
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  container: { gap: spacing.xs },
  track: { height: 8, borderRadius: radii.pill, backgroundColor: colors.primarySoft, overflow: 'hidden' },
  fill: { height: '100%', borderRadius: radii.pill, backgroundColor: colors.primary, alignSelf: 'flex-start' },
});
