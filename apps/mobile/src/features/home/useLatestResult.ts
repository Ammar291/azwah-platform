import { useRouter } from 'expo-router';

import { useResilienceTrend } from '@/api/participant/queries';
import type { ScoreReport } from '@/api/participant/schemas';
import { useSavedReport } from '@/features/score-report/useScoreReport';

export interface LatestResult {
  /** The family survey's latest recorded result, for the head of household (D-132); null otherwise or while loading. */
  sessionId: string | null;
  /** Its report sealed on this phone, if one was written (D-113). */
  report: ScoreReport | null;
  /** Opens that result, where its report and next steps are; null when there is none. */
  open: (() => void) | null;
}

/**
 * The latest recorded result of the family survey and its report on this phone, where the family's written next steps
 * are (D-114, D-146). The trend lists the household assessment's recorded results, oldest first, for the head only, and
 * is refetched after every submission, so this follows a new result at once.
 */
export function useLatestResult(enabled = true): LatestResult {
  const router = useRouter();
  const trend = useResilienceTrend(enabled);
  const points = trend.data?.scoreVisibility === 'visible' ? trend.data.points : [];
  const sessionId = points[points.length - 1]?.sessionId ?? null;
  const report = useSavedReport(sessionId);
  return {
    sessionId,
    report: report.data ?? null,
    open: sessionId ? () => router.push({ pathname: '/sessions/[sessionId]/result', params: { sessionId } }) : null,
  };
}
