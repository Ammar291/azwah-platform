import { StyleSheet, View } from 'react-native';

import type { ReportAuthority, ReportNextStep } from '@/api/participant/schemas';
import { AppButton } from '@/design-system/AppButton';
import { AppText } from '@/design-system/AppText';
import { ButtonRow } from '@/design-system/ButtonRow';
import { Notice } from '@/design-system/Notice';
import { borderWidths, colors, radii, spacing } from '@/design-system/tokens';
import { openDialler } from '@/features/emergency/dialler';
import { AuthorityMark } from '@/features/services/AuthorityMark';
import { openPage } from '@/features/services/openPage';
import { useLocalization } from '@/i18n/useLocalization';

const NUMBER_SIZE = 32;

/**
 * The family's way forward, as numbered steps with the area needing most attention first (D-114). In each, a model's words
 * say why and what to start at home, and the knowledge graph says where to go (D-115), compactly (D-145): each
 * organisation's name, where it serves and the services the graph reached, with its website, phone and service pages, where
 * the procedure is. All services keeps the full entries. Where the graph reached nothing, the step says so instead of
 * pretending. Azwah contacts nobody.
 */
export function NextStepsFlow({ steps }: { steps: readonly ReportNextStep[] }) {
  const { t, formatNumber } = useLocalization();
  if (steps.length === 0) return null;
  return (
    <View style={styles.flow} testID="report-next-steps">
      <AppText variant="bodyStrong" accessibilityRole="header">
        {t('scoreReport.flow.title')}
      </AppText>
      {steps.map((step, index) => (
        <View key={`${index}-${step.sectionCode}`} style={styles.step} testID={`report-step-${index + 1}`}>
          <View style={styles.rail}>
            <View style={styles.number}>
              <AppText variant="label" tone="inverse">
                {formatNumber(index + 1)}
              </AppText>
            </View>
            {index < steps.length - 1 ? <View style={styles.line} /> : null}
          </View>
          <View style={styles.content}>
            <AppText variant="bodyStrong" accessibilityRole="header">
              {t('scoreReport.flow.step', { number: formatNumber(index + 1), area: step.area })}
            </AppText>
            <AppText tone="secondary">{step.reason}</AppText>
            {step.steps.map((text, position) => (
              <AppText key={`${position}-${text}`}>{t('scoreReport.step', { step: text })}</AppText>
            ))}
            <AppText variant="label">{t('scoreReport.flow.whereToGo')}</AppText>
            {step.authorities.length === 0 ? (
              <AppText tone="muted" variant="caption">
                {t('scoreReport.flow.noAuthorities')}
              </AppText>
            ) : (
              step.authorities.map((authority) => <AuthorityCard key={authority.code} authority={authority} />)
            )}
          </View>
        </View>
      ))}
      <Notice tone="info" message={t('scoreReport.flow.notice')} />
    </View>
  );
}

function AuthorityCard({ authority }: { authority: ReportAuthority }) {
  const { t, formatDate } = useLocalization();
  return (
    <View style={styles.authority} testID={`report-authority-${authority.code}`}>
      <View style={styles.heading}>
        <AuthorityMark code={authority.code} size={32} />
        <View style={styles.title}>
          <AppText variant="bodyStrong">{authority.name}</AppText>
          <AppText variant="caption" tone="secondary">
            {authority.emirates.map((emirate) => t(`emergency.coverage.${emirate}`)).join(t('services.authorities.separator'))}
          </AppText>
        </View>
      </View>
      {authority.kind === 'private' ? (
        <AppText variant="caption" tone="secondary">
          {t('services.authorities.private')}
        </AppText>
      ) : null}
      {authority.services.map((service) => (
        <View key={service.code} style={styles.service} testID={`report-service-${service.code}`}>
          <AppText style={styles.serviceName}>{service.name}</AppText>
          {service.website ? (
            <AppButton
              label={t('scoreReport.flow.servicePage')}
              accessibilityLabel={t('scoreReport.flow.servicePageLabel', { name: service.name })}
              variant="tertiary"
              onPress={() => void openPage(service.website ?? '')}
            />
          ) : null}
        </View>
      ))}
      {authority.website || authority.phone ? (
        <ButtonRow>
          {authority.website ? (
            <AppButton
              label={t('scoreReport.flow.website')}
              variant="secondary"
              onPress={() => void openPage(authority.website ?? '')}
            />
          ) : null}
          {authority.phone ? (
            <AppButton
              label={t('scoreReport.flow.call', { phone: authority.phone })}
              variant="secondary"
              onPress={() => void openDialler((authority.phone ?? '').replace(/ /g, ''))}
            />
          ) : null}
        </ButtonRow>
      ) : null}
      <AppText variant="caption" tone="muted">
        {t('scoreReport.flow.checked', { date: formatDate(authority.checkedOn, 'date') })}
      </AppText>
    </View>
  );
}

const styles = StyleSheet.create({
  flow: { gap: spacing.md },
  step: { flexDirection: 'row', gap: spacing.md },
  rail: { alignItems: 'center', width: NUMBER_SIZE },
  number: {
    width: NUMBER_SIZE,
    height: NUMBER_SIZE,
    borderRadius: radii.pill,
    backgroundColor: colors.primary,
    alignItems: 'center',
    justifyContent: 'center',
  },
  line: { flex: 1, width: borderWidths.strong, backgroundColor: colors.border, marginTop: spacing.xs },
  content: { flex: 1, gap: spacing.xs, paddingBottom: spacing.md },
  authority: {
    gap: spacing.xs,
    padding: spacing.md,
    borderRadius: radii.md,
    borderWidth: borderWidths.hairline,
    borderColor: colors.borderStrong,
    backgroundColor: colors.surfaceMuted,
  },
  heading: { flexDirection: 'row', gap: spacing.sm, alignItems: 'center' },
  title: { flex: 1 },
  service: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', flexWrap: 'wrap', gap: spacing.xs },
  serviceName: { flexShrink: 1 },
});
