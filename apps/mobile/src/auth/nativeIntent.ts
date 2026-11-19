/** The deep links the identity provider sends the browser back to. They are not screens. */
const AUTH_RETURN_PATHS = new Set(['auth/callback', 'auth/signed-out']);

const SCHEME_AND_SLASHES = /^[a-z][a-z0-9+.-]*:\/\//i;
/** Expo Go links: exp://<host>:<port>/--/<path>. */
const EXPO_GO_PATH = /^[^/]*\/--\/(.*)$/;

const normalise = (path: string) => path.replace(/^\/+/, '').replace(/^--\/+/, '').replace(/\/+$/, '');

/**
 * True for `azwah://auth/callback?…`, `exp://192.168.1.5:8081/--/auth/callback?…`, `/auth/signed-out`,
 * `--/auth/callback` and the like: with or without a scheme, a leading slash or Expo Go's `--/` prefix.
 */
export function isAuthReturnPath(path: string): boolean {
  const [withoutQuery = ''] = path.split(/[?#]/);
  const scheme = SCHEME_AND_SLASHES.exec(withoutQuery);
  if (!scheme) {
    return AUTH_RETURN_PATHS.has(normalise(withoutQuery));
  }
  // With a custom scheme the first segment is part of the path (azwah://auth/callback); Expo Go puts it after `--/`.
  const rest = withoutQuery.slice(scheme[0].length);
  const expoGoPath = EXPO_GO_PATH.exec(rest)?.[1];
  return AUTH_RETURN_PATHS.has(normalise(rest)) || (expoGoPath !== undefined && AUTH_RETURN_PATHS.has(normalise(expoGoPath)));
}

/**
 * Expo Router's `redirectSystemPath` (app/+native-intent.tsx). While the app runs, the browser module receives the
 * callback itself, so the router stays on the sign-in screen (null: no navigation) instead of showing "not found".
 * When the link starts the app (the system ended it while the browser was open), it opens the anchor route `/`, which
 * redirects by auth state. Every other link passes through unchanged.
 */
export function redirectSystemPath({ path, initial }: { path: string; initial: boolean }): string | null {
  try {
    if (!isAuthReturnPath(path)) {
      return path;
    }
    return initial ? '/' : null;
  } catch {
    return path;
  }
}
