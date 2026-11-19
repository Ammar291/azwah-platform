import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { problem, stubBff } from '@/test/bff';
import { paged, participationRow } from '@/test/fixtures';
import { navigation } from '@/test/navigation';
import { renderWithProviders } from '@/test/render';

import { ParticipationView } from './participation-view';

vi.mock('next/navigation', async () => (await import('@/test/navigation')).nextNavigation);

beforeEach(() => navigation.reset('/monitoring/participation'));

const rows = [
  participationRow(),
  participationRow({
    householdId: '0190f0e2-1111-7000-8000-000000000002',
    reference: 'HH-0000-0001',
    status: 'withdrawn',
    rosterConfirmedAt: null,
  }),
];

describe('ParticipationView', () => {
  it('lists households with their status, roster confirmation and progress', async () => {
    stubBff({ 'admin/participation': paged(rows) });

    renderWithProviders(<ParticipationView />);

    const table = await screen.findByRole('table', { name: 'Households' });
    const [first, second] = within(table).getAllByRole('row').slice(1);
    expect(within(first).getByText('HH-7K2M-9QXA')).toBeInTheDocument();
    expect(within(first).getByText('Taking part')).toBeInTheDocument();
    expect(within(first).getByText('2 completed, 0 in progress, 1 not started')).toBeInTheDocument();
    expect(within(second).getByText('Withdrawn')).toBeInTheDocument();
    expect(within(second).getByText('No')).toBeInTheDocument();
  });

  it('searches by the exact reference, trimmed and in capitals', async () => {
    const bff = stubBff({ 'admin/participation': paged(rows) });
    const user = userEvent.setup();
    renderWithProviders(<ParticipationView />);
    await screen.findByRole('table', { name: 'Households' });

    await user.type(screen.getByLabelText('Household reference'), '  hh-7k2m-9qxa ');
    await user.click(screen.getByRole('button', { name: 'Search' }));

    await waitFor(() => expect(bff.lastQuery('admin/participation')?.get('reference')).toBe('HH-7K2M-9QXA'));
  });

  it('keeps the household reference out of the page address, so it never lands in history or access logs', async () => {
    stubBff({ 'admin/participation': paged(rows) });
    const user = userEvent.setup();
    renderWithProviders(<ParticipationView />);
    await screen.findByRole('table', { name: 'Households' });

    await user.type(screen.getByLabelText('Household reference'), 'HH-7K2M-9QXA');
    await user.selectOptions(screen.getByLabelText('Household status'), 'active');
    await user.click(screen.getByRole('button', { name: 'Search' }));

    expect(navigation.replace).toHaveBeenLastCalledWith('/monitoring/participation?status=active', { scroll: false });
    for (const [url] of navigation.replace.mock.calls) expect(url).not.toContain('HH-');
  });

  it('ignores a reference written into the address by hand', async () => {
    navigation.reset('/monitoring/participation', 'reference=HH-7K2M-9QXA');
    const bff = stubBff({ 'admin/participation': paged(rows) });

    renderWithProviders(<ParticipationView />);

    await screen.findByRole('table', { name: 'Households' });
    expect(bff.lastQuery('admin/participation')?.get('reference')).toBeNull();
  });

  it('explains a malformed reference without asking the server', async () => {
    stubBff({ 'admin/participation': paged(rows) });
    const user = userEvent.setup();
    renderWithProviders(<ParticipationView />);
    await screen.findByRole('table', { name: 'Households' });

    await user.type(screen.getByLabelText('Household reference'), 'HH-12');
    await user.click(screen.getByRole('button', { name: 'Search' }));

    expect(screen.getByText('A household reference looks like HH-7K2M-9QXA.')).toBeInTheDocument();
    expect(navigation.replace).not.toHaveBeenCalled();
  });

  it('filters by household status and roster confirmation together', async () => {
    const bff = stubBff({ 'admin/participation': paged(rows) });
    const user = userEvent.setup();
    renderWithProviders(<ParticipationView />);
    await screen.findByRole('table', { name: 'Households' });

    await user.selectOptions(screen.getByLabelText('Household status'), 'withdrawn');
    await user.selectOptions(screen.getByLabelText('Roster'), 'false');
    await user.click(screen.getByRole('button', { name: 'Search' }));

    await waitFor(() => expect(bff.lastQuery('admin/participation')?.toString()).toBe('page=1&pageSize=20&rosterConfirmed=false&status=withdrawn'));
  });

  it('pages on the server', async () => {
    const bff = stubBff({ 'admin/participation': paged(rows, 1, 20, 41) });
    const user = userEvent.setup();
    renderWithProviders(<ParticipationView />);
    await screen.findByRole('table', { name: 'Households' });

    await user.click(screen.getByRole('button', { name: 'Next' }));

    await waitFor(() => expect(bff.lastQuery('admin/participation')?.get('page')).toBe('2'));
  });

  it('goes to the last page when the address asks for a page past the end', async () => {
    navigation.reset('/monitoring/participation', 'page=99');
    stubBff({ 'admin/participation': (url: URL) => (url.searchParams.get('page') === '99' ? paged([], 99, 20, 41) : paged(rows, 3, 20, 41)) });

    renderWithProviders(<ParticipationView />);

    await waitFor(() => expect(navigation.replace).toHaveBeenLastCalledWith('/monitoring/participation?page=3', { scroll: false }));
    expect(await screen.findByText('Page 3 of 3')).toBeInTheDocument();
  });

  it('shows_filter_error_on_400', async () => {
    navigation.reset('/monitoring/participation', 'status=active');
    stubBff({ 'admin/participation': problem(400, undefined, { errors: { status: ['x'] } }) });

    renderWithProviders(<ParticipationView />);

    expect(await screen.findByRole('heading', { name: 'Check the filters' })).toBeInTheDocument();
  });

  it('shows an empty state when no household matches', async () => {
    stubBff({ 'admin/participation': paged([]) });

    renderWithProviders(<ParticipationView />);

    expect(await screen.findByRole('heading', { name: 'No households match these filters' })).toBeInTheDocument();
  });
});
