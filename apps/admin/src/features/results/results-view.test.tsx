import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { problem, stubBff } from '@/test/bff';
import { householdResult, paged, WAVE_H1, wavesBody } from '@/test/fixtures';
import { navigation } from '@/test/navigation';
import { renderWithProviders } from '@/test/render';

import { ResultsView } from './results-view';

vi.mock('next/navigation', async () => (await import('@/test/navigation')).nextNavigation);

beforeEach(() => navigation.reset('/results'));

const rows = [
  householdResult({ tierCode: 'vulnerable', totalScore: 58 }),
  householdResult({ sessionId: '0190f0e2-7777-7000-8000-000000000002', tierCode: 'transitional', totalScore: 112 }),
  householdResult({ sessionId: '0190f0e2-7777-7000-8000-000000000003', tierCode: 'flourishing', totalScore: 1234.5 }),
  householdResult({
    sessionId: '0190f0e2-7777-7000-8000-000000000004',
    instrumentCode: 'family_voices',
    sectionCode: 'module_a',
    tierCode: 'tier_unnamed',
    totalScore: 2.75,
  }),
  householdResult({
    sessionId: '0190f0e2-7777-7000-8000-000000000005',
    householdReference: 'HH-0000-0001',
    state: 'incomplete',
    totalScore: null,
    tierCode: null,
  }),
];

async function bodyRows(name = 'Household results') {
  const table = await screen.findByRole('table', { name });
  return within(table).getAllByRole('row').slice(1);
}

const cells = (row: HTMLElement) => within(row).getAllByRole('cell').map((cell) => cell.textContent);

describe('ResultsView', () => {
  it('shows each recorded result with its band named as the specification names it', async () => {
    stubBff({ 'admin/waves': wavesBody, 'admin/results': paged(rows) });

    renderWithProviders(<ResultsView />);

    const [vulnerable, transitional, flourishing] = await bodyRows();
    expect(cells(vulnerable)).toEqual(['HH-7K2M-9QXA', 'wave_2026_h2', 'household_assessmentVersion 1', 'Complete', '58', 'Vulnerable', '20 Oct 2026']);
    expect(cells(transitional)[5]).toBe('Transitional');
    expect(cells(flourishing)[5]).toBe('Flourishing');
  });

  it('shows the totals exactly as recorded, never rounded', async () => {
    stubBff({ 'admin/waves': wavesBody, 'admin/results': paged(rows) });

    renderWithProviders(<ResultsView />);

    const body = await bodyRows();
    expect(cells(body[2])[4]).toBe('1,234.5');
    expect(cells(body[3])[4]).toBe('2.75');
  });

  it('shows a band code it has no name for as the raw code, and the module of a module result', async () => {
    stubBff({ 'admin/waves': wavesBody, 'admin/results': paged(rows) });

    renderWithProviders(<ResultsView />);

    const moduleRow = (await bodyRows())[3];
    expect(cells(moduleRow)[2]).toBe('family_voicesVersion 1, module module_a');
    expect(within(moduleRow).getByText('tier_unnamed')).toHaveAttribute('dir', 'ltr');
  });

  it('shows dashes for the total and band of an incomplete result', async () => {
    stubBff({ 'admin/waves': wavesBody, 'admin/results': paged(rows) });

    renderWithProviders(<ResultsView />);

    const incomplete = (await bodyRows())[4];
    expect(cells(incomplete).slice(3, 6)).toEqual(['Incomplete', '—', '—']);
  });

  it('names the bands in Arabic, keeps Western digits and leaves an unknown code as it is', async () => {
    stubBff({ 'admin/waves': wavesBody, 'admin/results': paged(rows) });

    renderWithProviders(<ResultsView />, { language: 'ar' });

    const body = await bodyRows('نتائج الأسر');
    expect(body.map((row) => cells(row)[5])).toEqual(['هشّ', 'انتقالي', 'مزدهر', 'tier_unnamed', '—']);
    expect(cells(body[0])[4]).toBe('58');
    expect(cells(body[3])[4]).toBe('2.75');
    expect(cells(body[4])[3]).toBe('غير مكتملة');
    expect(body.map((row) => row.textContent).join('')).not.toMatch(/[٠-٩۰-۹]/);
  });

  it('says the scores are the platform recorded results, shown as recorded', async () => {
    stubBff({ 'admin/waves': wavesBody, 'admin/results': paged(rows) });

    renderWithProviders(<ResultsView />);

    expect(await screen.findByRole('heading', { level: 1, name: 'Household results' })).toBeInTheDocument();
    expect(screen.getByRole('note')).toHaveTextContent(/results the platform recorded.*shown exactly as recorded/);
  });

  it('shows an empty state when there is no result', async () => {
    stubBff({ 'admin/waves': wavesBody, 'admin/results': paged([]) });

    renderWithProviders(<ResultsView />);

    expect(await screen.findByRole('heading', { name: 'No household results to show' })).toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
  });

  it('filters by wave and questionnaire, kept in the address', async () => {
    const bff = stubBff({ 'admin/waves': wavesBody, 'admin/results': paged(rows) });
    const user = userEvent.setup();
    renderWithProviders(<ResultsView />);
    await bodyRows();
    await screen.findByRole('option', { name: /wave_2026_h1/ });

    await user.selectOptions(screen.getByLabelText('Wave'), WAVE_H1.id);
    await user.type(screen.getByLabelText('Questionnaire code'), ' household_assessment ');
    await user.click(screen.getByRole('button', { name: 'Apply filters' }));

    expect(navigation.replace).toHaveBeenLastCalledWith(`/results?instrumentCode=household_assessment&waveId=${WAVE_H1.id}`, { scroll: false });
    await waitFor(() =>
      expect(bff.lastQuery('admin/results')?.toString()).toBe(`instrumentCode=household_assessment&page=1&pageSize=20&waveId=${WAVE_H1.id}`),
    );
  });

  it('searches by the exact household reference, in capitals, without putting it in the address', async () => {
    const bff = stubBff({ 'admin/waves': wavesBody, 'admin/results': paged(rows) });
    const user = userEvent.setup();
    renderWithProviders(<ResultsView />);
    await bodyRows();

    await user.type(screen.getByLabelText('Household reference'), '  hh-7k2m-9qxa ');
    await user.click(screen.getByRole('button', { name: 'Apply filters' }));

    await waitFor(() => expect(bff.lastQuery('admin/results')?.get('reference')).toBe('HH-7K2M-9QXA'));
    for (const [url] of navigation.replace.mock.calls) expect(url).not.toContain('HH-');
  });

  it('ignores a reference written into the address by hand', async () => {
    navigation.reset('/results', 'reference=HH-7K2M-9QXA');
    const bff = stubBff({ 'admin/waves': wavesBody, 'admin/results': paged(rows) });

    renderWithProviders(<ResultsView />);

    await bodyRows();
    expect(bff.lastQuery('admin/results')?.get('reference')).toBeNull();
  });

  it('explains a malformed reference or questionnaire code without asking the server', async () => {
    stubBff({ 'admin/waves': wavesBody, 'admin/results': paged(rows) });
    const user = userEvent.setup();
    renderWithProviders(<ResultsView />);
    await bodyRows();

    await user.type(screen.getByLabelText('Household reference'), 'HH-12');
    await user.type(screen.getByLabelText('Questionnaire code'), 'Household Assessment');
    await user.click(screen.getByRole('button', { name: 'Apply filters' }));

    expect(screen.getByText('A household reference looks like HH-7K2M-9QXA.')).toBeInTheDocument();
    expect(screen.getByText('Use lowercase letters, digits and underscores, starting with a letter.')).toBeInTheDocument();
    expect(navigation.replace).not.toHaveBeenCalled();
  });

  it('pages on the server', async () => {
    const bff = stubBff({ 'admin/waves': wavesBody, 'admin/results': paged(rows, 1, 20, 41) });
    const user = userEvent.setup();
    renderWithProviders(<ResultsView />);
    await bodyRows();

    await user.click(screen.getByRole('button', { name: 'Next' }));

    expect(navigation.replace).toHaveBeenLastCalledWith('/results?page=2', { scroll: false });
    await waitFor(() => expect(bff.lastQuery('admin/results')?.get('page')).toBe('2'));
  });

  it('goes to the last page when the address asks for a page past the end', async () => {
    navigation.reset('/results', 'page=99');
    stubBff({ 'admin/waves': wavesBody, 'admin/results': (url: URL) => (url.searchParams.get('page') === '99' ? paged([], 99, 20, 41) : paged(rows, 3, 20, 41)) });

    renderWithProviders(<ResultsView />);

    await waitFor(() => expect(navigation.replace).toHaveBeenLastCalledWith('/results?page=3', { scroll: false }));
    expect(await screen.findByText('Page 3 of 3')).toBeInTheDocument();
  });

  it('shows permission denied when the platform refuses the caller', async () => {
    stubBff({ 'admin/waves': wavesBody, 'admin/results': problem(403) });

    renderWithProviders(<ResultsView />);

    expect(await screen.findByRole('heading', { name: 'You do not have access to this page' })).toBeInTheDocument();
  });

  it('shows the filter error when the platform refuses a filter', async () => {
    navigation.reset('/results', 'instrumentCode=household_assessment');
    stubBff({ 'admin/waves': wavesBody, 'admin/results': problem(400, undefined, { errors: { instrumentCode: ['x'] } }) });

    renderWithProviders(<ResultsView />);

    expect(await screen.findByRole('heading', { name: 'Check the filters' })).toBeInTheDocument();
  });

  it('refuses a result whose band comes without a total, showing nothing from it', async () => {
    stubBff({ 'admin/waves': wavesBody, 'admin/results': paged([householdResult({ totalScore: null })]) });

    renderWithProviders(<ResultsView />);

    expect(await screen.findByRole('heading', { name: 'The platform sent a response this page cannot read' })).toBeInTheDocument();
    expect(screen.queryByText('Transitional')).not.toBeInTheDocument();
  });
});
