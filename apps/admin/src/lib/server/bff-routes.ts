import 'server-only';

/**
 * Every request the browser may make through the BFF, and the query keys each may carry. Anything else answers 404.
 * `:id` matches a UUID only, so a path can never climb out of its route.
 */
export interface BffRoute {
  method: 'GET' | 'POST';
  pattern: readonly string[];
  query: readonly string[];
  /** POST that needs an `Idempotency-Key`, forwarded as is. */
  idempotent?: true;
  /** A file download: its `Content-Disposition` is kept. */
  stream?: true;
  /** Answered by the dashboard's server itself, not the API. */
  local?: 'health';
}

const PAGE = ['page', 'pageSize'] as const;

export const BFF_ROUTES: readonly BffRoute[] = [
  { method: 'GET', pattern: ['health'], query: [], local: 'health' },
  { method: 'GET', pattern: ['me', 'permissions'], query: [] },
  { method: 'GET', pattern: ['admin', 'analytics', 'overview'], query: ['waveId', 'from', 'to', 'interval'] },
  { method: 'GET', pattern: ['admin', 'analytics', 'score-distributions'], query: ['waveId'] },
  {
    method: 'GET',
    pattern: ['admin', 'assessments'],
    query: ['waveId', 'instrumentCode', 'versionNumber', 'status', 'assignedFrom', 'assignedTo', ...PAGE],
  },
  { method: 'GET', pattern: ['admin', 'participation'], query: ['status', 'rosterConfirmed', 'reference', ...PAGE] },
  { method: 'GET', pattern: ['admin', 'privacy-requests'], query: ['status', ...PAGE] },
  { method: 'GET', pattern: ['admin', 'results'], query: ['waveId', 'instrumentCode', 'reference', ...PAGE] },
  { method: 'POST', pattern: ['admin', 'privacy-requests', ':id', 'close'], query: [] },
  { method: 'GET', pattern: ['admin', 'safety-alerts'], query: ['status', 'category', ...PAGE] },
  { method: 'GET', pattern: ['admin', 'safety-alerts', 'summary'], query: [] },
  { method: 'POST', pattern: ['admin', 'safety-alerts', ':id', 'acknowledge'], query: [] },
  { method: 'POST', pattern: ['admin', 'safety-alerts', ':id', 'resolve'], query: [] },
  { method: 'GET', pattern: ['admin', 'waves'], query: PAGE },
  { method: 'GET', pattern: ['admin', 'exports'], query: PAGE },
  { method: 'POST', pattern: ['admin', 'exports'], query: [], idempotent: true },
  { method: 'GET', pattern: ['admin', 'exports', 'catalog'], query: [] },
  { method: 'GET', pattern: ['admin', 'exports', ':id'], query: [] },
  { method: 'GET', pattern: ['admin', 'exports', ':id', 'file'], query: [], stream: true },
  { method: 'GET', pattern: ['instruments'], query: PAGE },
  { method: 'GET', pattern: ['instruments', ':id'], query: [] },
  { method: 'GET', pattern: ['instruments', ':id', 'versions'], query: PAGE },
  { method: 'GET', pattern: ['instrument-versions', ':id'], query: [] },
  { method: 'GET', pattern: ['instrument-versions', ':id', 'rule-sets'], query: [] },
  { method: 'GET', pattern: ['rule-sets', ':id'], query: [] },
];

const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function matchBffRoute(method: string, segments: readonly string[]): { route: BffRoute; apiPath: string } | null {
  const route = BFF_ROUTES.find(
    (candidate) =>
      candidate.method === method &&
      candidate.pattern.length === segments.length &&
      candidate.pattern.every((part, index) => (part === ':id' ? UUID.test(segments[index]) : part === segments[index])),
  );
  if (!route) return null;
  return { route, apiPath: route.local ? `/${segments.join('/')}` : `/api/v1/${segments.join('/')}` };
}
