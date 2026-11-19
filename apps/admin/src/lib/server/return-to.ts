import 'server-only';

const BASE = 'http://return-to.invalid';

/** A same-site page path to return to after sign-in, or `/`. Never an absolute URL, an auth route or the BFF. */
export function sanitizeReturnTo(value: unknown): string {
  if (typeof value !== 'string' || !value.startsWith('/')) return '/';
  let url: URL;
  try {
    url = new URL(value, BASE);
  } catch {
    return '/';
  }
  if (url.origin !== BASE) return '/';
  if (/^\/(auth|bff)(\/|$)/.test(url.pathname) || url.pathname === '/sign-in') return '/';
  return `${url.pathname}${url.search}`;
}
