import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useCallback, useEffect, useRef, useState } from 'react';

import { isApiError } from '@/api/client';
import { useParticipantApi } from '@/api/participant/ParticipantApiContext';
import { participantKeys } from '@/api/participant/queries';
import type { ScoreReport } from '@/api/participant/schemas';
import { useSessionContent } from '@/features/sessions/useSessionContent';
import { useLocalization } from '@/i18n/useLocalization';

import { assembleReport, planReport } from './reportPlan';
import { useReportPreparer, useScoreReportStore } from './ScoreReportStoreContext';

export type ReportProblem = 'unavailable' | 'offline' | 'rate_limited' | 'failed';

/** The score the server recorded: the only source of a report's total, band and scale. */
export type RecordedScore = Pick<ScoreReport, 'totalScore' | 'tierCode' | 'scale'>;

function problemOf(error: unknown): ReportProblem {
  if (!isApiError(error)) return 'failed';
  // 503 is how this endpoint says no model is connected (D-112); no other 5xx code is read.
  if (error.status === 503) return 'unavailable';
  if (error.kind === 'rate_limited') return 'rate_limited';
  if (error.kind === 'network' || error.kind === 'timeout') return 'offline';
  return 'failed';
}

/**
 * The report sealed on this phone for a session, in the reader's language, or null (D-146: Home lists its next steps).
 * A local read, so it runs offline too; saving a report refreshes it.
 */
export function useSavedReport(sessionId: string | null) {
  const store = useScoreReportStore();
  const { language } = useLocalization();
  return useQuery({
    queryKey: participantKeys.savedReport(sessionId ?? '', language),
    queryFn: () => store.get(sessionId ?? '', language),
    enabled: sessionId !== null,
    networkMode: 'always',
  });
}

/** Keeps a finished report, drops the parts it was joined from, and tells Home it is there. Never fails the screen. */
export function useKeepReport(): (sessionId: string, report: ScoreReport) => void {
  const store = useScoreReportStore();
  const queryClient = useQueryClient();
  return useCallback(
    (sessionId, report) => {
      void store
        .save(report)
        .then(() => queryClient.invalidateQueries({ queryKey: participantKeys.savedReports() }))
        .then(() => store.deleteParts(sessionId))
        .catch(() => undefined);
    },
    [queryClient, store],
  );
}

export interface ScoreReportState {
  /** False until the phone has looked for a saved report and the person's choice. */
  loaded: boolean;
  report: ScoreReport | null;
  /** The person agreed to have it written ahead, so the report is being finished without asking again. */
  writtenAhead: boolean;
  creating: boolean;
  problem: ReportProblem | null;
  /** Writes the whole report in one call, the slow way. Only a person's own tap calls this. */
  create(): void;
}

/**
 * The session's report in the reader's language: the one saved on this phone, or one written from scratch on request. A
 * report finished after the person has left the screen is dropped, so nothing is saved after a sign-out or a lock.
 */
export function useScoreReport(sessionId: string, recorded: RecordedScore): ScoreReportState {
  const store = useScoreReportStore();
  const api = useParticipantApi();
  const { language } = useLocalization();
  const queryClient = useQueryClient();
  const [report, setReport] = useState<ScoreReport | null>(null);
  const [writtenAhead, setWrittenAhead] = useState(false);
  const [loaded, setLoaded] = useState(false);
  const [creating, setCreating] = useState(false);
  const [problem, setProblem] = useState<ReportProblem | null>(null);
  const mounted = useRef(true);

  useEffect(() => {
    mounted.current = true;
    let current = true;
    void Promise.all([store.get(sessionId, language), store.getChoice(sessionId)])
      .then(([saved, choice]) => {
        if (current) {
          setReport(saved);
          setWrittenAhead(saved === null && choice === 'on');
        }
      })
      .catch(() => undefined)
      .finally(() => {
        if (current) setLoaded(true);
      });
    return () => {
      current = false;
      mounted.current = false;
    };
  }, [language, sessionId, store]);

  const keep = useKeepReport();
  const finished = useCallback(
    (done: ScoreReport) => {
      setReport(done);
      setWrittenAhead(false);
      keep(sessionId, done);
    },
    [keep, sessionId],
  );

  const create = useCallback(() => {
    if (creating) return;
    setCreating(true);
    setProblem(null);
    void (async () => {
      try {
        const created = await api.createScoreReport(sessionId, language);
        if (mounted.current) finished({ ...created, ...recorded });
      } catch (error) {
        const next = problemOf(error);
        if (next === 'unavailable') {
          void queryClient.invalidateQueries({ queryKey: participantKeys.assistantAvailability() });
        }
        if (mounted.current) setProblem(next);
      } finally {
        if (mounted.current) setCreating(false);
      }
    })();
  }, [api, creating, finished, language, queryClient, recorded, sessionId]);

  return { loaded, report: report, writtenAhead, creating, problem, create };
}

export type FinishState = 'finishing' | 'not_finished';

/**
 * Joins the parts written as the person answered with the score the server recorded: no model and no waiting when they are
 * all there (D-113). Anything missing (answered offline, submitted the moment the last call began, a failed call) is
 * written now, and the report appears when it arrives. If that fails, the caller offers the whole-report call instead.
 */
export function useFinishReport(sessionId: string, recorded: RecordedScore, onFinished: (report: ScoreReport) => void): FinishState {
  const store = useScoreReportStore();
  const preparer = useReportPreparer();
  const { language } = useLocalization();
  const content = useSessionContent(sessionId);
  const [state, setState] = useState<FinishState>('finishing');
  const version = content.data?.version;
  const sectionCode = content.data?.session.sectionCode ?? null;
  const answers = content.data?.serverAnswers;
  const { totalScore, tierCode, scale } = recorded;

  useEffect(() => {
    if (content.error) {
      const failed = setTimeout(() => setState('not_finished'), 0);
      return () => clearTimeout(failed);
    }
    if (!version || !answers) return undefined;
    let current = true;
    const plan = planReport(version, sectionCode, answers);
    const attempt = async () =>
      assembleReport({ sessionId, language, totalScore, tierCode, scale }, plan, await store.listParts(sessionId, language));
    void (async () => {
      const ready = await attempt();
      if (ready) return ready;
      preparer.update(sessionId, language, plan.parts);
      await preparer.settled(sessionId);
      return attempt();
    })()
      .then((report) => {
        if (!current) return;
        if (report) onFinished(report);
        else setState('not_finished');
      })
      .catch(() => {
        if (current) setState('not_finished');
      });
    return () => {
      current = false;
    };
    // `onFinished` comes from a parent render; the work depends only on the session's records.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [answers, content.error, language, preparer, scale, sectionCode, sessionId, store, tierCode, totalScore, version]);

  return state;
}
