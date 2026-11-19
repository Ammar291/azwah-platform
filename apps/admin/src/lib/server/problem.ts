import 'server-only';

/** An RFC 9457 problem with the dashboard's own stable code; never internal detail. */
export function problem(status: number, code: string, headers?: HeadersInit): Response {
  const response = Response.json({ status, code }, { status, headers });
  response.headers.set('Content-Type', 'application/problem+json');
  response.headers.set('Cache-Control', 'no-store');
  return response;
}

/** Whether a state-changing request came from this dashboard's own pages. */
export function isSameOrigin(request: Request, appOrigin: string): boolean {
  return request.headers.get('origin') === appOrigin;
}
