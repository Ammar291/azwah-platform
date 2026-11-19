import { zodResolver } from '@hookform/resolvers/zod';
import { useMemo, useState } from 'react';
import { Controller, useForm } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { StyleSheet, View } from 'react-native';

import { useMe, useMembers, usePrivacyRequests } from '@/api/participant/queries';
import type { Me, Member, PrivacyRequest } from '@/api/participant/schemas';
import { describeProblem } from '@/components/describeProblem';
import { ProblemState } from '@/components/ProblemState';
import { useSingleFlight } from '@/components/useSingleFlight';
import { AppButton } from '@/design-system/AppButton';
import { AppCard } from '@/design-system/AppCard';
import { AppText } from '@/design-system/AppText';
import { Badge, type BadgeTone } from '@/design-system/Badge';
import { ConfirmationDialog } from '@/design-system/ConfirmationDialog';
import { DetailRow } from '@/design-system/DetailRow';
import { LoadingState } from '@/design-system/LoadingState';
import { Notice } from '@/design-system/Notice';
import { RadioCardGroup } from '@/design-system/RadioCardGroup';
import { spacing } from '@/design-system/tokens';
import { memberReference, memberTitle } from '@/features/household/memberLabels';
import { useLocalization } from '@/i18n/useLocalization';

import { useCreatePrivacyRequest } from './privacyMutations';
import {
  createPrivacyRequestSchema,
  PRIVACY_REQUEST_TYPES,
  type PrivacyRequestFormInput,
  type PrivacyRequestFormOutput,
} from './privacyRequestForm';

/**
 * "Ask about your data": access, correction or erasure for the person signed in or, for a household manager, a
 * member they act for (the server's `actableMemberIds`, D-49); a confirmation; then the requests and their status.
 * The programme team handles and closes requests; nothing here decides them.
 */
export function PrivacyRequests() {
  const { t } = useLocalization();
  const me = useMe();
  if (me.isPending) {
    return <LoadingState label={t('profile.data.requests.loading')} />;
  }
  if (me.error) {
    return <ProblemState error={me.error} variant="inline" onRetry={() => void me.refetch()} />;
  }
  if (!me.data.household) {
    return <AppText tone="secondary">{t('profile.data.requests.noHousehold')}</AppText>;
  }
  return <RequestsForHousehold household={me.data.household} />;
}

function RequestsForHousehold({ household }: { household: NonNullable<Me['household']> }) {
  const localization = useLocalization();
  const { t } = localization;
  const members = useMembers(household.householdId);
  const create = useCreatePrivacyRequest();
  const once = useSingleFlight();
  const [confirming, setConfirming] = useState<PrivacyRequestFormOutput | null>(null);
  const [sent, setSent] = useState(false);
  const schema = useMemo(
    () => createPrivacyRequestSchema(t, household.actableMemberIds),
    [t, household.actableMemberIds],
  );
  const form = useForm<PrivacyRequestFormInput, unknown, PrivacyRequestFormOutput>({
    resolver: zodResolver(schema),
    defaultValues: { type: null, memberId: household.memberId },
  });

  const actable = (members.data ?? []).filter((member) => household.actableMemberIds.includes(member.id));
  const canChoose = household.role === 'manager' && actable.length > 1;
  const describe = (memberId: string) => {
    const member = members.data?.find((candidate) => candidate.id === memberId);
    return member ? memberReference(member, localization) : t('profile.data.requests.unknownMember');
  };

  /** Any change to the form clears the last outcome, so a message never describes another request. */
  const changed = (onChange: (value: string) => void) => (value: string) => {
    setSent(false);
    create.reset();
    onChange(value);
  };

  const send = async (values: PrivacyRequestFormOutput) => {
    try {
      await create.mutateAsync({ type: values.type, memberId: values.memberId });
      setSent(true);
      form.reset({ type: null, memberId: values.memberId });
    } catch {
      // Shown from the mutation's error below.
    } finally {
      setConfirming(null);
    }
  };

  return (
    <>
      <Controller
        control={form.control}
        name="type"
        render={({ field, fieldState }) => (
          <RadioCardGroup
            label={t('profile.data.requests.typeLabel')}
            options={PRIVACY_REQUEST_TYPES.map((type) => ({
              value: type,
              label: t(`profile.data.requests.types.${type}.title`),
              description: t(`profile.data.requests.types.${type}.description`),
            }))}
            value={field.value}
            onChange={changed(field.onChange)}
            error={fieldState.error?.message}
          />
        )}
      />
      {canChoose ? (
        <Controller
          control={form.control}
          name="memberId"
          render={({ field, fieldState }) => (
            <RadioCardGroup
              label={t('profile.data.requests.memberLabel')}
              density="compact"
              options={actable.map((member: Member) => ({
                value: member.id,
                label: memberTitle(member, t),
                description: t('members.bornIn', { year: String(member.birthYear) }),
              }))}
              value={field.value}
              onChange={changed(field.onChange)}
              error={fieldState.error?.message}
            />
          )}
        />
      ) : null}
      {create.error ? <RequestProblem error={create.error} /> : null}
      {sent ? (
        <Notice
          tone="success"
          title={t('profile.data.requests.sent.title')}
          message={t('profile.data.requests.sent.message')}
          announce
        />
      ) : null}
      <AppButton
        label={t('profile.data.requests.submit')}
        onPress={form.handleSubmit((values) => setConfirming(values))}
      />

      <RequestList describe={describe} />

      <ConfirmationDialog
        visible={confirming !== null}
        title={t('profile.data.requests.confirm.title')}
        message={
          confirming
            ? t('profile.data.requests.confirm.message', {
                type: t(`profile.data.requests.types.${confirming.type}.title`),
                member: describe(confirming.memberId),
              })
            : ''
        }
        confirmLabel={t('profile.data.requests.confirm.confirm')}
        cancelLabel={t('common.cancel')}
        busy={create.isPending}
        onConfirm={() => {
          if (confirming) void once(() => send(confirming));
        }}
        onCancel={() => setConfirming(null)}
      />
    </>
  );
}

/** A refused request in words chosen by its problem code (an open one already, a member not found, a rate limit). */
function RequestProblem({ error }: { error: unknown }) {
  const { t, i18n } = useTranslation();
  const problem = describeProblem(error, (key) => i18n.exists(key));
  return <Notice tone="danger" title={t(problem.titleKey)} message={t(problem.messageKey)} announce />;
}

const RESOLUTION_TONES: Record<NonNullable<PrivacyRequest['resolution']>, BadgeTone> = {
  completed: 'success',
  declined: 'neutral',
  withdrawn: 'neutral',
};

function RequestList({ describe }: { describe: (memberId: string) => string }) {
  const { t } = useLocalization();
  const requests = usePrivacyRequests();
  if (requests.isPending) {
    return <LoadingState label={t('profile.data.requests.loading')} />;
  }
  if (requests.error) {
    return <ProblemState error={requests.error} variant="inline" onRetry={() => void requests.refetch()} />;
  }
  const seen = new Set<string>();
  const items = requests.data.pages
    .flatMap((page) => page.items)
    .filter((request) => (seen.has(request.id) ? false : (seen.add(request.id), true)));

  return (
    <View style={styles.list}>
      <AppText variant="bodyStrong" accessibilityRole="header">
        {t('profile.data.requests.listTitle')}
      </AppText>
      {items.length === 0 ? <AppText tone="secondary">{t('profile.data.requests.none')}</AppText> : null}
      {items.map((request) => (
        <RequestCard key={request.id} request={request} member={describe(request.memberId)} />
      ))}
      {requests.hasNextPage ? (
        <AppButton
          label={t('profile.data.requests.showMore')}
          variant="tertiary"
          loading={requests.isFetchingNextPage}
          onPress={() => void requests.fetchNextPage()}
        />
      ) : null}
    </View>
  );
}

function RequestCard({ request, member }: { request: PrivacyRequest; member: string }) {
  const { t, formatDate } = useLocalization();
  const badge =
    request.status === 'closed' && request.resolution
      ? { tone: RESOLUTION_TONES[request.resolution], label: t(`profile.data.requests.resolution.${request.resolution}`) }
      : {
          tone: request.status === 'open' ? ('info' as const) : ('neutral' as const),
          label: t(`profile.data.requests.status.${request.status}`),
        };
  return (
    <AppCard testID={`privacy-request-${request.id}`}>
      <AppText variant="bodyStrong">{t(`profile.data.requests.types.${request.type}.title`)}</AppText>
      <Badge tone={badge.tone} label={badge.label} />
      <DetailRow label={t('profile.data.requests.for')} value={member} />
      <DetailRow label={t('profile.data.requests.sentOn')} value={formatDate(request.createdAt)} />
      {request.closedAt ? (
        <DetailRow label={t('profile.data.requests.closedOn')} value={formatDate(request.closedAt)} />
      ) : null}
    </AppCard>
  );
}

const styles = StyleSheet.create({
  list: { gap: spacing.md, marginTop: spacing.sm },
});
