import { Notice } from '@/design-system/Notice';
import { useLocalization } from '@/i18n/useLocalization';

import { closedByExpiry, type AnswerSyncSnapshot } from './answerSync';
import type { SessionContent } from './useSessionContent';

/**
 * What the person must know about this phone's copy of the session: that it is a copy (offline), that answers could
 * not be kept, and that the time to answer has passed (the server refused answers; it may still list the session as
 * in progress until maintenance closes it). Nothing about saved answers is ever lost silently.
 */
export function SessionNotices({ content, sync }: { content: SessionContent; sync: AnswerSyncSnapshot }) {
  const { t, formatNumber, formatDate } = useLocalization();
  const expired = closedByExpiry(sync);
  return (
    <>
      {expired ? (
        <Notice
          tone="danger"
          title={t('session.abandoned.title')}
          message={t('problems.assignments.assignment_expired')}
          announce
          testID="session-expired"
        />
      ) : null}
      {expired && sync.discarded > 0 ? (
        <Notice
          tone="warning"
          title={t('session.notIncluded.title')}
          message={t('session.notIncluded.message', { count: formatNumber(sync.discarded) })}
          announce
        />
      ) : null}
      {content.source === 'device' ? (
        <Notice
          tone="info"
          title={t('session.offlineCopy.title')}
          message={t('session.offlineCopy.message', { date: formatDate(content.copiedAt ?? '', 'dateTime') })}
          testID="offline-copy"
        />
      ) : null}
      {sync.lost > 0 ? (
        <Notice
          tone="warning"
          title={t('session.lost.title')}
          message={t('session.lost.message', { count: formatNumber(sync.lost) })}
          announce
        />
      ) : null}
    </>
  );
}
