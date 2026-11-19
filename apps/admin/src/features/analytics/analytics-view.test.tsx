import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { problem, stubBff } from '@/test/bff';
import { distribution, distributionsBody, paged, WAVE_H1, WAVE_H2, wavesBody } from '@/test/fixtures';
import { navigation } from '@/test/navigation';
import { renderWithProviders } from '@/test/render';

import { AnalyticsView } from './analytics-view';

vi.mock('next/navigation', async () => (await import('@/test/navigation')).nextNavigation);

beforeEach(() => navigation.reset('/analytics'));

const household = distribution();
const moduleA = distribution({
  instrumentCode: 'family_voices',
  sectionCode: 'module_a',
  ruleSetCode: 'module_scoring',
  total: 30,
  tiers: [
    { tier: 'tier_a', count: 22, suppressed: false },
    { tier: 'tier_b', count: null, suppressed: true },
    { tier: 'tier_c', count: null, suppressed: true },
  ],
});
const hiddenModule = distribution({ instrumentCode: 'family_voices', sectionCode: 'module_b', ruleSetCode: 'module_scoring', total: null, suppressed: true, tiers: [] });
const householdV2 = distribution({ versionNumber: 2, ruleSetCode: 'household_scoring_v2', total: 15, tiers: [{ tier: 'tier_a', count: 15, suppressed: false }] });

function byWave(url: URL) {
  const waveId = url.searchParams.get('waveId');
  if (waveId === WAVE_H1.id) return distributionsBody([distribution({ total: 20, tiers: [{ tier: 'tier_a', count: 10, suppressed: false }, { tier: 'tier_b', count: 10, suppressed: false }] })], WAVE_H1.id);
  if (waveId === WAVE_H2.id) return distributionsBody([distribution({ total: 40 }), householdV2], WAVE_H2.id);
  return distributionsBody([household, moduleA, hiddenModule, householdV2]);
}

describe('AnalyticsView', () => {
  it('explains that no scores exist until a scoring rule set is approved', async () => {
    stubBff({ 'admin/waves': wavesBody, 'admin/analytics/score-distributions': distributionsBody([]) });

    renderWithProviders(<AnalyticsView />);

    expect(await screen.findByRole('heading', { name: 'No approved scores yet' })).toBeInTheDocument();
    expect(screen.getByText(/once a scoring rule set is approved/)).toBeInTheDocument();
  });

  it('shows one card per version, module and rule set, never combining them', async () => {
    stubBff({ 'admin/waves': wavesBody, 'admin/analytics/score-distributions': byWave });

    renderWithProviders(<AnalyticsView />);

    const cards = await screen.findAllByRole('article');
    expect(cards.map((card) => within(card).getByRole('heading').textContent)).toEqual([
      'household_assessment, version 1',
      'family_voices, version 1',
      'family_voices, version 1',
      'household_assessment, version 2',
    ]);
    expect(within(cards[0]).getByText('Scored with household_scoring, version 1')).toBeInTheDocument();
    expect(within(cards[3]).getByText('Scored with household_scoring_v2, version 1')).toBeInTheDocument();
  });

  it('hides suppressed tiers and lists the rest in the table view', async () => {
    stubBff({ 'admin/waves': wavesBody, 'admin/analytics/score-distributions': byWave });
    const user = userEvent.setup();

    renderWithProviders(<AnalyticsView />);

    const card = (await screen.findAllByRole('article'))[1];
    await user.click(within(card).getByRole('button', { name: 'Show as table' }));
    const rows = within(within(card).getByRole('table')).getAllByRole('row').map((row) => row.textContent);
    expect(rows).toEqual(['BandResults', 'tier_a22', 'tier_bHidden', 'tier_cHidden']);
    // The hidden bands are named once, in the page direction, outside the drawing.
    const note = within(card).getByText('Hidden to protect privacy: tier_b, tier_c');
    expect(within(card).getByRole('img')).not.toContainElement(note);
  });

  it('makes each card one figure, captioned by the card heading and not by the section heading again', async () => {
    stubBff({ 'admin/waves': wavesBody, 'admin/analytics/score-distributions': byWave });

    renderWithProviders(<AnalyticsView />);

    const card = (await screen.findAllByRole('article'))[0];
    const figure = within(card).getByRole('figure', { name: 'household_assessment, version 1' });
    expect(figure.querySelector('figcaption')).toBeNull();
    expect(figure.className).not.toMatch(/border/);
    expect(within(card).queryByText('Score distributions')).toBeNull();
    expect(screen.getAllByText('Score distributions')).toHaveLength(1);
  });

  it('says what the number on a card counts: complete results with a band, not people', async () => {
    stubBff({ 'admin/waves': wavesBody, 'admin/analytics/score-distributions': byWave });

    renderWithProviders(<AnalyticsView />);

    const card = (await screen.findAllByRole('article'))[0];
    expect(within(card).getByText('Complete results with a band').nextElementSibling).toHaveTextContent('60');
    expect(within(card).getByText('Counted per scored session, not per person: someone scored in two waves counts twice.')).toBeInTheDocument();
    expect(screen.queryByText(/People/)).toBeNull();
  });

  it('names the bands of the specification, lowest first, and shows any other code as recorded', async () => {
    const banded = distribution({
      ruleSetCode: 'household_assessment_total',
      total: 96,
      tiers: [
        { tier: 'flourishing', count: 30, suppressed: false },
        { tier: 'other_code', count: 12, suppressed: false },
        { tier: 'transitional', count: 40, suppressed: false },
        { tier: 'vulnerable', count: 14, suppressed: false },
      ],
    });
    stubBff({ 'admin/waves': wavesBody, 'admin/analytics/score-distributions': distributionsBody([banded]) });
    const user = userEvent.setup();

    renderWithProviders(<AnalyticsView />);

    const card = (await screen.findAllByRole('article'))[0];
    await user.click(within(card).getByRole('button', { name: 'Show as table' }));
    const rows = within(within(card).getByRole('table')).getAllByRole('row').map((row) => row.textContent);
    expect(rows).toEqual(['BandResults', 'Vulnerable14', 'Transitional40', 'Flourishing30', 'other_code12']);
  });

  it('names the bands in Arabic and draws the chart left to right', async () => {
    const banded = distribution({
      total: 84,
      tiers: [
        { tier: 'flourishing', count: 30, suppressed: false },
        { tier: 'transitional', count: 40, suppressed: false },
        { tier: 'vulnerable', count: 14, suppressed: false },
      ],
    });
    stubBff({ 'admin/waves': wavesBody, 'admin/analytics/score-distributions': distributionsBody([banded]) });
    const user = userEvent.setup();

    renderWithProviders(<AnalyticsView />, { language: 'ar' });

    const card = (await screen.findAllByRole('article'))[0];
    expect(within(card).getByRole('img')).toHaveAttribute('dir', 'ltr');
    await user.click(within(card).getByRole('button', { name: 'عرض كجدول' }));
    const rows = within(within(card).getByRole('table')).getAllByRole('row').map((row) => row.textContent);
    expect(rows).toEqual(['المستوىالنتائج', 'هشّ14', 'انتقالي40', 'مزدهر30']);
  });

  it('shows a distribution with too few results as hidden, with no bands at all', async () => {
    stubBff({ 'admin/waves': wavesBody, 'admin/analytics/score-distributions': byWave });

    renderWithProviders(<AnalyticsView />);

    const card = (await screen.findAllByRole('article'))[2];
    expect(within(card).getByText('These results cover fewer than 10 people, so the bands are hidden.')).toBeInTheDocument();
    expect(within(card).queryByRole('figure')).toBeNull();
    expect(within(card).queryByText(/tier_/)).toBeNull();
  });

  it('says that a rule set hidden in every compared wave is hidden, instead of drawing an empty chart', async () => {
    const hidden = (waveId: string | null) => distributionsBody([hiddenModule], waveId);
    stubBff({ 'admin/waves': wavesBody, 'admin/analytics/score-distributions': (url: URL) => hidden(url.searchParams.get('waveId')) });

    renderWithProviders(<AnalyticsView />);

    const trends = await screen.findByRole('region', { name: 'Trends by assessment period' });
    const comparison = await within(trends).findByRole('region', { name: 'family_voices, version 1, across waves' });
    expect(within(comparison).getByText('In every compared wave these results cover fewer than 10 people, so the bands are hidden.')).toBeInTheDocument();
    expect(within(trends).queryByRole('figure')).toBeNull();
    expect(within(trends).queryByRole('img')).toBeNull();
    expect(within(trends).queryByRole('button', { name: 'Show as table' })).toBeNull();
  });

  it('compares the same version and rule set across waves only', async () => {
    stubBff({ 'admin/waves': wavesBody, 'admin/analytics/score-distributions': byWave });
    const user = userEvent.setup();

    renderWithProviders(<AnalyticsView />);

    const trends = await screen.findByRole('region', { name: 'Trends by assessment period' });
    const figure = await within(trends).findByRole('figure', { name: 'household_assessment, version 1, across waves' });
    await user.click(within(figure).getByRole('button', { name: 'Show as table' }));
    const rows = within(within(figure).getByRole('table')).getAllByRole('row').map((row) => row.textContent);
    expect(rows).toEqual(['Bandwave_2026_h1wave_2026_h2', 'tier_a1025', 'tier_b1035']);
    expect(within(trends).queryByRole('figure', { name: /family_voices/ })).toBeNull();
  });

  it('leaves planned waves out of the comparison, since nothing can be scored in them yet', async () => {
    const planned = (id: string, code: string, opensAt: string) => ({ id, code, status: 'planned' as const, opensAt, closesAt: '2028-12-31T00:00:00+00:00' });
    const waves = paged(
      [
        planned('0190f0e2-9999-7000-8000-000000000002', 'wave_2027_h2', '2027-07-01T00:00:00+00:00'),
        planned('0190f0e2-9999-7000-8000-000000000001', 'wave_2027_h1', '2027-01-01T00:00:00+00:00'),
        WAVE_H2,
        WAVE_H1,
      ],
      1,
      100,
    );
    const bff = stubBff({ 'admin/waves': waves, 'admin/analytics/score-distributions': byWave });

    renderWithProviders(<AnalyticsView />);

    const trends = await screen.findByRole('region', { name: 'Trends by assessment period' });
    expect(await within(trends).findByRole('figure', { name: 'household_assessment, version 1, across waves' })).toBeInTheDocument();
    expect(bff.calls.some((call) => call.url.searchParams.get('waveId')?.startsWith('0190f0e2-9999'))).toBe(false);
  });

  it('asks for the comparison only without a wave filter', async () => {
    navigation.reset('/analytics', `waveId=${WAVE_H2.id}`);
    const bff = stubBff({ 'admin/waves': wavesBody, 'admin/analytics/score-distributions': byWave });

    renderWithProviders(<AnalyticsView />);

    expect(await screen.findByText('Clear the wave filter to compare waves.')).toBeInTheDocument();
    await waitFor(() => expect(bff.lastQuery('admin/analytics/score-distributions')?.get('waveId')).toBe(WAVE_H2.id));
  });

  it('shows no demographic score comparison, because none is approved', async () => {
    stubBff({ 'admin/waves': wavesBody, 'admin/analytics/score-distributions': byWave });

    renderWithProviders(<AnalyticsView />);

    expect(await screen.findByRole('heading', { name: 'No approved demographic comparison' })).toBeInTheDocument();
  });

  it('shows permission denied when the platform refuses scores to this role', async () => {
    stubBff({ 'admin/waves': wavesBody, 'admin/analytics/score-distributions': problem(403) });

    renderWithProviders(<AnalyticsView />);

    expect(await screen.findByRole('heading', { name: 'You do not have access to this page' })).toBeInTheDocument();
  });
});
