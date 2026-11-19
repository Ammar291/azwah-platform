import { ActivityIndicator, Pressable, StyleSheet, useWindowDimensions, View } from 'react-native';
import Svg, { Path } from 'react-native-svg';

import { SINGLE_LINE_MAX_FONT_SCALE } from '@/design-system/AppButton';
import { AppText } from '@/design-system/AppText';
import { minTouchTarget, motion, radii, spacing } from '@/design-system/tokens';

/** Google's sign-in button colours (its branding guidelines, light theme); the app's own colours stay off it. */
const GOOGLE_SURFACE = '#ffffff';
const GOOGLE_BORDER = '#747775';
const GOOGLE_TEXT = '#1f1f1f';

export interface GoogleButtonProps {
  label: string;
  onPress: () => void;
  loading?: boolean;
  disabled?: boolean;
}

/** Google's "G" mark, as its branding guidelines require it on a sign-in button. It never mirrors. */
function GoogleMark({ size }: { size: number }) {
  return (
    <Svg width={size} height={size} viewBox="0 0 48 48" accessible={false}>
      <Path
        fill="#EA4335"
        d="M24 9.5c3.54 0 6.71 1.22 9.21 3.6l6.85-6.85C35.9 2.38 30.47 0 24 0 14.62 0 6.51 5.38 2.56 13.22l7.98 6.19C12.43 13.72 17.74 9.5 24 9.5z"
      />
      <Path
        fill="#4285F4"
        d="M46.98 24.55c0-1.57-.15-3.09-.38-4.55H24v9.02h12.94c-.58 2.96-2.26 5.48-4.78 7.18l7.73 6c4.51-4.18 7.09-10.36 7.09-17.65z"
      />
      <Path
        fill="#FBBC05"
        d="M10.53 28.59c-.48-1.45-.76-2.99-.76-4.59s.27-3.14.76-4.59l-7.98-6.19C.92 16.46 0 20.12 0 24c0 3.88.92 7.54 2.56 10.78l7.97-6.19z"
      />
      <Path
        fill="#34A853"
        d="M24 48c6.48 0 11.93-2.13 15.89-5.81l-7.73-6c-2.15 1.45-4.92 2.3-8.16 2.3-6.26 0-11.57-4.22-13.47-9.91l-7.98 6.19C6.51 42.62 14.62 48 24 48z"
      />
    </Svg>
  );
}

/**
 * "Continue with Google" (D-147). Shown only when the identity provider lists the Google sign-in, so it never claims an
 * integration that is not configured. The label always names Google.
 */
export function GoogleButton({ label, onPress, loading = false, disabled = false }: GoogleButtonProps) {
  const inactive = disabled || loading;
  // As AppButton: one line at ordinary text sizes, so the label never splits; it wraps at large ones.
  const singleLine = useWindowDimensions().fontScale <= SINGLE_LINE_MAX_FONT_SCALE;
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel={label}
      accessibilityState={{ disabled: inactive, busy: loading }}
      disabled={inactive}
      onPress={onPress}
      style={({ pressed }) => [
        styles.base,
        pressed && !inactive ? styles.pressed : null,
        disabled ? styles.disabled : null,
      ]}
    >
      <View style={styles.content}>
        {loading ? <ActivityIndicator color={GOOGLE_TEXT} /> : <GoogleMark size={20} />}
        <AppText
          variant="bodyStrong"
          style={[styles.label, { color: GOOGLE_TEXT }]}
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
    borderWidth: 1,
    borderColor: GOOGLE_BORDER,
    backgroundColor: GOOGLE_SURFACE,
    justifyContent: 'center',
  },
  content: { flexDirection: 'row', alignItems: 'center', justifyContent: 'center', gap: spacing.md },
  label: { textAlign: 'center', flexShrink: 1 },
  pressed: { opacity: motion.pressedOpacity },
  disabled: { opacity: motion.disabledOpacity },
});
