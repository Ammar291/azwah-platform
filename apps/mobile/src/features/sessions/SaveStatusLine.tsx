import { StyleSheet, View } from 'react-native';

import { AppButton } from '@/design-system/AppButton';
import { AppText, type TextTone } from '@/design-system/AppText';
import { Icon, type IconName } from '@/design-system/Icon';
import { colors, spacing, statusColors } from '@/design-system/tokens';
import type { SaveStatus } from '@/drafts/saveStatus';
import { useLocalization } from '@/i18n/useLocalization';

const LOOK: Record<Exclude<SaveStatus, 'none'>, { icon: IconName; tone: TextTone; color: string }> = {
  savedOnDevice: { icon: 'dot', tone: 'secondary', color: colors.textSecondary },
  keptInMemory: { icon: 'dot', tone: 'secondary', color: colors.textSecondary },
  saving: { icon: 'dot', tone: 'secondary', color: colors.textSecondary },
  saved: { icon: 'check', tone: 'success', color: statusColors.success },
  offlineOnDevice: { icon: 'alert', tone: 'warning', color: statusColors.warning },
  offlineInMemory: { icon: 'alert', tone: 'warning', color: statusColors.warning },
  retrying: { icon: 'alert', tone: 'warning', color: statusColors.warning },
  notSaved: { icon: 'alert', tone: 'danger', color: statusColors.danger },
  rejected: { icon: 'alert', tone: 'danger', color: statusColors.danger },
  conflict: { icon: 'alert', tone: 'warning', color: statusColors.warning },
};

export interface SaveStatusLineProps {
  status: SaveStatus;
  /** When the server last accepted answers from this phone. */
  lastSyncedAt: string | null;
  onRetry: () => void;
}

/**
 * Where the answers are right now, announced politely as it changes, with the time of the last save to the server.
 * Never claims "saved" before the server has the answers.
 */
export function SaveStatusLine({ status, lastSyncedAt, onRetry }: SaveStatusLineProps) {
  const { t, formatDate } = useLocalization();
  if (status === 'none') {
    return null;
  }
  const look = LOOK[status];
  return (
    <View style={styles.container}>
      <View style={styles.row} accessible accessibilityLiveRegion="polite" accessibilityRole="text">
        <Icon name={look.icon} size={16} color={look.color} />
        <AppText variant="caption" tone={look.tone} style={styles.text}>
          {t(`session.saveStatus.${status}`)}
        </AppText>
      </View>
      {lastSyncedAt ? (
        <AppText variant="caption" tone="muted">
          {t('session.lastSynced', { time: formatDate(lastSyncedAt, 'dateTime') })}
        </AppText>
      ) : null}
      {status === 'notSaved' || status === 'retrying' ? (
        <AppButton label={t('common.retry')} variant="tertiary" onPress={onRetry} />
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  container: { gap: spacing.xs },
  row: { flexDirection: 'row', alignItems: 'center', gap: spacing.xs },
  text: { flexShrink: 1 },
});
