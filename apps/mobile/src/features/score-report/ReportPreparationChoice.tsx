import { StyleSheet, View } from 'react-native';

import { AppButton } from '@/design-system/AppButton';
import { AppCard } from '@/design-system/AppCard';
import { AppText } from '@/design-system/AppText';
import { ButtonRow } from '@/design-system/ButtonRow';
import { spacing } from '@/design-system/tokens';
import type { SessionContent } from '@/features/sessions/useSessionContent';
import { useLocalization } from '@/i18n/useLocalization';

import { useReportPreparation } from './useReportPreparation';

/**
 * Asks once, on the questionnaire itself and before anything is sent, whether to write the report as the person answers
 * (D-113). Where the answers go is no longer said here (D-142: this build is for a recorded demonstration on synthetic
 * data; the disclosure must come back, in the consent and privacy text, before any real family uses the app). Saying
 * nothing is no: answering carries on and nothing is sent. Afterwards a line says it is happening, with a way to stop.
 */
export function ReportPreparationChoice({ content }: { content: SessionContent }) {
  const { t } = useLocalization();
  const { eligible, choice, choose } = useReportPreparation(content);

  if (!eligible || choice === 'off') return null;
  if (choice === 'on') {
    return (
      <View style={styles.on} testID="report-preparing">
        <AppText variant="caption" tone="muted">
          {t('scoreReport.ahead.on')}
        </AppText>
        <AppButton label={t('scoreReport.ahead.stop')} variant="tertiary" onPress={() => choose('off')} />
      </View>
    );
  }
  return (
    <AppCard testID="report-choice">
      <AppText variant="bodyStrong" accessibilityRole="header">
        {t('scoreReport.ahead.title')}
      </AppText>
      <ButtonRow>
        <AppButton label={t('scoreReport.ahead.yes')} onPress={() => choose('on')} />
        <AppButton label={t('scoreReport.ahead.no')} variant="secondary" onPress={() => choose('off')} />
      </ButtonRow>
    </AppCard>
  );
}

const styles = StyleSheet.create({
  on: { gap: spacing.xs },
});
