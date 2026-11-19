import { StyleSheet, View, type StyleProp, type ViewStyle } from 'react-native';

import { AppText } from './AppText';
import { spacing } from './tokens';

export interface DetailRowProps {
  label: string;
  value: string;
  /** `stacked`: label above value (facts). `inline`: label at the start, value at the end (figures). */
  layout?: 'stacked' | 'inline';
  /** Placement in the parent, for example a column of a facts grid. */
  style?: StyleProp<ViewStyle>;
}

/** One labelled value, read by screen readers as "label: value". */
export function DetailRow({ label, value, layout = 'stacked', style }: DetailRowProps) {
  const inline = layout === 'inline';
  return (
    <View style={[inline ? styles.inline : styles.stacked, style]} accessible accessibilityLabel={`${label}: ${value}`}>
      <AppText variant={inline ? 'body' : 'caption'} tone={inline ? 'secondary' : 'muted'} style={inline ? styles.grow : null}>
        {label}
      </AppText>
      <AppText variant={inline ? 'bodyStrong' : 'body'}>{value}</AppText>
    </View>
  );
}

const styles = StyleSheet.create({
  stacked: { gap: spacing.xxs },
  inline: { flexDirection: 'row', justifyContent: 'space-between', gap: spacing.md },
  grow: { flex: 1 },
});
