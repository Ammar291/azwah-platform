import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { Toaster } from '@/components/ui/sonner';
import type { Language } from '@/lib/i18n';
import { problem, stubBff } from '@/test/bff';
import { paged, privacyRequest } from '@/test/fixtures';
import { navigation } from '@/test/navigation';
import { renderWithProviders } from '@/test/render';

import { PrivacyRequestsView } from './privacy-requests-view';

vi.mock('next/navigation', async () => (await import('@/test/navigation')).nextNavigation);

beforeEach(() => navigation.reset('/privacy-requests'));

const LIST = 'admin/privacy-requests';
const open = privacyRequest();
const erasure = privacyRequest({
  id: '0190f0e2-5555-7000-8000-000000000002',
  householdReference: 'HH-0000-0001',
  memberId: '0190f0e2-6666-7000-8000-00000000c3d4',
  type: 'erasure',
  createdAt: '2026-10-05T14:00:00+00:00',
});
const closed = privacyRequest({
  id: '0190f0e2-5555-7000-8000-000000000003',
  type: 'correction',
  status: 'closed',
  closedAt: '2026-10-12T11:00:00+00:00',
  resolution: 'withdrawn',
});
const closePath = (id: string) => `${LIST}/${id}/close`;

function view(language: Language = 'en') {
  return renderWithProviders(
    <>
      <PrivacyRequestsView />
      <Toaster />
    </>,
    { language },
  );
}

function listReads(bff: ReturnType<typeof stubBff>) {
  return bff.calls.filter((call) => call.url.pathname === `/bff/${LIST}`).length;
}

function closes(bff: ReturnType<typeof stubBff>) {
  return bff.calls.filter((call) => call.url.pathname.endsWith('/close'));
}

async function openDialog(user: ReturnType<typeof userEvent.setup>, reference = 'HH-7K2M-9QXA') {
  await user.click(await screen.findByRole('button', { name: `Close request: Access to data, ${reference}` }));
  return screen.findByRole('dialog', { name: 'Close this request' });
}

async function closeAs(user: ReturnType<typeof userEvent.setup>, outcome: RegExp) {
  const dialog = await openDialog(user);
  await user.click(within(dialog).getByRole('radio', { name: outcome }));
  await user.click(within(dialog).getByRole('checkbox', { name: /I have handled this request/ }));
  await user.click(within(dialog).getByRole('button', { name: 'Close request' }));
  return dialog;
}

describe('PrivacyRequestsView', () => {
  it('lists open requests with household, member, type, status and dates', async () => {
    stubBff({ [LIST]: paged([open, erasure]) });

    view();

    const table = await screen.findByRole('table', { name: 'Privacy requests' });
    const headers = within(table).getAllByRole('columnheader').map((cell) => cell.textContent);
    expect(headers).toEqual(['Household', 'Member', 'Request', 'Status', 'Received', 'Closed', 'Outcome', 'Action']);
    const [first, second] = within(table).getAllByRole('row').slice(1);
    expect(within(first).getByText('HH-7K2M-9QXA')).toBeInTheDocument();
    expect(within(first).getByText('00000000a1b2')).toHaveAttribute('title', `Member ID ${open.memberId}`);
    expect(within(first).getByText('Access to data')).toBeInTheDocument();
    expect(within(first).getByText('Open')).toBeInTheDocument();
    expect(within(first).getByText('2 Oct 2026')).toBeInTheDocument();
    expect(within(second).getByText('Erasure of data')).toBeInTheDocument();
    expect(within(second).getByRole('button', { name: 'Close request: Erasure of data, HH-0000-0001' })).toHaveTextContent('Close');
  });

  it('shows closed requests with their closing date and outcome, and no close action', async () => {
    navigation.reset('/privacy-requests', 'status=closed');
    stubBff({ [LIST]: paged([closed]) });

    view();

    const table = await screen.findByRole('table', { name: 'Privacy requests' });
    const [row] = within(table).getAllByRole('row').slice(1);
    expect(within(row).getByText('Closed')).toBeInTheDocument();
    expect(within(row).getByText('12 Oct 2026')).toBeInTheDocument();
    expect(within(row).getByText('Withdrawn by the family')).toBeInTheDocument();
    expect(within(table).queryByRole('button')).toBeNull();
  });

  it('shows the table in Arabic, with Western digits in UTC dates', async () => {
    stubBff({ [LIST]: paged([open]) });

    view('ar');

    const table = await screen.findByRole('table', { name: 'طلبات الخصوصية' });
    expect(within(table).getByRole('columnheader', { name: 'الأسرة' })).toBeInTheDocument();
    const [row] = within(table).getAllByRole('row').slice(1);
    expect(within(row).getByText('الاطلاع على البيانات')).toBeInTheDocument();
    expect(within(row).getByText('مفتوح')).toBeInTheDocument();
    const received = within(row).getAllByRole('cell')[4];
    expect(received.textContent).toMatch(/2026/);
    expect(received.textContent).not.toMatch(/[٠-٩۰-۹]/);
    expect(within(row).getByRole('button', { name: /^إغلاق الطلب: / })).toBeInTheDocument();
  });

  it('asks for open requests by default and keeps the closed filter in the address', async () => {
    const bff = stubBff({ [LIST]: paged([open]) });
    const user = userEvent.setup();
    view();
    await screen.findByRole('table', { name: 'Privacy requests' });

    expect(bff.lastQuery(LIST)?.toString()).toBe('page=1&pageSize=20&status=open');
    expect(screen.getByRole('button', { name: 'Open' })).toHaveAttribute('aria-pressed', 'true');

    await user.click(screen.getByRole('button', { name: 'Closed' }));

    expect(navigation.replace).toHaveBeenLastCalledWith('/privacy-requests?status=closed', { scroll: false });
    await waitFor(() => expect(bff.lastQuery(LIST)?.get('status')).toBe('closed'));
    expect(screen.getByRole('button', { name: 'Closed' })).toHaveAttribute('aria-pressed', 'true');

    await user.click(screen.getByRole('button', { name: 'Open' }));

    expect(navigation.replace).toHaveBeenLastCalledWith('/privacy-requests', { scroll: false });
  });

  it('reads the filter and page from the address, and ignores values it does not know', async () => {
    navigation.reset('/privacy-requests', 'status=closed&page=2&householdId=x');
    const bff = stubBff({ [LIST]: paged([closed], 2, 20, 21) });
    const { unmount } = view();
    await screen.findByRole('table', { name: 'Privacy requests' });
    expect(bff.lastQuery(LIST)?.toString()).toBe('page=2&pageSize=20&status=closed');
    unmount();

    navigation.reset('/privacy-requests', 'status=everything&page=-1');
    view();
    await screen.findByRole('table', { name: 'Privacy requests' });
    expect(bff.lastQuery(LIST)?.toString()).toBe('page=1&pageSize=20&status=open');
  });

  it('pages on the server, and starts again at the first page for another status', async () => {
    const bff = stubBff({ [LIST]: (url: URL) => paged([open], Number(url.searchParams.get('page')), 20, 41) });
    const user = userEvent.setup();
    view();
    await screen.findByRole('table', { name: 'Privacy requests' });

    await user.click(screen.getByRole('button', { name: 'Next' }));

    expect(navigation.replace).toHaveBeenLastCalledWith('/privacy-requests?page=2', { scroll: false });
    await waitFor(() => expect(bff.lastQuery(LIST)?.get('page')).toBe('2'));

    await user.click(screen.getByRole('button', { name: 'Closed' }));

    expect(navigation.replace).toHaveBeenLastCalledWith('/privacy-requests?status=closed', { scroll: false });
  });

  it('moves to the new last page when the page it shows has become empty', async () => {
    navigation.reset('/privacy-requests', 'page=3');
    stubBff({ [LIST]: (url: URL) => (url.searchParams.get('page') === '3' ? paged([], 3, 20, 40) : paged([open], 2, 20, 40)) });

    view();

    await waitFor(() => expect(navigation.replace).toHaveBeenLastCalledWith('/privacy-requests?page=2', { scroll: false }));
    expect(await screen.findByRole('table', { name: 'Privacy requests' })).toBeInTheDocument();
  });

  it('closes a request with the chosen outcome once, then reads the list again', async () => {
    let remaining = [open, erasure];
    const bff = stubBff({
      [LIST]: () => paged(remaining),
      [closePath(open.id)]: () => {
        remaining = [erasure];
        return { ...open, status: 'closed', closedAt: '2026-10-17T09:00:00+00:00', resolution: 'declined' };
      },
    });
    const user = userEvent.setup();
    view();

    const dialog = await openDialog(user);
    expect(within(dialog).getByText('HH-7K2M-9QXA')).toBeInTheDocument();
    expect(within(dialog).getByText('Access to data')).toBeInTheDocument();
    const before = listReads(bff);
    await user.click(within(dialog).getByRole('radio', { name: /Declined/ }));
    await user.click(within(dialog).getByRole('checkbox', { name: /I have handled this request/ }));
    await user.click(within(dialog).getByRole('button', { name: 'Close request' }));

    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    expect(await screen.findByText('Request closed.')).toBeInTheDocument();
    const posts = closes(bff);
    expect(posts).toHaveLength(1);
    expect(posts[0].url.pathname).toBe(`/bff/${closePath(open.id)}`);
    expect(posts[0].init.method).toBe('POST');
    expect(JSON.parse(String(posts[0].init.body))).toEqual({ resolution: 'declined' });
    await waitFor(() => expect(listReads(bff)).toBeGreaterThan(before));
    await waitFor(() => expect(screen.queryByText('HH-7K2M-9QXA')).toBeNull());
  });

  it('sends nothing until an outcome is chosen and the closing is confirmed, or when cancelled', async () => {
    const bff = stubBff({ [LIST]: paged([open]) });
    const user = userEvent.setup();
    view();

    const dialog = await openDialog(user);
    await user.click(within(dialog).getByRole('button', { name: 'Close request' }));

    expect(within(dialog).getByText('Choose an outcome.')).toHaveAttribute('role', 'alert');
    expect(within(dialog).getByText('Confirm that the request has been handled.')).toHaveAttribute('role', 'alert');
    expect(within(dialog).getByRole('radio', { name: /Completed/ })).toHaveFocus();

    await user.click(within(dialog).getByRole('radio', { name: /Completed/ }));
    await user.click(within(dialog).getByRole('button', { name: 'Close request' }));

    expect(within(dialog).queryByText('Choose an outcome.')).toBeNull();
    expect(within(dialog).getByText('Confirm that the request has been handled.')).toBeInTheDocument();
    expect(within(dialog).getByRole('checkbox')).toHaveFocus();

    await user.click(within(dialog).getByRole('button', { name: 'Cancel' }));

    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    expect(closes(bff)).toHaveLength(0);
  });

  it('explains that the request was already closed, without the server text, and reads the list again', async () => {
    const bff = stubBff({
      [LIST]: paged([open]),
      [closePath(open.id)]: problem(409, 'privacy.request_already_closed', { title: 'Server title', detail: 'Server detail' }),
    });
    const user = userEvent.setup();
    view();
    await screen.findByRole('table', { name: 'Privacy requests' });
    const before = listReads(bff);

    const dialog = await closeAs(user, /Completed/);

    expect(await within(dialog).findByText('This request has already been closed. The list has been read again.')).toBeInTheDocument();
    expect(screen.queryByText(/Server title|Server detail/)).toBeNull();
    await waitFor(() => expect(listReads(bff)).toBeGreaterThan(before));
    expect(within(dialog).getByRole('button', { name: 'Close request' })).toBeDisabled();
  });

  it('explains a request that no longer exists', async () => {
    stubBff({ [LIST]: paged([open]), [closePath(open.id)]: problem(404, 'privacy.request_not_found') });
    const user = userEvent.setup();
    view();

    const dialog = await closeAs(user, /Completed/);

    expect(await within(dialog).findByText(/This request no longer exists, or you cannot see it/)).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: 'Close request' })).toBeDisabled();
  });

  it('names the setting when the dashboard refuses the closing because it was opened at another address', async () => {
    stubBff({ [LIST]: paged([open]), [closePath(open.id)]: problem(403, 'bff.cross_origin') });
    const user = userEvent.setup();
    view();

    const dialog = await closeAs(user, /Completed/);

    expect(await within(dialog).findByText(/opened at a different address from the one it is set up for.*ADMIN_APP_ORIGIN/)).toBeInTheDocument();
    // Not the platform's role refusal, which a 403 otherwise means.
    expect(within(dialog).queryByText('Your role cannot close privacy requests.')).toBeNull();
  });

  it('explains the rate limit and keeps the dialog open to try again', async () => {
    const bff = stubBff({ [LIST]: paged([open]), [closePath(open.id)]: problem(429, 'requests.rate_limited', { title: 'Too Many Requests' }) });
    const user = userEvent.setup();
    view();

    const dialog = await closeAs(user, /Withdrawn/);

    expect(await within(dialog).findByText('Too many changes in a short time. Wait a minute, then try again.')).toBeInTheDocument();
    expect(screen.queryByText('Too Many Requests')).toBeNull();
    expect(screen.getByRole('dialog', { name: 'Close this request' })).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: 'Close request' })).toBeEnabled();
    expect(JSON.parse(String(closes(bff)[0].init.body))).toEqual({ resolution: 'withdrawn' });
  });

  it('shows an empty state for each status', async () => {
    stubBff({ [LIST]: paged([]) });
    const user = userEvent.setup();
    view();

    expect(await screen.findByRole('heading', { name: 'No open requests' })).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Closed' }));

    expect(await screen.findByRole('heading', { name: 'No closed requests yet' })).toBeInTheDocument();
  });

  it('shows the platform refusal as a translated state', async () => {
    stubBff({ [LIST]: problem(403, undefined, { title: 'Forbidden' }) });

    view();

    expect(await screen.findByRole('heading', { name: 'You do not have access to this page' })).toBeInTheDocument();
    expect(screen.queryByText('Forbidden')).toBeNull();
  });
});
