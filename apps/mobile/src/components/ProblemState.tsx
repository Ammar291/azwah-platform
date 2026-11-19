import { useRouter } from 'expo-router';
import { useTranslation } from 'react-i18next';

import { ErrorState } from '@/design-system/ErrorState';
import { Notice } from '@/design-system/Notice';

import { describeProblem } from './describeProblem';

export interface ProblemStateProps {
  error: unknown;
  onRetry?: () => void;
  /** `inline` for a failed action inside a screen; `screen` when the whole screen could not load. */
  variant?: 'screen' | 'inline';
}

/**
 * A translated problem. Missing participation consent leads to the consent screen; anything not found leads home.
 * A 401 never reaches here as a state: it ends the session (see httpParticipantApi).
 */
export function ProblemState({ error, onRetry, variant = 'screen' }: ProblemStateProps) {
  const { t, i18n } = useTranslation();
  const router = useRouter();
  const problem = describeProblem(error, (key) => i18n.exists(key));
  const needsConsent = problem.code === 'sessions.participation_consent_required';
  const forward = needsConsent
    ? { label: t('consent.review'), onPress: () => router.push('/consent') }
    : problem.kind === 'not_found'
      ? { label: t('common.goHome'), onPress: () => router.replace('/home') }
      : undefined;
  const retryable = onRetry && !needsConsent && problem.kind !== 'not_found';

  if (variant === 'inline') {
    return (
      <Notice
        tone="danger"
        title={t(problem.titleKey)}
        message={t(problem.messageKey)}
        action={forward ?? (retryable ? { label: t('common.retry'), onPress: onRetry } : undefined)}
        announce
      />
    );
  }
  return (
    <ErrorState
      title={t(problem.titleKey)}
      message={t(problem.messageKey)}
      retry={retryable ? { label: t('common.retry'), onPress: onRetry } : undefined}
      action={forward}
    />
  );
}
