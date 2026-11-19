import type { ReactNode } from 'react';
import { Pressable, StyleSheet, View, type StyleProp, type ViewStyle } from 'react-native';

import { Icon } from './Icon';
import { borderWidths, colors, radii, spacing } from './tokens';

export type CardVariant = 'default' | 'muted' | 'highlight';

interface BaseCardProps {
  children: ReactNode;
  variant?: CardVariant;
  style?: StyleProp<ViewStyle>;
  testID?: string;
}

interface PressableCardProps extends BaseCardProps {
  /** Makes the whole card one button, with a chevron at the end. */
  onPress: () => void;
  /** What the card does, read as the button's name. */
  accessibilityLabel: string;
  accessibilityHint?: string;
}

export type AppCardProps = BaseCardProps | PressableCardProps;

const variants = StyleSheet.create({
  default: { backgroundColor: colors.surface, borderColor: colors.borderSoft },
  muted: { backgroundColor: colors.surfaceMuted, borderColor: colors.surfaceMuted },
  highlight: { backgroundColor: colors.primarySoft, borderColor: colors.primarySoft },
});

/** A bordered surface without shadows. Pass onPress to make it one large, labelled button. */
export function AppCard(props: AppCardProps) {
  const { children, variant = 'default', style, testID } = props;
  if ('onPress' in props) {
    return (
      <Pressable
        testID={testID}
        accessibilityRole="button"
        accessibilityLabel={props.accessibilityLabel}
        accessibilityHint={props.accessibilityHint}
        onPress={props.onPress}
        style={({ pressed }) => [styles.card, variants[variant], styles.row, pressed ? styles.pressed : null, style]}
      >
        <View style={styles.body}>{children}</View>
        <Icon name="chevron-forward" size={18} color={colors.primary} />
      </Pressable>
    );
  }
  return (
    <View testID={testID} style={[styles.card, variants[variant], style]}>
      {children}
    </View>
  );
}

const styles = StyleSheet.create({
  card: { borderWidth: borderWidths.hairline, borderRadius: radii.lg, padding: spacing.lg, gap: spacing.md },
  row: { flexDirection: 'row', alignItems: 'center' },
  body: { flex: 1, gap: spacing.sm },
  pressed: { backgroundColor: colors.surfaceMuted },
});
