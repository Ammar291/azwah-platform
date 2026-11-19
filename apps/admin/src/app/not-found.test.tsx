import { screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import type { ConfigResult } from '@/lib/server/config';
import { oidcTestConfig, testConfig } from '@/test/config';
import { renderWithProviders } from '@/test/render';

const getAdminConfig = vi.fn<() => ConfigResult>();

vi.mock('@/lib/server/config', () => ({ getAdminConfig: () => getAdminConfig() }));
vi.mock('@/lib/i18n-server', () => ({ getRequestLanguage: async () => 'en' }));

beforeEach(() => {
  getAdminConfig.mockReset();
});

describe('not-found page', () => {
  it('says that development data is synthetic, like every other page', async () => {
    getAdminConfig.mockReturnValue({ ok: true, config: testConfig({ data: { source: 'mock', scenario: 'approved_scores' } }) });
    const { default: NotFound } = await import('./not-found');

    renderWithProviders(await NotFound());

    expect(screen.getByRole('heading', { level: 1, name: 'Page not found' })).toBeInTheDocument();
    expect(screen.getByRole('note')).toHaveTextContent('Development mock data: every number on these pages is synthetic.');
    expect(screen.getByRole('note')).toHaveTextContent('Development sign-in: not a real identity provider.');
  });

  it('shows no banner when nothing is simulated', async () => {
    getAdminConfig.mockReturnValue({ ok: true, config: oidcTestConfig() });
    const { default: NotFound } = await import('./not-found');

    renderWithProviders(await NotFound());

    expect(screen.getByRole('heading', { level: 1, name: 'Page not found' })).toBeInTheDocument();
    expect(screen.queryByRole('note')).toBeNull();
  });
});
