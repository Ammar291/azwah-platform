import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';

import { useParticipantApi } from '@/api/participant/ParticipantApiContext';
import { invalidateParticipantData } from '@/api/participant/queries';
import type { ConsentPolicy, ConsentRecord } from '@/api/participant/schemas';
import { ProblemState } from '@/components/ProblemState';
import { AppButton } from '@/design-system/AppButton';
import { AppCard } from '@/design-system/AppCard';
import { AppText } from '@/design-system/AppText';
import { Badge } from '@/design-system/Badge';
import { ConfirmationDialog } from '@/design-system/ConfirmationDialog';
import { useLocalization } from '@/i18n/useLocalization';

export interface ConsentPolicyCardProps {
  policy: ConsentPolicy;
  /** The member's active consent of this type, if any. */
  active: ConsentRecord | undefined;
  memberId: string;
}

/** One purpose: what it is for, whether consent is given, and giving or withdrawing it. Withdrawing asks first. */
export function ConsentPolicyCard({ policy, active, memberId }: ConsentPolicyCardProps) {
  const { t, language, formatDate, formatNumber } = useLocalization();
  const api = useParticipantApi();
  const queryClient = useQueryClient();
  const [confirmingWithdrawal, setConfirmingWithdrawal] = useState(false);
  const type = policy.consentType;

  const decide = useMutation({
    mutationFn: (decision: 'grant' | 'withdraw') =>
      api.recordConsent({ memberId, policyVersionId: policy.id, decision }),
    onSettled: () => setConfirmingWithdrawal(false),
    onSuccess: () => invalidateParticipantData(queryClient),
  });

  const title = t(`consent.types.${type}.title`);
  return (
    <AppCard testID={`consent-${type}`}>
      <AppText variant="heading" accessibilityRole="header">
        {title}
      </AppText>
      <Badge tone={active ? 'success' : 'neutral'} label={t(active ? 'consent.given' : 'consent.notGiven')} />
      <AppText>{t(`consent.types.${type}.purpose`)}</AppText>
      {active ? (
        <AppText variant="caption" tone="muted">
          {t('consent.givenOn', { date: formatDate(active.grantedAt) })}
        </AppText>
      ) : null}
      {/* An unapproved policy has no document to cite yet; the notice above the cards says its text awaits approval. */}
      {policy.approvedAt ? (
        <AppText variant="caption" tone="muted">
          {t('consent.documentReference', {
            reference: language === 'ar' ? policy.documentReferenceAr : policy.documentReferenceEn,
            version: formatNumber(policy.versionNumber),
          })}
        </AppText>
      ) : null}
      {decide.error ? <ProblemState error={decide.error} variant="inline" /> : null}
      {active ? (
        <AppButton
          label={t('consent.withdraw')}
          variant="secondary"
          accessibilityHint={title}
          onPress={() => setConfirmingWithdrawal(true)}
        />
      ) : (
        <AppButton
          label={t('consent.give')}
          accessibilityHint={title}
          loading={decide.isPending}
          onPress={() => decide.mutate('grant')}
        />
      )}
      <ConfirmationDialog
        visible={confirmingWithdrawal}
        destructive
        title={t('consent.withdrawTitle', { purpose: title })}
        message={t(`consent.types.${type}.withdrawMessage`)}
        confirmLabel={t('consent.withdraw')}
        cancelLabel={t('consent.keep')}
        busy={decide.isPending}
        onConfirm={() => decide.mutate('withdraw')}
        onCancel={() => setConfirmingWithdrawal(false)}
      />
    </AppCard>
  );
}
