import { act, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import { buildNavigation } from '@/lib/permissions';
import { problem, stubBff } from '@/test/bff';
import { renderWithProviders } from '@/test/render';

import { AppShell } from './app-shell';

vi.mock('next/navigation', () => ({ usePathname: () => '/overview' }));

const researcher = ['analytics.read', 'analytics.scores.read', 'exports.manage', 'questionnaires.read'];
const programAdministrator = [
  'analytics.read',
  'assessments.monitor',
  'participation.read',
  'privacy.requests.manage',
  'questionnaires.read',
];
const systemAdministrator = ['content.manage', 'questionnaires.read', 'rules.manage'];

function shell(permissions: string[], overrides: Partial<Parameters<typeof AppShell>[0]> = {}, language: 'en' | 'ar' = 'en') {
  return renderWithProviders(
    <AppShell
      navigation={buildNavigation(permissions)}
      persona="researcher"
      authMode="development"
      dataSource="mock"
      {...overrides}
    >
      <p>Page content</p>
    </AppShell>,
    { language },
  );
}

function sidebarLinks() {
  const nav = screen.getByRole('navigation', { name: 'Sections' });
  return within(nav)
    .getAllByRole('link')
    .map((link) => link.textContent);
}

describe('AppShell navigation', () => {
  it('shows researchers their research sections', () => {
    shell(researcher);

    expect(sidebarLinks()).toEqual(['Overview', 'Resilience analytics', 'Research exports', 'System status']);
  });

  it('shows program administrators monitoring and privacy requests, and never exports or scores', () => {
    shell(programAdministrator, { persona: 'program_administrator' });

    expect(sidebarLinks()).toEqual(['Overview', 'Assessment monitoring', 'Participation', 'Privacy requests', 'System status']);
  });

  it('shows system administrators the read-only configuration', () => {
    shell(systemAdministrator, { persona: 'system_administrator' });

    expect(sidebarLinks()).toEqual(['Questionnaires and rules', 'System status']);
  });

  it('shows household results only with results.read', () => {
    shell([...systemAdministrator, 'results.read'], { persona: 'system_administrator' });

    expect(sidebarLinks()).toEqual(['Questionnaires and rules', 'Household results', 'System status']);
    expect(screen.getByRole('link', { name: 'Household results' })).toHaveAttribute('href', '/results');
  });

  it('never shows household results to researchers or program administrators', () => {
    shell([...researcher, ...programAdministrator]);

    expect(screen.queryByRole('link', { name: 'Household results' })).not.toBeInTheDocument();
  });

  it('marks the current page', () => {
    shell(researcher);

    expect(screen.getByRole('link', { name: 'Overview' })).toHaveAttribute('aria-current', 'page');
    expect(screen.getByRole('link', { name: 'Research exports' })).not.toHaveAttribute('aria-current');
  });

  it('names the role it is signed in as and offers sign-out as a form post', () => {
    shell(researcher);

    expect(screen.getByText('Signed in as Researcher')).toBeInTheDocument();
    const signOut = screen.getByRole('button', { name: 'Sign out' });
    expect(signOut.closest('form')).toHaveAttribute('action', '/auth/sign-out');
    expect(signOut.closest('form')).toHaveAttribute('method', 'post');
  });

  it('labels development sign-in and mock data on every page', () => {
    shell(researcher);

    expect(screen.getByText('Development sign-in: not a real identity provider.')).toBeInTheDocument();
    expect(screen.getByText('Development mock data: every number on these pages is synthetic.')).toBeInTheDocument();
  });

  it('shows no development banner for OIDC sign-in with the API', () => {
    shell(researcher, { authMode: 'oidc', dataSource: 'api', persona: null });

    expect(screen.queryByText(/Development/)).toBeNull();
    expect(screen.getByText('Signed in')).toBeInTheDocument();
  });

  it('offers a skip link to the main content', () => {
    shell(researcher);

    expect(screen.getByRole('link', { name: 'Skip to main content' })).toHaveAttribute('href', '#main');
    expect(screen.getByRole('main')).toHaveAttribute('id', 'main');
  });
});

describe('AppShell on small screens', () => {
  it('opens the navigation in a sheet with the same sections and closes it on navigation', async () => {
    const user = userEvent.setup();
    shell(researcher);

    await user.click(screen.getByRole('button', { name: 'Open navigation' }));

    const dialog = await screen.findByRole('dialog');
    const links = within(dialog).getAllByRole('link').map((link) => link.textContent);
    expect(links).toEqual(['Overview', 'Resilience analytics', 'Research exports', 'System status']);

    await user.click(within(dialog).getByRole('link', { name: 'Research exports' }));

    await vi.waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
  });

  it('opens the sheet from the start edge in both directions, flipping the side once (in the sheet) and not again', async () => {
    for (const language of ['en', 'ar'] as const) {
      const user = userEvent.setup();
      const { unmount } = shell(researcher, {}, language);
      await user.click(screen.getByRole('button', { name: language === 'ar' ? 'فتح قائمة التنقل' : 'Open navigation' }));

      const dialog = await screen.findByRole('dialog');
      expect(dialog).toHaveAttribute('data-side', 'left');
      expect(dialog.className).toContain('data-[side=left]:start-0');
      expect(dialog.className).not.toContain('data-[side=left]:left-0');
      unmount();
    }
  });

  it('hides the desktop sidebar below the medium breakpoint and the menu button above it', () => {
    shell(researcher);

    expect(screen.getByRole('navigation', { name: 'Sections' }).closest('aside')).toHaveClass('hidden', 'md:flex');
    expect(screen.getByRole('button', { name: 'Open navigation' })).toHaveClass('md:hidden');
  });
});

describe('AppShell safety alerts', () => {
  const summary = (overrides: object = {}) => ({ open: 2, acknowledged: 1, openCritical: 0, latestDetectedAt: null, ...overrides });

  it('shows the count of alerts that need attention on the link, with an accessible label', async () => {
    stubBff({ 'admin/safety-alerts/summary': summary() });
    shell([...systemAdministrator, 'safety_alerts.read'], { persona: 'system_administrator' });

    const link = screen.getByRole('link', { name: /Safety alerts/ });
    expect(link).toHaveAttribute('href', '/safety-alerts');
    expect(await within(link).findByText('3 safety alerts need attention')).toBeInTheDocument();
    expect(within(link).getByText('3')).toHaveAttribute('aria-hidden', 'true');
  });

  it('turns the badge red only when an open alert is critical', async () => {
    stubBff({ 'admin/safety-alerts/summary': summary({ openCritical: 1 }) });
    shell([...systemAdministrator, 'safety_alerts.read'], { persona: 'system_administrator' });

    const count = await screen.findByText('3');
    expect(count.parentElement).toHaveClass('bg-destructive');
  });

  it('shows no badge when nothing needs attention', async () => {
    const bff = stubBff({ 'admin/safety-alerts/summary': summary({ open: 0, acknowledged: 0 }) });
    shell([...systemAdministrator, 'safety_alerts.read'], { persona: 'system_administrator' });

    await vi.waitFor(() => expect(bff.calls).toHaveLength(1));
    expect(screen.queryByText(/safety alerts? needs? attention/)).toBeNull();
  });

  it('asks for nothing and shows no link to a caller without the permission', () => {
    const bff = stubBff({});
    shell(researcher);

    expect(screen.queryByRole('link', { name: /Safety alerts/ })).toBeNull();
    expect(bff.calls).toHaveLength(0);
  });

  it('does not turn the badge red when no open alert is critical', async () => {
    stubBff({ 'admin/safety-alerts/summary': summary({ openCritical: 0 }) });
    shell([...systemAdministrator, 'safety_alerts.read'], { persona: 'system_administrator' });

    const count = await screen.findByText('3');
    expect(count.parentElement).not.toHaveClass('bg-destructive');
  });

  it('uses the singular form for one alert', async () => {
    stubBff({ 'admin/safety-alerts/summary': summary({ open: 1, acknowledged: 0 }) });
    shell([...systemAdministrator, 'safety_alerts.read'], { persona: 'system_administrator' });

    expect(await screen.findByText('1 safety alert needs attention')).toBeInTheDocument();
  });

  it.each([
    ['a 500', () => problem(500)],
    ['a 401', () => problem(401, 'bff.signed_out')],
  ])('shows a neutral "!" with an explanation after a good read is followed by %s', async (_name, failure) => {
    let reads = 0;
    stubBff({ 'admin/safety-alerts/summary': () => (reads++ < 1 ? summary({ openCritical: 1 }) : failure()) });
    const { client } = shell([...systemAdministrator, 'safety_alerts.read'], { persona: 'system_administrator' });
    const count = await screen.findByText('3');
    expect(count.parentElement).toHaveClass('bg-destructive');

    await act(() => client.invalidateQueries({ queryKey: ['safety-alerts', 'summary'] }));

    const stale = await screen.findByText('Safety alerts could not be checked');
    expect(stale.parentElement).not.toHaveClass('bg-destructive');
    expect(within(stale.parentElement as HTMLElement).getByText('!')).toBeInTheDocument();
    expect(screen.queryByText('3')).toBeNull();
  });
});
