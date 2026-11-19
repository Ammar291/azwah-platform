import { useState, type Ref } from 'react';
import { StyleSheet, TextInput, View, type TextInputProps } from 'react-native';

import { AppText } from './AppText';
import { Icon } from './Icon';
import { borderWidths, colors, maxFontScale, minTouchTarget, radii, spacing, statusColors, typeScale } from './tokens';

export interface AppInputProps extends Omit<TextInputProps, 'style' | 'editable'> {
  label: string;
  /** Guidance shown under the label and read after it. */
  hint?: string;
  /** A validation message; shown with a glyph and announced. */
  error?: string;
  disabled?: boolean;
  ref?: Ref<TextInput>;
}

/** A labelled text input with hint, error, focus and disabled states. */
export function AppInput({ label, hint, error, disabled = false, onFocus, onBlur, ref, ...rest }: AppInputProps) {
  const [focused, setFocused] = useState(false);
  return (
    <View style={styles.field}>
      <AppText variant="label">{label}</AppText>
      {hint ? (
        <AppText variant="caption" tone="muted">
          {hint}
        </AppText>
      ) : null}
      <TextInput
        {...rest}
        ref={ref}
        accessibilityLabel={label}
        accessibilityHint={hint}
        accessibilityState={{ disabled }}
        editable={!disabled}
        maxFontSizeMultiplier={maxFontScale}
        placeholderTextColor={colors.textMuted}
        onFocus={(event) => {
          setFocused(true);
          onFocus?.(event);
        }}
        onBlur={(event) => {
          setFocused(false);
          onBlur?.(event);
        }}
        style={[
          styles.input,
          focused ? styles.focused : null,
          error ? styles.invalid : null,
          disabled ? styles.disabled : null,
        ]}
      />
      {error ? (
        <View style={styles.error}>
          <Icon name="alert" size={16} color={statusColors.danger} />
          <AppText variant="caption" tone="danger" accessibilityRole="alert" style={styles.errorText}>
            {error}
          </AppText>
        </View>
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  field: { gap: spacing.xs },
  input: {
    ...typeScale.body,
    minHeight: minTouchTarget,
    paddingHorizontal: spacing.md,
    paddingVertical: spacing.sm,
    borderWidth: borderWidths.hairline,
    borderColor: colors.borderStrong,
    borderRadius: radii.md,
    backgroundColor: colors.surface,
    color: colors.text,
  },
  focused: { borderColor: colors.primary, borderWidth: borderWidths.strong },
  invalid: { borderColor: statusColors.danger, borderWidth: borderWidths.strong },
  disabled: { backgroundColor: colors.surfaceMuted, color: colors.textMuted },
  error: { flexDirection: 'row', alignItems: 'center', gap: spacing.xs },
  errorText: { flexShrink: 1 },
});
