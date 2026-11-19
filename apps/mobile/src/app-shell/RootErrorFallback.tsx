import { Pressable, StyleSheet, Text, View } from 'react-native';

import { colors, minTouchTarget, radii, spacing } from '@/design-system/tokens';

/**
 * Shown when the root layout itself fails, so translations may not exist. Both languages are inline, like
 * BootFailure. The error is never shown: it may hold internal detail.
 */
export function RootErrorFallback({ retry }: { retry: () => void }) {
  return (
    <View style={styles.container} accessibilityRole="alert">
      <Text style={styles.text}>Something went wrong. Try again.</Text>
      <Text style={styles.text}>حدث خطأ ما. حاول مرة أخرى.</Text>
      <Pressable
        accessibilityRole="button"
        accessibilityLabel="Try again / حاول مرة أخرى"
        onPress={retry}
        style={styles.button}
      >
        <Text style={styles.buttonText}>Try again / حاول مرة أخرى</Text>
      </Pressable>
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
    alignItems: 'center',
    justifyContent: 'center',
    gap: spacing.lg,
    padding: spacing.xl,
    backgroundColor: colors.background,
  },
  text: { fontSize: 16, color: colors.text, textAlign: 'center' },
  button: {
    minHeight: minTouchTarget,
    paddingHorizontal: spacing.xl,
    justifyContent: 'center',
    borderRadius: radii.md,
    backgroundColor: colors.primary,
  },
  buttonText: { fontSize: 16, color: colors.onPrimary },
});
