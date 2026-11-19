import type { Ref } from 'react';
import { Text, type TextProps } from 'react-native';

import { colors, maxFontScale, statusColors, typeScale } from './tokens';

export type TextVariant = keyof typeof typeScale;
export type TextTone =
  | 'default'
  | 'secondary'
  | 'muted'
  | 'primary'
  | 'success'
  | 'warning'
  | 'danger'
  | 'inverse';

export const toneColors: Record<TextTone, string> = {
  default: colors.text,
  secondary: colors.textSecondary,
  muted: colors.textMuted,
  primary: colors.primary,
  success: statusColors.success,
  warning: statusColors.warning,
  danger: statusColors.danger,
  inverse: colors.onPrimary,
};

export interface AppTextProps extends TextProps {
  variant?: TextVariant;
  tone?: TextTone;
  /** For moving screen-reader focus to a heading. */
  ref?: Ref<Text>;
}

/**
 * Text aligns with the layout direction by default, so it reads correctly in Arabic and English. It follows the
 * system text size up to maxFontScale.
 */
export function AppText({ variant = 'body', tone = 'default', style, maxFontSizeMultiplier, ...rest }: AppTextProps) {
  return (
    <Text
      {...rest}
      maxFontSizeMultiplier={maxFontSizeMultiplier ?? maxFontScale}
      style={[typeScale[variant], { color: toneColors[tone] }, style]}
    />
  );
}
