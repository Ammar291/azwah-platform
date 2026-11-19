import { StyleSheet, View } from 'react-native';

import { AppButton } from './AppButton';
import { AppText, type TextTone } from './AppText';
import { Icon, type IconName } from './Icon';
import { borderWidths, radii, spacing, statusColors, type StatusTone } from './tokens';

// The border is the tone's colour at about a quarter strength: it holds the edge on the paper background without
// becoming a stripe. Meaning still comes from the glyph and the words.
interface ToneStyle {
  background: string;
  accent: string;
  border: string;
  text: TextTone;
  icon: IconName;
}

const tones: Record<StatusTone, ToneStyle> = {
  info: {
    background: statusColors.infoSoft,
    accent: statusColors.info,
    border: `${statusColors.info}33`,
    text: 'primary',
    icon: 'info',
  },
  success: {
    background: statusColors.successSoft,
    accent: statusColors.success,
    border: `${statusColors.success}33`,
    text: 'success',
    icon: 'check',
  },
  warning: {
    background: statusColors.warningSoft,
    accent: statusColors.warning,
    border: `${statusColors.warning}33`,
    text: 'warning',
    icon: 'alert',
  },
  danger: {
    background: statusColors.dangerSoft,
    accent: statusColors.danger,
    border: `${statusColors.danger}33`,
    text: 'danger',
    icon: 'alert',
  },
};

export interface NoticeProps {
  tone?: StatusTone;
  title?: string;
  message: string;
  action?: { label: string; onPress: () => void };
  /** Announce the notice when it appears (errors and expiry), instead of leaving it to be found. */
  announce?: boolean;
  testID?: string;
}

/** An inline banner: development mode, placeholders awaiting approval, an expired session, a failed save. */
export function Notice({ tone = 'info', title, message, action, announce = false, testID }: NoticeProps) {
  const style = tones[tone];
  return (
    <View testID={testID} style={[styles.notice, { backgroundColor: style.background, borderColor: style.border }]}>
      <View style={styles.row}>
        <View style={styles.glyph}>
          <Icon name={style.icon} size={20} color={style.accent} />
        </View>
        {/* The words are one element for screen readers; the action stays separately focusable. */}
        <View
          style={styles.text}
          accessible
          accessibilityRole={announce ? 'alert' : 'text'}
          accessibilityLiveRegion={announce ? 'polite' : 'none'}
        >
          {title ? (
            <AppText variant="bodyStrong" tone={style.text}>
              {title}
            </AppText>
          ) : null}
          <AppText variant="body" tone="default">
            {message}
          </AppText>
        </View>
      </View>
      {action ? <AppButton label={action.label} variant="secondary" onPress={action.onPress} /> : null}
    </View>
  );
}

const styles = StyleSheet.create({
  notice: {
    borderRadius: radii.lg,
    borderWidth: borderWidths.hairline,
    paddingVertical: spacing.md,
    paddingHorizontal: spacing.lg,
    gap: spacing.md,
  },
  row: { flexDirection: 'row', alignItems: 'flex-start', gap: spacing.md },
  // Centres the 20 pt glyph on the first line of 26 pt body text.
  glyph: { paddingTop: 3 },
  text: { flex: 1, gap: spacing.xxs },
});
