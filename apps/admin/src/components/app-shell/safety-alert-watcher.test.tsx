import { screen, waitFor } from '@testing-library/react';
import { toast } from 'sonner';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { Toaster } from '@/components/ui/sonner';
import { SafetyAlertsView } from '@/features/safety-alerts/safety-alerts-view';
import { paged, safetyAlert } from '@/test/fixtures';
import { problem, stubBff } from '@/test/bff';
import { renderWithProviders } from '@/test/render';

import { SafetyAlertWatcher } from './safety-alert-watcher';

vi.mock('next/navigation', async () => (await import('@/test/navigation')).nextNavigation);

const SUMMARY = 'admin/safety-alerts/summary';

function summary(latestDetectedAt: string | null) {
  return { open: 1, acknowledged: 0, openCritical: 1, latestDetectedAt };
}

function watch(language: 'en' | 'ar' = 'en') {
  return renderWithProviders(
    <>
      <SafetyAlertWatcher pollMs={30} />
      <Toaster />
    </>,
    { language },
  );
}

describe('SafetyAlertWatcher', () => {
  afterEach(() => {
    toast.dismiss();
  });

  it('does not toast for what is already there on the first load', async () => {
    const bff = stubBff({ [SUMMARY]: summary('2026-10-15T09:00:00+00:00') });
    watch();

    await waitFor(() => expect(bff.calls.length).toBeGreaterThanOrEqual(2));
    expect(screen.queryByText('New safety alert')).toBeNull();
  });

  it('toasts once, with a link to the page, when a later poll holds a newer alert', async () => {
    let reads = 0;
    stubBff({ [SUMMARY]: () => summary(reads++ < 2 ? '2026-10-15T09:00:00+00:00' : '2026-10-15T09:05:00+00:00') });
    watch();

    expect(await screen.findByText('New safety alert')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'View alerts' })).toHaveAttribute('href', '/safety-alerts');
    await new Promise((resolve) => setTimeout(resolve, 150));
    expect(screen.getAllByText('New safety alert')).toHaveLength(1);
  });

  it('toasts when the first alert arrives after a load that held none', async () => {
    let reads = 0;
    stubBff({ [SUMMARY]: () => summary(reads++ < 1 ? null : '2026-10-15T09:05:00+00:00') });
    watch();

    expect(await screen.findByText('New safety alert')).toBeInTheDocument();
  });

  it('does not toast when the marker moves back or stays', async () => {
    let reads = 0;
    const bff = stubBff({ [SUMMARY]: () => summary(reads++ < 1 ? '2026-10-15T09:00:00+00:00' : '2026-10-15T08:00:00+00:00') });
    watch();

    await waitFor(() => expect(bff.calls.length).toBeGreaterThanOrEqual(3));
    expect(screen.queryByText('New safety alert')).toBeNull();
  });

  it('toasts once for a newer value after one failed poll, and not on the failure', async () => {
    let reads = 0;
    const bff = stubBff({
      [SUMMARY]: () => {
        const read = reads++;
        if (read === 1) return problem(500);
        return summary(read === 0 ? '2026-10-15T09:00:00+00:00' : '2026-10-15T09:05:00+00:00');
      },
    });
    watch();

    expect(await screen.findByText('New safety alert')).toBeInTheDocument();
    await waitFor(() => expect(bff.calls.length).toBeGreaterThanOrEqual(4));
    expect(screen.getAllByText('New safety alert')).toHaveLength(1);
  });

  it('never toasts for a value that is not a time', async () => {
    let reads = 0;
    const bff = stubBff({ [SUMMARY]: () => summary(reads++ < 1 ? '2026-10-15T09:00:00+00:00' : 'not-a-time') });
    watch();

    await waitFor(() => expect(bff.calls.length).toBeGreaterThanOrEqual(3));
    expect(screen.queryByText('New safety alert')).toBeNull();
  });

  it('speaks Arabic when the dashboard does', async () => {
    let reads = 0;
    stubBff({ [SUMMARY]: () => summary(reads++ < 1 ? '2026-10-15T09:00:00+00:00' : '2026-10-15T09:05:00+00:00') });
    watch('ar');

    expect(await screen.findByText('تنبيه سلامة جديد')).toBeInTheDocument();
  });

  it('keeps a critical toast and says it is critical when openCritical goes up', async () => {
    let reads = 0;
    stubBff({
      [SUMMARY]: () =>
        reads++ < 1
          ? { open: 1, acknowledged: 0, openCritical: 1, latestDetectedAt: '2026-10-15T09:00:00+00:00' }
          : { open: 2, acknowledged: 0, openCritical: 2, latestDetectedAt: '2026-10-15T09:05:00+00:00' },
    });
    watch();

    expect(await screen.findByText('New critical safety alert')).toBeInTheDocument();
    expect(screen.queryByText('New safety alert')).toBeNull();
  });

  it('gives a new alert that is not critical an ordinary toast', async () => {
    let reads = 0;
    stubBff({
      [SUMMARY]: () => {
        const read = reads++;
        return { open: 1 + read, acknowledged: 0, openCritical: 1, latestDetectedAt: read < 1 ? '2026-10-15T09:00:00+00:00' : '2026-10-15T09:05:00+00:00' };
      },
    });
    watch();

    expect(await screen.findByText('New safety alert')).toBeInTheDocument();
    expect(screen.queryByText('New critical safety alert')).toBeNull();
  });

  it('tells the person, with a sign-in link, when a good read is followed by a 401, and only once', async () => {
    let reads = 0;
    const bff = stubBff({ [SUMMARY]: () => (reads++ < 1 ? summary('2026-10-15T09:00:00+00:00') : problem(401, 'bff.signed_out')) });
    watch();

    expect(await screen.findByText(/no longer being checked because your session has ended/)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Sign in' })).toHaveAttribute('href', '/auth/sign-in');
    await waitFor(() => expect(bff.calls.length).toBeGreaterThanOrEqual(4));
    expect(screen.getAllByText(/no longer being checked/)).toHaveLength(1);
  });

  it('shows no sign-in toast for a good read followed by a 500', async () => {
    let reads = 0;
    const bff = stubBff({ [SUMMARY]: () => (reads++ < 1 ? summary('2026-10-15T09:00:00+00:00') : problem(500)) });
    watch();

    await waitFor(() => expect(bff.calls.length).toBeGreaterThanOrEqual(3));
    expect(screen.queryByText(/no longer being checked/)).toBeNull();
  });

  it('reads the alert list again when a newer summary arrives, so the toast and the list agree', async () => {
    let summaryReads = 0;
    let listReads = 0;
    stubBff({
      [SUMMARY]: () => summary(summaryReads++ < 1 ? '2026-10-15T09:00:00+00:00' : '2026-10-15T09:05:00+00:00'),
      'admin/safety-alerts': () => {
        listReads += 1;
        return paged([safetyAlert()]);
      },
    });
    renderWithProviders(
      <>
        <SafetyAlertWatcher pollMs={30} />
        <SafetyAlertsView canManage />
      </>,
    );
    await screen.findByRole('table', { name: 'Safety alerts' });

    await waitFor(() => expect(listReads).toBeGreaterThanOrEqual(2));
  });

  it('prefixes the page title with the count while alerts need attention, and removes it on unmount', async () => {
    document.title = 'Safety alerts - Azwah';
    stubBff({ [SUMMARY]: { open: 2, acknowledged: 1, openCritical: 0, latestDetectedAt: null } });
    const { unmount } = watch();

    await waitFor(() => expect(document.title).toBe('(3) Safety alerts - Azwah'));
    unmount();
    expect(document.title).toBe('Safety alerts - Azwah');
  });

  it('leaves the page title alone when nothing needs attention', async () => {
    document.title = 'Safety alerts - Azwah';
    const bff = stubBff({ [SUMMARY]: { open: 0, acknowledged: 0, openCritical: 0, latestDetectedAt: null } });
    watch();

    await waitFor(() => expect(bff.calls.length).toBeGreaterThanOrEqual(1));
    expect(document.title).toBe('Safety alerts - Azwah');
  });
});
