import { StyleSheet, useWindowDimensions, View } from 'react-native';

import { AppText, type TextTone } from './AppText';
import { Icon, type IconName } from './Icon';
import { borderWidths, colors, radii, spacing, statusColors, type StatusTone } from './tokens';

export type BadgeTone = StatusTone | 'neutral';

const tones: Record<BadgeTone, { background: string; foreground: string; text: TextTone; icon: IconName }> = {
  neutral: { background: colors.surfaceMuted, foreground: colors.textSecondary, text: 'secondary', icon: 'dot' },
  info: { background: statusColors.infoSoft, foreground: statusColors.info, text: 'primary', icon: 'info' },
  success: { background: statusColors.successSoft, foreground: statusColors.success, text: 'success', icon: 'check' },
  warning: { background: statusColors.warningSoft, foreground: statusColors.warning, text: 'warning', icon: 'alert' },
  danger: { background: statusColors.dangerSoft, foreground: statusColors.danger, text: 'danger', icon: 'alert' },
};

export interface BadgeProps {
  label: string;
  tone?: BadgeTone;
}

/** Up to this system text size every badge fits on one line of a phone screen. */
const SINGLE_LINE_MAX_SCALE = 1.3;

/** A short status: always a glyph and words, never colour alone. */
export function Badge({ label, tone = 'neutral' }: BadgeProps) {
  const style = tones[tone];
  const { fontScale } = useWindowDimensions();
  return (
    <View style={[styles.badge, { backgroundColor: style.background }]}>
      <View pointerEvents="none" style={[StyleSheet.absoluteFill, styles.edge, { borderColor: `${style.foreground}33` }]} />
      <Icon name={style.icon} size={14} color={style.foreground} />
      {/* Android measured "تسجيل دخول بالمحاكاة" a hair narrower than it draws it, so the badge, which shrinks to its
          words, broke the line and clipped the last word. One line cannot break. At large text sizes a long badge
          may need two lines, so it wraps there instead of being cut off. */}
      <AppText variant="label" tone={style.text} numberOfLines={fontScale <= SINGLE_LINE_MAX_SCALE ? 1 : undefined}>
        {label}
      </AppText>
    </View>
  );
}

const styles = StyleSheet.create({
  badge: {
    flexDirection: 'row',
    alignItems: 'center',
    alignSelf: 'flex-start',
    gap: spacing.xs,
    paddingHorizontal: spacing.sm,
    paddingVertical: spacing.xxs,
    borderRadius: radii.pill,
  },
  // A faint edge in the badge's own tone keeps it visible on tinted panels as well as on white. It is drawn over the
  // badge rather than as its border: a border narrows the text's box after Android has measured it, and the last
  // word then wraps onto a line that is clipped ("تسجيل دخول بالمحاكاة" lost its last word).
  edge: { borderRadius: radii.pill, borderWidth: borderWidths.hairline },
});
