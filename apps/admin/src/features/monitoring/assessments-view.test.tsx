import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { problem, stubBff } from '@/test/bff';
import { assessmentRow, paged, WAVE_H1, wavesBody } from '@/test/fixtures';
import { navigation } from '@/test/navigation';
import { renderWithProviders } from '@/test/render';

import { AssessmentsView } from './assessments-view';

vi.mock('next/navigation', async () => (await import('@/test/navigation')).nextNavigation);

beforeEach(() => navigation.reset('/monitoring/assessments'));

const rows = [
  assessmentRow(),
  assessmentRow({
    assignmentId: '0190f0e2-0000-7000-8000-000000000002',
    instrumentCode: 'family_voices',
    sectionCode: 'module_a',
    assignmentStatus: 'in_progress',
    sessionStatus: 'in_progress',
    completedAt: null,
    submittedAt: null,
  }),
  assessmentRow({ assignmentId: '0190f0e2-0000-7000-8000-000000000003', sessionStatus: 'scored', scored: true }),
];

describe('AssessmentsView', () => {
  it('lists assignments with their status, never an answer or a score', async () => {
    stubBff({ 'admin/waves': wavesBody, 'admin/assessments': paged(rows, 1, 20, 3) });

    renderWithProviders(<AssessmentsView />);

    const table = await screen.findByRole('table', { name: 'Assignments' });
    const body = within(table).getAllByRole('row').slice(1);
    expect(body).toHaveLength(3);
    expect(within(body[1]).getByText('In progress')).toBeInTheDocument();
    // Version and module sit under the questionnaire code, as on the household results.
    expect(within(body[0]).getByText('Version 1')).toBeInTheDocument();
    expect(within(body[1]).getByText('Version 1, module module_a')).toBeInTheDocument();
    expect(within(body[2]).getByText('Scored')).toBeInTheDocument();
    expect(within(body[2]).getByText('Yes')).toBeInTheDocument();
    expect(table.textContent).not.toMatch(/tier|total score/i);
  });

  it('tells apart households created in the same minute, whose version 7 IDs share their time prefix', async () => {
    stubBff({
      'admin/waves': wavesBody,
      'admin/assessments': paged([
        assessmentRow({ householdId: '0199f0e2-7b1c-7000-8000-00000000a001' }),
        assessmentRow({ assignmentId: '0190f0e2-0000-7000-8000-000000000009', householdId: '0199f0e2-7b1c-7000-8000-00000000b002' }),
      ]),
    });

    renderWithProviders(<AssessmentsView />);

    const table = await screen.findByRole('table', { name: 'Assignments' });
    const [first, second] = within(table).getAllByRole('row').slice(1);
    const shortId = (row: HTMLElement) => within(row).getByTitle(/^Household ID/).textContent;
    expect(shortId(first)).not.toBe(shortId(second));
    expect(shortId(first)).toBe('00000000a001');
  });

  it('asks the server for the next page', async () => {
    const bff = stubBff({ 'admin/waves': wavesBody, 'admin/assessments': paged(rows, 1, 20, 45) });
    const user = userEvent.setup();
    renderWithProviders(<AssessmentsView />);
    await screen.findByRole('table', { name: 'Assignments' });

    await user.click(screen.getByRole('button', { name: 'Next' }));

    expect(navigation.replace).toHaveBeenLastCalledWith('/monitoring/assessments?page=2', { scroll: false });
    await waitFor(() => expect(bff.lastQuery('admin/assessments')?.get('page')).toBe('2'));
  });

  it('goes back to the first page when a filter changes', async () => {
    navigation.reset('/monitoring/assessments', 'page=3');
    stubBff({ 'admin/waves': wavesBody, 'admin/assessments': paged(rows, 3, 20, 80) });
    const user = userEvent.setup();
    renderWithProviders(<AssessmentsView />);
    await screen.findByRole('table', { name: 'Assignments' });

    await user.selectOptions(screen.getByLabelText('Status'), 'completed');
    await user.click(screen.getByRole('button', { name: 'Apply filters' }));

    expect(navigation.replace).toHaveBeenLastCalledWith('/monitoring/assessments?status=completed', { scroll: false });
  });

  it('sends every filter together in one request', async () => {
    const bff = stubBff({ 'admin/waves': wavesBody, 'admin/assessments': paged(rows) });
    const user = userEvent.setup();
    renderWithProviders(<AssessmentsView />);
    await screen.findByRole('table', { name: 'Assignments' });

    await user.selectOptions(await screen.findByLabelText('Wave'), WAVE_H1.id);
    await user.type(screen.getByLabelText('Questionnaire code'), 'household_assessment');
    await user.type(screen.getByLabelText('Version'), '1');
    await user.selectOptions(screen.getByLabelText('Status'), 'expired');
    await user.type(screen.getByLabelText('Assigned from'), '2026-01-01');
    await user.type(screen.getByLabelText('Assigned to'), '2026-03-31');
    await user.click(screen.getByRole('button', { name: 'Apply filters' }));

    await waitFor(() =>
      expect(bff.lastQuery('admin/assessments')?.toString()).toBe(
        `assignedFrom=2026-01-01&assignedTo=2026-03-31&instrumentCode=household_assessment&page=1&pageSize=20&status=expired&versionNumber=1&waveId=${WAVE_H1.id}`,
      ),
    );
  });

  it('keeps the version filter off until a questionnaire code is given', async () => {
    stubBff({ 'admin/waves': wavesBody, 'admin/assessments': paged(rows) });
    const user = userEvent.setup();
    renderWithProviders(<AssessmentsView />);
    await screen.findByRole('table', { name: 'Assignments' });

    expect(screen.getByLabelText('Version')).toBeDisabled();
    await user.type(screen.getByLabelText('Questionnaire code'), 'family_voices');
    expect(screen.getByLabelText('Version')).toBeEnabled();
  });

  it('refuses a malformed questionnaire code before asking the server', async () => {
    const bff = stubBff({ 'admin/waves': wavesBody, 'admin/assessments': paged(rows) });
    const user = userEvent.setup();
    renderWithProviders(<AssessmentsView />);
    await screen.findByRole('table', { name: 'Assignments' });
    const before = bff.calls.length;

    await user.type(screen.getByLabelText('Questionnaire code'), 'Household Assessment');
    await user.click(screen.getByRole('button', { name: 'Apply filters' }));

    expect(screen.getByText('Use lowercase letters, digits and underscores, starting with a letter.')).toBeInTheDocument();
    expect(navigation.replace).not.toHaveBeenCalled();
    expect(bff.calls.length).toBe(before);
  });

  it('shows a filter problem when the server refuses the filters', async () => {
    navigation.reset('/monitoring/assessments', 'assignedFrom=2026-05-01&assignedTo=2026-01-01');
    stubBff({ 'admin/waves': wavesBody, 'admin/assessments': problem(400, undefined, { errors: { assignedFrom: ['x'] } }) });

    renderWithProviders(<AssessmentsView />);

    expect(await screen.findByRole('heading', { name: 'Check the filters' })).toBeInTheDocument();
  });

  it('goes to the last page when the address asks for a page past the end, instead of an empty table', async () => {
    navigation.reset('/monitoring/assessments', 'page=99');
    stubBff({
      'admin/waves': wavesBody,
      'admin/assessments': (url: URL) => (url.searchParams.get('page') === '99' ? paged([], 99, 20, 45) : paged(rows, 3, 20, 45)),
    });

    renderWithProviders(<AssessmentsView />);

    await waitFor(() => expect(navigation.replace).toHaveBeenLastCalledWith('/monitoring/assessments?page=3', { scroll: false }));
    expect(await screen.findByText('Page 3 of 3')).toBeInTheDocument();
    expect(screen.queryByText('Page 99 of 3')).toBeNull();
  });

  it('does not repeat the note about cohorts under its wave filter', async () => {
    stubBff({ 'admin/waves': wavesBody, 'admin/assessments': paged(rows) });

    renderWithProviders(<AssessmentsView />);

    await screen.findByRole('table', { name: 'Assignments' });
    expect(screen.getByLabelText('Wave')).toBeInTheDocument();
    expect(screen.queryByText(/Study cohorts are not defined/)).toBeNull();
  });

  it('shows an empty state when nothing matches', async () => {
    stubBff({ 'admin/waves': wavesBody, 'admin/assessments': paged([]) });

    renderWithProviders(<AssessmentsView />);

    expect(await screen.findByRole('heading', { name: 'No assignments match these filters' })).toBeInTheDocument();
  });

  it('shows permission denied when the server refuses the read', async () => {
    stubBff({ 'admin/waves': wavesBody, 'admin/assessments': problem(403) });

    renderWithProviders(<AssessmentsView />);

    expect(await screen.findByRole('heading', { name: 'You do not have access to this page' })).toBeInTheDocument();
  });

  it('clears every filter at once', async () => {
    navigation.reset('/monitoring/assessments', 'status=completed&page=2');
    stubBff({ 'admin/waves': wavesBody, 'admin/assessments': paged(rows) });
    const user = userEvent.setup();
    renderWithProviders(<AssessmentsView />);
    await screen.findByRole('table', { name: 'Assignments' });

    await user.click(screen.getByRole('button', { name: 'Clear filters' }));

    expect(navigation.replace).toHaveBeenLastCalledWith('/monitoring/assessments', { scroll: false });
  });
});
