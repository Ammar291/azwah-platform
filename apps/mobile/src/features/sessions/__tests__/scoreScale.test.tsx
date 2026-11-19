import { screen, within } from '@testing-library/react-native';

import type { ScoreScale as Scale } from '@/api/participant/schemas';
import { renderWithProviders, settleQueries } from '@/test-utils/renderWithProviders';

import { bandShare, scalePosition } from '../scalePosition';
import { ScoreScale } from '../ScoreScale';

const SCALE: Scale = {
  minimum: 39,
  maximum: 156,
  bands: [
    { code: 'vulnerable', min: 39, max: 78 },
    { code: 'transitional', min: 79, max: 117 },
    { code: 'flourishing', min: 118, max: 156 },
  ],
};

const tierLabel = (code: string) => ({ vulnerable: 'Vulnerable', transitional: 'Transitional', flourishing: 'Flourishing' })[code] ?? code;

afterEach(settleQueries);

describe('where a total sits on the scale', () => {
  /** The drawn start and end of a band, as shares of the scale. */
  function extent(code: string): [number, number] {
    let start = 0;
    for (const band of SCALE.bands) {
      const end = start + bandShare(band, SCALE);
      if (band.code === code) return [start, end];
      start = end;
    }
    throw new Error(`No band ${code}`);
  }

  it.each([
    [39, 'vulnerable'],
    [78, 'vulnerable'],
    [79, 'transitional'],
    [117, 'transitional'],
    [118, 'flourishing'],
    [156, 'flourishing'],
  ])('keeps %i inside the %s band drawn on the scale, even at its edge', (total, code) => {
    const [start, end] = extent(code);
    const position = scalePosition(total, SCALE);

    expect(position).toBeGreaterThan(start);
    expect(position).toBeLessThan(end);
  });

  it('draws the bands to fill the whole scale', () => {
    expect(SCALE.bands.reduce((sum, band) => sum + bandShare(band, SCALE), 0)).toBeCloseTo(1);
  });

  it('keeps a total off the scale at the nearer end', () => {
    expect(scalePosition(10, SCALE)).toBe(0);
    expect(scalePosition(500, SCALE)).toBe(1);
  });

  it('copes with a scale that has no width', () => {
    expect(scalePosition(5, { minimum: 5, maximum: 4 })).toBe(0);
    expect(bandShare({ code: 'x', min: 5, max: 4 }, { minimum: 5, maximum: 4 })).toBe(0);
  });
});

describe('the grading scale', () => {
  it('lists every band with its range and marks the one the server recorded', async () => {
    await renderWithProviders(<ScoreScale total={96} tierCode="transitional" scale={SCALE} tierLabel={tierLabel} />);

    expect(screen.getByText('Where your score stands')).toBeOnTheScreen();
    const current = within(screen.getByTestId('result-scale-band-transitional'));
    expect(current.getByText('Transitional')).toBeOnTheScreen();
    expect(current.getByText(/79–117/)).toBeOnTheScreen();
    expect(current.getByText('Your family: 96')).toBeOnTheScreen();
    expect(within(screen.getByTestId('result-scale-band-vulnerable')).queryByText(/Your family/)).toBeNull();
    expect(within(screen.getByTestId('result-scale-band-flourishing')).queryByText(/Your family/)).toBeNull();
    expect(screen.getByText('39')).toBeOnTheScreen();
    expect(screen.getByText('156')).toBeOnTheScreen();
  });

  it('marks the band the server recorded, not one worked out from the total', async () => {
    await renderWithProviders(<ScoreScale total={96} tierCode="flourishing" scale={SCALE} tierLabel={tierLabel} />);

    expect(within(screen.getByTestId('result-scale-band-flourishing')).getByText('Your family: 96')).toBeOnTheScreen();
    expect(within(screen.getByTestId('result-scale-band-transitional')).queryByText(/Your family/)).toBeNull();
  });

  it('tells a screen reader where the score stands in words', async () => {
    await renderWithProviders(<ScoreScale total={96} tierCode="transitional" scale={SCALE} tierLabel={tierLabel} />);

    expect(
      screen.getByLabelText("Your family's score is 96, on a scale from 39 to 156. It is in the Transitional band."),
    ).toBeOnTheScreen();
  });

  it('places the marker by logical start, so it mirrors in Arabic', async () => {
    await renderWithProviders(<ScoreScale total={39} tierCode="vulnerable" scale={SCALE} tierLabel={tierLabel} />, { language: 'ar' });

    const marker = screen.getByTestId('result-scale-marker');
    const style = Object.assign({}, ...[marker.props.style].flat()) as Record<string, unknown>;
    expect(String(style.start)).toMatch(/%$/);
    expect(style).not.toHaveProperty('left');
    expect(screen.getByText('أين تقف درجتك')).toBeOnTheScreen();
  });

  it('shows a band with no wording as recorded', async () => {
    const scale: Scale = { minimum: 2, maximum: 6, bands: [{ code: 'band_low', min: 2, max: 3 }, { code: 'band_high', min: 4, max: 6 }] };
    await renderWithProviders(<ScoreScale total={2} tierCode="band_low" scale={scale} tierLabel={(code) => code} />);

    expect(screen.getByText('band_low')).toBeOnTheScreen();
    expect(screen.getByText('band_high')).toBeOnTheScreen();
  });
});
