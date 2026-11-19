import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { problem, stubBff } from '@/test/bff';
import { overviewBody, WAVE_H1, wavesBody } from '@/test/fixtures';
import { navigation } from '@/test/navigation';
import { renderWithProviders } from '@/test/render';

import { OverviewView } from './overview-view';

vi.mock('next/navigation', async () => (await import('@/test/navigation')).nextNavigation);

beforeEach(() => navigation.reset('/overview'));

describe('OverviewView', () => {
  it('shows a loading state, then the counts exactly as the platform returned them', async () => {
    stubBff({ 'admin/waves': wavesBody, 'admin/analytics/overview': overviewBody() });

    renderWithProviders(<OverviewView />);

    expect(screen.getAllByRole('status').some((element) => element.textContent?.includes('Loading'))).toBe(true);
    const flow = await screen.findByRole('region', { name: 'Assessments' });
    expect(within(flow).getByText('980')).toBeInTheDocument();
    expect(within(flow).getByText('702')).toBeInTheDocument();
    expect(within(flow).getByText('559')).toBeInTheDocument();
    expect(within(flow).getByText('121')).toBeInTheDocument();
    expect(within(flow).getByText('57.6%')).toBeInTheDocument();
    const enrolment = screen.getByRole('region', { name: 'Enrolment' });
    expect(within(enrolment).getByText('412')).toBeInTheDocument();
    expect(within(enrolment).getByText('1,534')).toBeInTheDocument();
  });

  it('describes the trend chart and lists its points in a table', async () => {
    stubBff({ 'admin/waves': wavesBody, 'admin/analytics/overview': overviewBody() });
    const user = userEvent.setup();

    renderWithProviders(<OverviewView />);

    const figure = await screen.findByRole('figure', { name: 'Completed assessments per month' });
    expect(within(figure).getByRole('img').getAttribute('aria-label')).toContain('16 Oct 2025');
    await user.click(within(figure).getByRole('button', { name: 'Show as table' }));
    const rows = within(within(figure).getByRole('table')).getAllByRole('row');
    expect(rows.map((row) => row.textContent)).toEqual(['PeriodCompleted', 'Aug 202640', 'Sept 202655', 'Oct 202621']);
  });

  it('hides suppressed and small demographic cells and says why', async () => {
    stubBff({ 'admin/waves': wavesBody, 'admin/analytics/overview': overviewBody() });

    renderWithProviders(<OverviewView />);

    const employment = await screen.findByRole('table', { name: 'Completion by employment status' });
    const retired = within(employment).getByRole('row', { name: /Retired/ });
    expect(within(retired).getAllByText('Hidden')).toHaveLength(2);
    const student = within(employment).getByRole('row', { name: /Student/ });
    expect(within(student).queryByText('4')).toBeNull();
    expect(
      screen.getByText(/^A count that covers fewer than 10 people is hidden to protect privacy\. .*the count covering the next fewest people is hidden too.*A hidden count can therefore be 10 or more\.$/),
    ).toBeInTheDocument();
    expect(screen.queryByText(/Fewer than 10/)).toBeNull();
  });

  it('names each number for what the platform counts', async () => {
    stubBff({ 'admin/waves': wavesBody, 'admin/analytics/overview': overviewBody() });

    renderWithProviders(<OverviewView />);

    const flow = await screen.findByRole('region', { name: 'Assessments' });
    // The total includes cancelled assignments; the tables leave them out, as the completion rate does.
    expect(within(flow).getByText('All assignments').nextElementSibling).toHaveTextContent('980');
    const versions = screen.getByRole('table', { name: 'Completion by questionnaire version' });
    expect(within(versions).getByRole('columnheader', { name: 'Assigned, not cancelled' })).toBeInTheDocument();
    const enrolment = screen.getByRole('region', { name: 'Enrolment' });
    expect(within(enrolment).getByText('Active family members').nextElementSibling).toHaveTextContent('1,534');
    // The platform leaves out the members of withdrawn households (their rows stay active on the roster).
    expect(within(enrolment).getByText(/on the roster of a household taking part\. Members of withdrawn households are not counted\./)).toBeInTheDocument();
    expect(screen.queryByText('Family members taking part')).toBeNull();
  });

  it('says once, under the wave filter, that cohorts do not exist', async () => {
    stubBff({ 'admin/waves': wavesBody, 'admin/analytics/overview': overviewBody() });

    renderWithProviders(<OverviewView />);

    expect(await screen.findByLabelText('Wave')).toHaveAccessibleDescription(/Study cohorts are not defined in the platform yet/);
  });

  it('lists each questionnaire version on its own row, never combined', async () => {
    stubBff({ 'admin/waves': wavesBody, 'admin/analytics/overview': overviewBody() });

    renderWithProviders(<OverviewView />);

    const table = await screen.findByRole('table', { name: 'Completion by questionnaire version' });
    expect(within(table).getAllByRole('row')).toHaveLength(3);
    expect(within(table).getByRole('row', { name: /family_voices.*module_a/ })).toBeInTheDocument();
  });

  it('keeps the wave filter in the URL and asks the platform again', async () => {
    const bff = stubBff({ 'admin/waves': wavesBody, 'admin/analytics/overview': overviewBody() });
    const user = userEvent.setup();
    renderWithProviders(<OverviewView />);
    await screen.findByRole('region', { name: 'Assessments' });

    await user.selectOptions(await screen.findByLabelText('Wave'), WAVE_H1.id);

    expect(navigation.replace).toHaveBeenLastCalledWith(`/overview?waveId=${WAVE_H1.id}`, { scroll: false });
    await waitFor(() => expect(bff.lastQuery('admin/analytics/overview')?.get('waveId')).toBe(WAVE_H1.id));
  });

  it('sends the date window and interval together', async () => {
    navigation.reset('/overview', 'from=2026-01-01&to=2026-06-30&interval=week');
    const bff = stubBff({ 'admin/waves': wavesBody, 'admin/analytics/overview': overviewBody({ interval: 'week' }) });

    renderWithProviders(<OverviewView />);

    await waitFor(() => expect(bff.lastQuery('admin/analytics/overview')?.toString()).toBe('from=2026-01-01&interval=week&to=2026-06-30'));
  });

  it('waits for a whole date in the supported years before applying it', async () => {
    stubBff({ 'admin/waves': wavesBody, 'admin/analytics/overview': overviewBody() });
    renderWithProviders(<OverviewView />);
    await screen.findByRole('region', { name: 'Assessments' });
    const from = screen.getByLabelText('From');

    fireEvent.change(from, { target: { value: '0002-01-01' } });
    fireEvent.change(from, { target: { value: '0202-01-01' } });
    expect(navigation.replace).not.toHaveBeenCalled();
    expect(screen.queryByRole('heading', { name: 'Check the filters' })).toBeNull();

    fireEvent.change(from, { target: { value: '2026-02-01' } });
    expect(navigation.replace).toHaveBeenLastCalledWith('/overview?from=2026-02-01', { scroll: false });
  });

  it('clears a date at once', async () => {
    navigation.reset('/overview', 'from=2026-02-01');
    stubBff({ 'admin/waves': wavesBody, 'admin/analytics/overview': overviewBody() });
    renderWithProviders(<OverviewView />);
    await screen.findByRole('region', { name: 'Assessments' });

    fireEvent.change(screen.getByLabelText('From'), { target: { value: '' } });

    expect(navigation.replace).toHaveBeenLastCalledWith('/overview', { scroll: false });
  });

  it('refuses a reversed date window before asking the platform', async () => {
    navigation.reset('/overview', 'from=2026-06-30&to=2026-01-01');
    const bff = stubBff({ 'admin/waves': wavesBody, 'admin/analytics/overview': overviewBody() });

    renderWithProviders(<OverviewView />);

    expect(await screen.findByText('The start date must not be after the end date.')).toBeInTheDocument();
    expect(bff.calls.some((call) => call.url.pathname === '/bff/admin/analytics/overview')).toBe(false);
  });

  it('shows an empty trend as an empty state', async () => {
    stubBff({ 'admin/waves': wavesBody, 'admin/analytics/overview': overviewBody({ completionTrend: [] }) });

    renderWithProviders(<OverviewView />);

    expect(await screen.findByRole('heading', { name: 'No completed assessments in this window' })).toBeInTheDocument();
  });

  it('shows permission denied when the platform refuses the read', async () => {
    stubBff({ 'admin/waves': wavesBody, 'admin/analytics/overview': problem(403) });

    renderWithProviders(<OverviewView />);

    expect(await screen.findByRole('heading', { name: 'You do not have access to this page' })).toBeInTheDocument();
  });

  it('shows a filter problem when the platform refuses the window', async () => {
    stubBff({ 'admin/waves': wavesBody, 'admin/analytics/overview': problem(400, undefined, { errors: { to: ['x'] } }) });

    renderWithProviders(<OverviewView />);

    expect(await screen.findByRole('heading', { name: 'Check the filters' })).toBeInTheDocument();
  });

  it('reads in Arabic, right to left, with Western digits', async () => {
    stubBff({ 'admin/waves': wavesBody, 'admin/analytics/overview': overviewBody() });

    renderWithProviders(<OverviewView />, { language: 'ar' });

    const flow = await screen.findByRole('region', { name: 'التقييمات' });
    expect(within(flow).getByText('980')).toBeInTheDocument();
  });
});
