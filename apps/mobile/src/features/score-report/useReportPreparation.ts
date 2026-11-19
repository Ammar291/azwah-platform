import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useCallback, useEffect } from 'react';

import { useParticipantApi } from '@/api/participant/ParticipantApiContext';
import { participantKeys } from '@/api/participant/queries';
import type { SessionContent } from '@/features/sessions/useSessionContent';
import { useLocalization } from '@/i18n/useLocalization';

import { planReport } from './reportPlan';
import type { ReportChoice } from './reportStore';
import { useReportPreparer, useScoreReportStore } from './ScoreReportStoreContext';

export interface ReportPreparation {
  /** Whether this session's report can be written ahead: asked only of the people it applies to. */
  eligible: boolean;
  /** Null until the person has said; nothing is sent while it is null or `off`. */
  choice: ReportChoice | null;
  choose(choice: ReportChoice): void;
}

/**
 * Follows the answers the server has confirmed: after each save, any section now fully answered, or changed, goes to the
 * preparer in the background. It sends nothing until the person agrees, and reads only the server's copy, so nothing is
 * sent for an answer the platform has not recorded.
 */
export function useReportPreparation(content: SessionContent): ReportPreparation {
  const { session, version, serverAnswers, source } = content;
  const api = useParticipantApi();
  const store = useScoreReportStore();
  const preparer = useReportPreparer();
  const queryClient = useQueryClient();
  const { language } = useLocalization();
  const inProgress = session.status === 'in_progress';

  const eligibility = useQuery({
    queryKey: participantKeys.reportEligibility(session.id),
    queryFn: () => api.getScoreReportEligibility(session.id),
    enabled: inProgress && source === 'server',
    staleTime: 60_000,
  });
  const stored = useQuery({
    queryKey: participantKeys.reportChoice(session.id),
    queryFn: () => store.getChoice(session.id),
    networkMode: 'always',
    staleTime: Infinity,
  });
  const eligible = eligibility.data?.eligible === true;
  const choice = stored.data ?? null;

  useEffect(() => {
    if (eligible && choice === 'on' && inProgress && source === 'server') {
      preparer.update(session.id, language, planReport(version, session.sectionCode, serverAnswers).parts);
    }
  }, [choice, eligible, inProgress, language, preparer, serverAnswers, session.id, session.sectionCode, source, version]);

  const choose = useCallback(
    (next: ReportChoice) => {
      queryClient.setQueryData(participantKeys.reportChoice(session.id), next);
      void store.setChoice(session.id, next).catch(() => undefined);
      if (next === 'off') {
        preparer.reset();
        void store.deleteParts(session.id).catch(() => undefined);
      }
    },
    [preparer, queryClient, session.id, store],
  );

  return { eligible, choice, choose };
}
