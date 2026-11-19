import { ActivityIndicator, Pressable, StyleSheet, useWindowDimensions, View, type PressableProps } from 'react-native';

import { AppText, type TextTone } from './AppText';
import { useInButtonRow } from './ButtonRowContext';
import { Icon, type IconName } from './Icon';
import { colors, minTouchTarget, motion, radii, spacing, statusColors } from './tokens';

export type ButtonVariant = 'primary' | 'secondary' | 'tertiary' | 'danger';

export interface AppButtonProps extends Omit<PressableProps, 'children' | 'style'> {
  label: string;
  /**
   * A fuller name for screen readers when several buttons share a label ("Edit" on each card). It must start with the
   * visible label so voice control still finds it. Defaults to the label.
   */
  accessibilityLabel?: string;
  variant?: ButtonVariant;
  /** Shows a spinner, announces busy and ignores presses. */
  loading?: boolean;
  /** A glyph after the label, for example 'chevron-forward' on "Next". */
  trailingIcon?: IconName;
}

/**
 * Up to this system text size a label stays on one line (shrinking to at most MIN_LABEL_SCALE of its size before it
 * would break), so it can never split into a line above and a line below. Larger text wraps instead of being cut off.
 */
export const SINGLE_LINE_MAX_FONT_SCALE = 1.3;
const MIN_LABEL_SCALE = 0.85;

const variantStyles = {
  primary: {
    container: { backgroundColor: colors.primary, borderColor: colors.primary },
    pressed: { backgroundColor: colors.primaryStrong, borderColor: colors.primaryStrong },
    tone: 'inverse',
  },
  secondary: {
    container: { backgroundColor: colors.surface, borderColor: colors.borderStrong },
    pressed: { backgroundColor: colors.primarySoft, borderColor: colors.primary },
    tone: 'primary',
  },
  tertiary: {
    container: { backgroundColor: 'transparent', borderColor: 'transparent' },
    pressed: { backgroundColor: colors.primarySoft, borderColor: colors.primarySoft },
    tone: 'primary',
  },
  danger: {
    container: { backgroundColor: statusColors.danger, borderColor: statusColors.danger },
    pressed: { backgroundColor: statusColors.dangerStrong, borderColor: statusColors.dangerStrong },
    tone: 'inverse',
  },
} as const satisfies Record<ButtonVariant, { container: object; pressed: object; tone: TextTone }>;

/**
 * The app's button. It fills the width it is given (a column stretches it; ButtonRow shares a row out equally and
 * gives it compact padding) and centres its spinner, label and glyph as one line.
 */
export function AppButton({
  label,
  variant = 'primary',
  loading = false,
  trailingIcon,
  disabled,
  accessibilityLabel,
  accessibilityState,
  ...rest
}: AppButtonProps) {
  const inactive = disabled === true || loading;
  const compact = useInButtonRow();
  const singleLine = useWindowDimensions().fontScale <= SINGLE_LINE_MAX_FONT_SCALE;
  const { container, pressed: pressedContainer, tone } = variantStyles[variant];
  const foreground = tone === 'inverse' ? colors.onPrimary : colors.primary;
  return (
    <Pressable
      {...rest}
      accessibilityRole="button"
      accessibilityLabel={accessibilityLabel ?? label}
      accessibilityState={{ ...accessibilityState, disabled: inactive, busy: loading }}
      disabled={inactive}
      style={({ pressed }) => [
        styles.base,
        compact ? styles.compact : null,
        container,
        pressed && !inactive ? pressedContainer : null,
        disabled === true ? styles.disabled : null,
      ]}
    >
      <View style={styles.content}>
        {loading ? <ActivityIndicator color={foreground} /> : null}
        <AppText
          variant="bodyStrong"
          tone={tone}
          style={styles.label}
          numberOfLines={singleLine ? 1 : undefined}
          adjustsFontSizeToFit={singleLine}
          minimumFontScale={singleLine ? MIN_LABEL_SCALE : undefined}
        >
          {label}
        </AppText>
        {trailingIcon && !loading ? <Icon name={trailingIcon} size={18} color={foreground} /> : null}
      </View>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  base: {
    minHeight: minTouchTarget,
    minWidth: minTouchTarget,
    paddingHorizontal: spacing.xl,
    paddingVertical: spacing.md,
    borderRadius: radii.md,
    borderWidth: 1,
    justifyContent: 'center',
  },
  // In a row every point counts: at 360 points three buttons have about 98 points each.
  compact: { paddingHorizontal: spacing.md },
  content: { flexDirection: 'row', alignItems: 'center', justifyContent: 'center', gap: spacing.sm },
  label: { textAlign: 'center', flexShrink: 1 },
  disabled: { opacity: motion.disabledOpacity },
});
