import { SignInView, type SignInMode } from '@/features/auth/sign-in-view';
import { getAdminConfig } from '@/lib/server/config';
import { sanitizeReturnTo } from '@/lib/server/return-to';

type SearchParams = Promise<Record<string, string | string[] | undefined>>;

/** Sign-in: which way to sign in is decided on the server from the deployment's configuration. */
export default async function SignInPage({ searchParams }: { searchParams: SearchParams }) {
  const query = await searchParams;
  const one = (key: string) => (typeof query[key] === 'string' ? (query[key] as string) : null);
  const result = getAdminConfig();
  const mode: SignInMode = !result.ok
    ? { kind: 'not_configured', problems: result.problems }
    : result.config.auth.mode === 'oidc'
      ? { kind: 'oidc' }
      : { kind: 'development', mockData: result.config.data.source === 'mock' };
  // Configuration hints only in development: anyone can open this page with any error code. An unusable
  // configuration is not known to be development, so it gets none either.
  const development = result.ok && result.config.environment === 'development';
  return (
    <SignInView
      mode={mode}
      returnTo={sanitizeReturnTo(one('returnTo'))}
      error={one('error')}
      signedOut={one('signedOut') === '1'}
      development={development}
    />
  );
}
