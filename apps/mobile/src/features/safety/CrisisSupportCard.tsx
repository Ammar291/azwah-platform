import { useRouter } from 'expo-router';
import { useEffect, useState } from 'react';
import { AccessibilityInfo, StyleSheet, View } from 'react-native';

import { AppButton } from '@/design-system/AppButton';
import { AppCard } from '@/design-system/AppCard';
import { AppText } from '@/design-system/AppText';
import { Notice } from '@/design-system/Notice';
import { spacing } from '@/design-system/tokens';
import { openDialler } from '@/features/emergency/dialler';
import { POLICE } from '@/features/emergency/emergencyNumbers';
import { useLocalization } from '@/i18n/useLocalization';

export interface CrisisSupportCardProps {
  onDismiss: () => void;
  /** Runs first when the person opens the dialler or the numbers: the consultant's call ends there. */
  onLeave?: () => void;
}

/**
 * Shown when the server's safety screen matched what the person wrote or said (D-135 to D-138): a plain, calm card
 * with the emergency number and the emergency numbers screen. Its words come from the app's own translations, never
 * from the server, and it decides nothing: the server signalled it, the person can dismiss it.
 */
export function CrisisSupportCard({ onDismiss, onLeave }: CrisisSupportCardProps) {
  const { t } = useLocalization();
  const router = useRouter();
  const [dialFailed, setDialFailed] = useState(false);

  useEffect(() => {
    AccessibilityInfo.announceForAccessibility(t('safety.crisisCard.announcement'));
  }, [t]);

  const dial = async () => {
    onLeave?.();
    setDialFailed(!(await openDialler(POLICE.dial)));
  };

  return (
    <AppCard variant="highlight" testID="crisis-support-card">
      {/* The words are one element for screen readers; the buttons stay separately focusable. */}
      <View style={styles.words} accessible accessibilityRole="alert" accessibilityLiveRegion="assertive">
        <AppText variant="bodyStrong">{t('safety.crisisCard.title')}</AppText>
        <AppText>{t('safety.crisisCard.body')}</AppText>
        <AppText variant="caption" tone="secondary">
          {t('safety.crisisCard.teamNotified')}
        </AppText>
      </View>
      {dialFailed ? (
        <Notice
          tone="warning"
          message={t('emergency.dialFailed', { number: POLICE.display })}
          announce
          testID="crisis-dial-failed"
        />
      ) : null}
      <View style={styles.buttons}>
        <AppButton label={t('safety.crisisCard.dial999')} onPress={() => void dial()} />
        <AppButton
          label={t('safety.crisisCard.emergencyNumbers')}
          variant="secondary"
          onPress={() => {
            onLeave?.();
            router.push('/emergency');
          }}
        />
        <AppButton label={t('safety.crisisCard.dismiss')} variant="tertiary" onPress={onDismiss} />
      </View>
    </AppCard>
  );
}

const styles = StyleSheet.create({
  words: { gap: spacing.sm },
  buttons: { gap: spacing.sm },
});
