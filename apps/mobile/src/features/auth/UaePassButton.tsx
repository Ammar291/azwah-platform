import { ActivityIndicator, Pressable, StyleSheet, useWindowDimensions, View } from 'react-native';

import { SINGLE_LINE_MAX_FONT_SCALE } from '@/design-system/AppButton';
import { AppText } from '@/design-system/AppText';
import { Icon } from '@/design-system/Icon';
import { minTouchTarget, motion, radii, spacing } from '@/design-system/tokens';

/** UAE PASS presents itself in black and white; the app's own colours stay off its button. */
const UAE_PASS_BLACK = '#000000';
const UAE_PASS_WHITE = '#ffffff';

export interface UaePassButtonProps {
  label: string;
  onPress: () => void;
  /** `primary`: solid black ("Sign in with UAE PASS"); `outline`: the secondary action ("Create an account …"). */
  variant?: 'primary' | 'outline';
  loading?: boolean;
  disabled?: boolean;
}

/**
 * The sign-in button for UAE PASS. Drawn in UAE PASS's black and white with a fingerprint glyph; the official button
 * artwork from the UAE PASS brand kit replaces this drawing once the programme's onboarding provides it (open
 * question 5). The label always names UAE PASS, so the button never claims anything else.
 */
export function UaePassButton({ label, onPress, variant = 'primary', loading = false, disabled = false }: UaePassButtonProps) {
  const inactive = disabled || loading;
  // As AppButton: one line at ordinary text sizes, so the label never splits; it wraps at large ones.
  const singleLine = useWindowDimensions().fontScale <= SINGLE_LINE_MAX_FONT_SCALE;
  const solid = variant === 'primary';
  const foreground = solid ? UAE_PASS_WHITE : UAE_PASS_BLACK;
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel={label}
      accessibilityState={{ disabled: inactive, busy: loading }}
      disabled={inactive}
      onPress={onPress}
      style={({ pressed }) => [
        styles.base,
        solid ? styles.solid : styles.outline,
        pressed && !inactive ? styles.pressed : null,
        disabled ? styles.disabled : null,
      ]}
    >
      <View style={styles.content}>
        {loading ? <ActivityIndicator color={foreground} /> : <Icon name="fingerprint" size={22} color={foreground} />}
        <AppText
          variant="bodyStrong"
          style={[styles.label, { color: foreground }]}
          numberOfLines={singleLine ? 1 : undefined}
          adjustsFontSizeToFit={singleLine}
          minimumFontScale={singleLine ? 0.85 : undefined}
        >
          {label}
        </AppText>
      </View>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  base: {
    minHeight: Math.max(minTouchTarget, 52),
    paddingHorizontal: spacing.xl,
    paddingVertical: spacing.md,
    borderRadius: radii.md,
    borderWidth: 1.5,
    justifyContent: 'center',
  },
  solid: { backgroundColor: UAE_PASS_BLACK, borderColor: UAE_PASS_BLACK },
  outline: { backgroundColor: UAE_PASS_WHITE, borderColor: UAE_PASS_BLACK },
  content: { flexDirection: 'row', alignItems: 'center', justifyContent: 'center', gap: spacing.md },
  label: { textAlign: 'center', flexShrink: 1 },
  pressed: { opacity: motion.pressedOpacity },
  disabled: { opacity: motion.disabledOpacity },
});
