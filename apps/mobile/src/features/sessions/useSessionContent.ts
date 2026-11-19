import { useQuery, type UseQueryResult } from '@tanstack/react-query';
import { useEffect } from 'react';

import { isApiError } from '@/api/client';
import {
  participantKeys,
  useAssignment,
  usePublishedVersion,
  useSession,
  useSessionAnswers,
} from '@/api/participant/queries';
import type { Assignment, PublishedItem, PublishedVersion, Session, SessionAnswers } from '@/api/participant/schemas';
import { useDraftStore } from '@/drafts/DraftStoreContext';

import { selectItems } from './questionnaire';

export { selectItems } from './questionnaire';

export interface SessionContent {
  session: Session;
  assignment: Assignment;
  version: PublishedVersion;
  sectionTitle: PublishedVersion['sections'][number]['title'] | null;
  /** The items of the session's section (or of every section), in display order. */
  items: PublishedItem[];
  serverAnswers: SessionAnswers;
  /** `device`: the server could not be reached, so this is the copy kept on the phone (decision M-38). */
  source: 'server' | 'device';
  /** When the copy was saved, for `device`. */
  copiedAt: string | null;
}

/**
 * The version the server returned is not the one the session was started on. Never shown as if it were: a session
 * keeps its version for life (decision M-42).
 */
export class VersionMismatchError extends Error {
  constructor() {
    super("The questionnaire version does not match the session's version.");
    this.name = 'VersionMismatchError';
  }
}

/** No connection (or a server fault) is the only reason to use the copy; an answer from the server never is. */
function unreachable(query: Pick<UseQueryResult, 'error' | 'fetchStatus' | 'data'>): boolean {
  if (query.data === undefined && query.fetchStatus === 'paused') return true;
  const { error } = query;
  return isApiError(error) && (error.kind === 'network' || error.kind === 'timeout' || error.kind === 'server');
}

/**
 * Loads a session, its assignment (for the instrument code), the published version and the saved answers, as one
 * loading or problem state. When all four arrive from the server, the version is checked against the session's and a
 * sealed copy is kept on the phone. When the server cannot be reached, the copy is used instead, never mixed with
 * server data.
 */
export function useSessionContent(sessionId: string) {
  const store = useDraftStore();
  const session = useSession(sessionId);
  const assignment = useAssignment(session.data?.assignmentId);
  const version = usePublishedVersion(assignment.data?.instrumentCode, session.data?.versionNumber);
  const answers = useSessionAnswers(sessionId);
  const copy = useQuery({
    queryKey: participantKeys.localCopy(sessionId),
    queryFn: () => store.loadCopy(sessionId),
    networkMode: 'always',
    staleTime: Infinity,
  });

  const fromServer =
    session.data && assignment.data && version.data && answers.data
      ? { session: session.data, assignment: assignment.data, version: version.data, serverAnswers: answers.data }
      : undefined;
  const mismatch = fromServer !== undefined && fromServer.version.versionId !== fromServer.session.instrumentVersionId;

  // Kept only once everything was read from the server, signed in, and only for the version the session uses.
  // Query data keeps its identity until it changes, so the copy is written again only when a read changed.
  const [sessionData, assignmentData, versionData, answersData] = [session.data, assignment.data, version.data, answers.data];
  useEffect(() => {
    if (
      sessionData?.status === 'in_progress' &&
      assignmentData &&
      versionData &&
      answersData &&
      versionData.versionId === sessionData.instrumentVersionId
    ) {
      const content = { session: sessionData, assignment: assignmentData, version: versionData };
      void store.saveCopy(sessionId, content, answersData, new Date()).catch(() => undefined);
    }
  }, [sessionData, assignmentData, versionData, answersData, sessionId, store]);

  const queries = [session, assignment, version, answers];
  const serverError = queries.find((query) => query.error)?.error ?? null;
  const offline = queries.some(unreachable);
  const stored = copy.data && copy.data.content.session.id === sessionId ? copy.data : null;

  let data: SessionContent | undefined;
  let error: unknown = serverError;
  if (fromServer && !mismatch) {
    data = {
      ...fromServer,
      ...selectItems(fromServer.version, fromServer.session.sectionCode),
      source: 'server',
      copiedAt: null,
    };
  } else if (mismatch) {
    error = new VersionMismatchError();
  } else if (offline && stored) {
    // The server's session now uses another version: the copy is not used for it.
    const { content, serverAnswers } = stored;
    if (content.version.versionId === content.session.instrumentVersionId) {
      data = {
        ...content,
        serverAnswers,
        ...selectItems(content.version, content.session.sectionCode),
        source: 'device',
        copiedAt: stored.savedAt,
      };
      error = null;
    }
  }

  const waitingForCopy = offline && copy.isPending;
  return {
    data,
    error: data ? null : error,
    /** No connection and no copy on this phone: there is nothing to show until it returns. */
    offlineWithoutCopy: !data && offline && !copy.isPending && !stored,
    isPending: !data && !error && (!offline || waitingForCopy),
    refetch: async () => {
      await Promise.all([...queries.filter((query) => query.error || query.fetchStatus === 'paused'), copy].map((query) => query.refetch()));
    },
  };
}
