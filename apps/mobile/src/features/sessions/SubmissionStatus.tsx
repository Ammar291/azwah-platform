import { useRouter } from 'expo-router';

import { ProblemState } from '@/components/ProblemState';
import { Notice } from '@/design-system/Notice';
import { useDraftStore } from '@/drafts/DraftStoreContext';
import { useLocalization } from '@/i18n/useLocalization';

import type { SubmissionState } from './submissionQueue';

export interface SubmissionStatusProps {
  state: SubmissionState;
  onRetry: () => void;
}

/**
 * Where a submission stands, in words. Until the server confirms, it always says the answers are not submitted yet
 * (decision M-41), and where they are meanwhile.
 */
export function SubmissionStatus({ state, onRetry }: SubmissionStatusProps) {
  const { t } = useLocalization();
  const router = useRouter();
  const store = useDraftStore();
  switch (state.status) {
    case 'idle':
    case 'submitted':
      return null;
    case 'sending':
      return <Notice tone="info" title={t('submission.sending.title')} message={t('submission.sending.message')} announce />;
    case 'queued': {
      const where = store.persistence;
      const retry = state.reason === 'retrying' || state.reason === 'waiting';
      return (
        <Notice
          tone="warning"
          title={t('submission.queued.title')}
          message={t(`submission.queued.${state.reason}`, { where: t(`submission.where.${where}`) })}
          action={retry ? { label: t('common.retry'), onPress: onRetry } : undefined}
          announce
          testID="submission-queued"
        />
      );
    }
    case 'blocked':
      if (state.reason === 'refused') {
        return <ProblemState error={state.error} variant="inline" />;
      }
      return (
        <Notice
          tone="danger"
          title={t('submission.blocked.title')}
          message={
            state.reason === 'expired'
              ? t('problems.assignments.assignment_expired')
              : t(`submission.blocked.${state.reason}`)
          }
          action={
            state.reason === 'consent_required'
              ? { label: t('consent.review'), onPress: () => router.push('/consent') }
              : undefined
          }
          announce
          testID="submission-blocked"
        />
      );
  }
}
