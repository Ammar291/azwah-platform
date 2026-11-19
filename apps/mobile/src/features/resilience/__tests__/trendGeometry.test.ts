import type { ResilienceTrendPoint, ScoreScale } from '@/api/participant/schemas';

import { axisTicks, bandExtents, runsOf, toSvgX, xOf, yOf, type PlotBox } from '../trendGeometry';

// A synthetic scale, not an approved one: 10 to 40 in two bands.
const scale: ScoreScale = {
  minimum: 10,
  maximum: 40,
  bands: [
    { code: 'band_low', min: 10, max: 25 },
    { code: 'band_high', min: 26, max: 40 },
  ],
};
const box: PlotBox = { start: 40, top: 10, width: 200, height: 150 };

const point = (segment: number, index: number): ResilienceTrendPoint => ({
  sessionId: `0199e0a1-0000-7000-8000-00000000000${index}`,
  waveCode: `wave_${index}`,
  submittedAt: '2026-05-01T09:00:00Z',
  totalScore: 20,
  tierCode: null,
  segment,
});

describe('trend geometry', () => {
  it('puts the top of the scale at the top of the plot and the bottom at the bottom', () => {
    expect(yOf(40, scale, box)).toBe(10);
    expect(yOf(10, scale, box)).toBe(160);
    expect(yOf(25, scale, box)).toBe(85);
  });

  it('keeps a value outside the scale on its edge rather than off the chart', () => {
    expect(yOf(45, scale, box)).toBe(10);
    expect(yOf(5, scale, box)).toBe(160);
  });

  it('spreads check-ins evenly from the start of the plot to its end, and centres a single one', () => {
    expect([0, 1, 2].map((index) => xOf(index, 3, box))).toEqual([40, 140, 240]);
    expect(xOf(0, 1, box)).toBe(140);
  });

  it('measures SVG positions from the right when the layout runs right to left', () => {
    expect(toSvgX(40, 300, false)).toBe(40);
    expect(toSvgX(40, 300, true)).toBe(260);
  });

  it('draws bands that meet halfway between their whole-number bounds, inside the scale', () => {
    expect(bandExtents(scale)).toEqual([
      { code: 'band_low', from: 10, to: 25.5 },
      { code: 'band_high', from: 25.5, to: 40 },
    ]);
  });

  it('ticks the bottom, where each later band begins, and the top', () => {
    expect(axisTicks(scale)).toEqual([10, 26, 40]);
    expect(axisTicks({ minimum: 4, maximum: 12, bands: [] })).toEqual([4, 12]);
  });

  it('groups consecutive points of one segment into one run, so lines never join two segments', () => {
    expect(runsOf([point(0, 0), point(0, 1), point(1, 2), point(1, 3), point(2, 4)])).toEqual([[0, 1], [2, 3], [4]]);
    expect(runsOf([])).toEqual([]);
  });
});
