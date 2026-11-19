import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { Toaster } from '@/components/ui/sonner';
import { problem, stubBff } from '@/test/bff';
import { paged, safetyAlert } from '@/test/fixtures';
import { navigation } from '@/test/navigation';
import { renderWithProviders } from '@/test/render';

import { SafetyAlertsView } from './safety-alerts-view';

vi.mock('next/navigation', async () => (await import('@/test/navigation')).nextNavigation);

beforeEach(() => navigation.reset('/safety-alerts'));

const LIST = 'admin/safety-alerts';
const alert = safetyAlert();
const resolvePath = `${LIST}/${alert.id}/resolve`;

function view() {
  return renderWithProviders(
    <>
      <SafetyAlertsView canManage />
      <Toaster />
    </>,
  );
}

function resolves(bff: ReturnType<typeof stubBff>) {
  return bff.calls.filter((call) => call.url.pathname.endsWith('/resolve') && call.init.method === 'POST');
}

async function openDialog(user: ReturnType<typeof userEvent.setup>) {
  await user.click(await screen.findByRole('button', { name: 'Resolve alert: Suicide or self-harm, HH-7K2M-9QXA' }));
  return screen.findByRole('dialog', { name: 'Resolve this alert' });
}

describe('resolve alert dialog', () => {
  it('requires an outcome before it sends anything', async () => {
    const bff = stubBff({ [LIST]: paged([alert]) });
    const user = userEvent.setup();
    view();

    const dialog = await openDialog(user);
    await user.click(within(dialog).getByRole('button', { name: 'Resolve alert' }));

    expect(within(dialog).getByText('Choose an outcome.')).toBeInTheDocument();
    expect(resolves(bff)).toHaveLength(0);
  });

  it('offers every outcome and posts the chosen one', async () => {
    const bff = stubBff({
      [LIST]: paged([alert]),
      [resolvePath]: { ...alert, status: 'resolved', resolvedAt: '2026-10-15T12:00:00+00:00', resolution: 'referred_to_service' },
    });
    const user = userEvent.setup();
    view();

    const dialog = await openDialog(user);
    expect(within(dialog).getAllByRole('radio')).toHaveLength(6);
    await user.click(within(dialog).getByRole('radio', { name: /Referred to a service/ }));
    await user.click(within(dialog).getByRole('button', { name: 'Resolve alert' }));

    expect(await screen.findByText('Alert resolved.')).toBeInTheDocument();
    expect(JSON.parse(String(resolves(bff)[0].init.body))).toEqual({ resolution: 'referred_to_service' });
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
  });

  it('says an alert that is already resolved was already resolved, and keeps the dialog open', async () => {
    stubBff({ [LIST]: paged([alert]), [resolvePath]: problem(409, 'safeguarding.alert_resolved') });
    const user = userEvent.setup();
    view();

    const dialog = await openDialog(user);
    await user.click(within(dialog).getByRole('radio', { name: /False alarm/ }));
    await user.click(within(dialog).getByRole('button', { name: 'Resolve alert' }));

    expect(await within(dialog).findByText('This alert was already resolved.')).toBeInTheDocument();
    expect(within(dialog).queryByText(/Someone else changed/)).toBeNull();
    expect(within(dialog).getByRole('button', { name: 'Resolve alert' })).toBeDisabled();
  });

  it('says an alert that is already resolved was already resolved, in Arabic', async () => {
    stubBff({ [LIST]: paged([alert]), [resolvePath]: problem(409, 'safeguarding.alert_resolved') });
    const user = userEvent.setup();
    renderWithProviders(<SafetyAlertsView canManage />, { language: 'ar' });

    await user.click(await screen.findByRole('button', { name: /^معالجة التنبيه:/ }));
    const dialog = await screen.findByRole('dialog');
    await user.click(within(dialog).getAllByRole('radio')[4]);
    await user.click(within(dialog).getAllByRole('button').find((button) => button.textContent === 'معالجة التنبيه') as HTMLElement);

    expect(await within(dialog).findByText('تمت معالجة هذا التنبيه من قبل.')).toBeInTheDocument();
  });

  it('shows a conflict in its own words, never the server text, and keeps the dialog open', async () => {
    stubBff({
      [LIST]: paged([alert]),
      [resolvePath]: problem(409, 'safeguarding.alert_not_open', { title: 'SECRET SERVER TITLE', detail: 'SECRET SERVER DETAIL' }),
    });
    const user = userEvent.setup();
    view();

    const dialog = await openDialog(user);
    await user.click(within(dialog).getByRole('radio', { name: /False alarm/ }));
    await user.click(within(dialog).getByRole('button', { name: 'Resolve alert' }));

    expect(await within(dialog).findByText(/Someone else changed this alert at the same time/)).toBeInTheDocument();
    expect(screen.queryByText(/SECRET/)).toBeNull();
  });

  it('says when the alert no longer exists', async () => {
    stubBff({ [LIST]: paged([alert]), [resolvePath]: problem(404, 'safeguarding.alert_not_found') });
    const user = userEvent.setup();
    view();

    const dialog = await openDialog(user);
    await user.click(within(dialog).getByRole('radio', { name: /No action needed/ }));
    await user.click(within(dialog).getByRole('button', { name: 'Resolve alert' }));

    expect(await within(dialog).findByText(/no longer exists/)).toBeInTheDocument();
  });
});
