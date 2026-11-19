import { useMemo, useState } from 'react';
import { StyleSheet, View } from 'react-native';

import { useSupportAuthorities } from '@/api/participant/queries';
import type { SupportAuthority } from '@/api/participant/schemas';
import { ProblemState } from '@/components/ProblemState';
import { AppButton } from '@/design-system/AppButton';
import { AppCard } from '@/design-system/AppCard';
import { AppText } from '@/design-system/AppText';
import { LoadingState } from '@/design-system/LoadingState';
import { Section } from '@/design-system/Section';
import { spacing } from '@/design-system/tokens';
import { openDialler } from '@/features/emergency/dialler';
import { matchesSearch } from '@/features/emergency/emergencySearch';
import { useLocalization } from '@/i18n/useLocalization';

import { AuthorityMark } from './AuthorityMark';
import { openPage } from './openPage';

/** Everything a person may type to find an organisation, in both languages: they may not know which language it uses. */
function searchRow(authority: SupportAuthority) {
  return {
    dial: (authority.phone ?? '').replace(/ /g, ''),
    texts: [
      authority.name.en,
      authority.name.ar,
      authority.purpose.en,
      authority.purpose.ar,
      ...authority.services.flatMap((service) => [service.name.en, service.name.ar, service.summary.en, service.summary.ar]),
    ],
  };
}

/**
 * The organisations in the support knowledge graph (D-115), below the directory in All services and found with the same
 * search. The same for everyone and linked to nothing about a family; the report's next steps are where answers count.
 */
export function SupportAuthorityList({ query }: { query: string }) {
  const { t } = useLocalization();
  const authorities = useSupportAuthorities();
  const shown = useMemo(
    () => authorities.data?.filter((authority) => matchesSearch(searchRow(authority), query)) ?? [],
    [authorities.data, query],
  );

  return (
    <Section title={t('services.authorities.title')} description={t('services.authorities.description')}>
      {authorities.isPending ? <LoadingState label={t('services.authorities.loading')} /> : null}
      {authorities.error ? (
        <ProblemState error={authorities.error} variant="inline" onRetry={() => void authorities.refetch()} />
      ) : null}
      {authorities.data && shown.length === 0 ? (
        <AppCard variant="muted" testID="authorities-none">
          <AppText tone="secondary">
            {query.trim() ? t('services.authorities.none', { query: query.trim() }) : t('services.authorities.empty')}
          </AppText>
        </AppCard>
      ) : null}
      {shown.length > 0 ? (
        <View style={styles.list}>
          {shown.map((authority) => (
            <AuthorityRow key={authority.code} authority={authority} />
          ))}
        </View>
      ) : null}
      <AppText variant="caption" tone="muted">
        {t('services.authorities.symbols')}
      </AppText>
    </Section>
  );
}

function AuthorityRow({ authority }: { authority: SupportAuthority }) {
  const { t, language, formatDate, formatNumber } = useLocalization();
  const [open, setOpen] = useState(false);
  const pick = (text: { en: string; ar: string }) => (language === 'ar' ? text.ar : text.en);
  const name = pick(authority.name);
  return (
    <AppCard testID={`authority-${authority.code}`}>
      <View style={styles.heading}>
        <AuthorityMark code={authority.code} />
        <View style={styles.title}>
          <AppText variant="bodyStrong">{name}</AppText>
          <AppText variant="caption" tone="secondary">
            {authority.emirates.map((emirate) => t(`emergency.coverage.${emirate}`)).join(t('services.authorities.separator'))}
          </AppText>
        </View>
      </View>
      <AppText>{pick(authority.purpose)}</AppText>
      {authority.kind === 'private' ? (
        <AppText variant="caption" tone="secondary">
          {t('services.authorities.private')}
        </AppText>
      ) : null}
      {authority.services.length > 0 ? (
        <AppButton
          label={
            open
              ? t('services.authorities.hideServices')
              : t('services.authorities.showServices', { total: formatNumber(authority.services.length) })
          }
          accessibilityLabel={
            open ? t('services.authorities.hideServicesLabel', { name }) : t('services.authorities.showServicesLabel', { name })
          }
          accessibilityState={{ expanded: open }}
          variant="tertiary"
          onPress={() => setOpen((current) => !current)}
        />
      ) : null}
      {open
        ? authority.services.map((service) => (
            <View key={service.code} style={styles.service} testID={`authority-service-${service.code}`}>
              <AppText variant="bodyStrong">{pick(service.name)}</AppText>
              <AppText tone="secondary">{pick(service.summary)}</AppText>
              {service.via ? (
                <AppText variant="caption" tone="secondary">
                  {t('scoreReport.flow.via', { name: pick(service.via) })}
                </AppText>
              ) : null}
              {service.procedure.length > 0 ? (
                service.procedure.map((step, index) => (
                  <AppText key={`${index}-${step.en}`}>
                    {t('scoreReport.flow.procedureStep', { number: formatNumber(index + 1), step: pick(step) })}
                  </AppText>
                ))
              ) : (
                <AppText variant="caption" tone="muted">
                  {t('scoreReport.flow.noProcedure')}
                </AppText>
              )}
              {service.documents.length > 0 ? (
                <>
                  <AppText variant="label">{t('scoreReport.flow.documents')}</AppText>
                  {service.documents.map((document, index) => (
                    <AppText key={`${index}-${document.en}`}>{t('scoreReport.step', { step: pick(document) })}</AppText>
                  ))}
                </>
              ) : null}
              {service.website ? (
                <AppButton
                  label={t('scoreReport.flow.servicePage')}
                  accessibilityLabel={t('scoreReport.flow.servicePageLabel', { name: pick(service.name) })}
                  variant="tertiary"
                  onPress={() => void openPage(service.website ?? '')}
                />
              ) : null}
            </View>
          ))
        : null}
      {authority.website ? (
        <AppButton
          label={t('services.directory.visit')}
          accessibilityLabel={t('services.directory.visitLabel', { name })}
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
      <AppText variant="caption" tone="muted">
        {t('scoreReport.flow.checkedOn', { date: formatDate(authority.checkedOn, 'date') })}
      </AppText>
    </AppCard>
  );
}

const styles = StyleSheet.create({
  list: { gap: spacing.sm },
  heading: { flexDirection: 'row', gap: spacing.sm, alignItems: 'center' },
  title: { flex: 1 },
  service: { gap: spacing.xs },
});
