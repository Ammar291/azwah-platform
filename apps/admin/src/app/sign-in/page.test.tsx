import { screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { parseAdminConfig, type ConfigResult } from '@/lib/server/config';
import { renderWithProviders } from '@/test/render';

import SignInPage from './page';

const deployment = vi.hoisted(() => ({ result: null as ConfigResult | null }));

vi.mock('@/lib/server/config', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/lib/server/config')>()),
  getAdminConfig: () => deployment.result,
}));
vi.mock('@/app/actions', () => ({ setLanguage: vi.fn() }));

const production = {
  ADMIN_APP_ORIGIN: 'https://admin.example.org',
  ADMIN_SESSION_SECRET: Buffer.alloc(32, 7).toString('base64'),
  ADMIN_AUTH_MODE: 'oidc',
  OIDC_ISSUER: 'https://issuer.example.org/realms/azwah',
  OIDC_CLIENT_ID: 'azwah-admin',
  ADMIN_API_URL: 'https://api.example.org',
};

/** The sign-in page as a link to `/sign-in?error=cross_origin` opens it, on a dashboard configured with `env`. */
async function openCrossOriginRefusal(env: Record<string, string | undefined>) {
  deployment.result = parseAdminConfig(env);
  renderWithProviders(await SignInPage({ searchParams: Promise.resolve({ error: 'cross_origin' }) }));
  return screen.getByRole('alert');
}

afterEach(() => {
  deployment.result = null;
});

describe('the sign-in page after a cross-origin refusal', () => {
  it('in production says only that the request was refused, never which server setting to change', async () => {
    const alert = await openCrossOriginRefusal(production);

    expect(alert).toHaveTextContent(/refused this request.*Open the dashboard from its own address/);
    expect(alert).not.toHaveTextContent(/ADMIN_APP_ORIGIN|Development/);
  });

  it('names no setting either when the deployment is not configured', async () => {
    const alert = await openCrossOriginRefusal({});

    expect(alert).not.toHaveTextContent(/ADMIN_APP_ORIGIN/);
  });

  it('in development adds which setting must match the address the dashboard was opened at', async () => {
    const alert = await openCrossOriginRefusal({ ADMIN_ENVIRONMENT: 'development', ADMIN_DATA_SOURCE: 'mock' });

    expect(alert).toHaveTextContent(/refused this request.*Development: .*ADMIN_APP_ORIGIN/);
  });
});
