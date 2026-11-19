import type { ResilienceTrendPoint, ScoreScale } from '@/api/participant/schemas';

/*
 * Where the trend chart (D-132) draws what the server sent. Display geometry only: no score, band or change is computed
 * here; values are placed on the scale exactly as recorded.
 */

/** The plot inside the chart, in points: `start` is measured from the start edge, so it mirrors with the layout. */
export interface PlotBox {
  start: number;
  top: number;
  width: number;
  height: number;
}

/** The scale's maximum at the top of the plot and its minimum at the bottom; a value outside it stays on its edge. */
export function yOf(value: number, scale: ScoreScale, box: PlotBox): number {
  const clamped = Math.min(scale.maximum, Math.max(scale.minimum, value));
  return box.top + ((scale.maximum - clamped) / (scale.maximum - scale.minimum)) * box.height;
}

/** One place per check-in, evenly from the start of the plot to its end; a single check-in sits in the middle. */
export function xOf(index: number, count: number, box: PlotBox): number {
  return count <= 1 ? box.start + box.width / 2 : box.start + (index / (count - 1)) * box.width;
}

/** SVG has no layout direction: right to left, a distance from the start edge is measured from the right. */
export function toSvgX(fromStart: number, width: number, rtl: boolean): number {
  return rtl ? width - fromStart : fromStart;
}

/** Each band from just below its lowest total to just above its highest, so neighbours meet halfway; inside the scale. */
export function bandExtents(scale: ScoreScale): { code: string; from: number; to: number }[] {
  return scale.bands.map((band) => ({
    code: band.code,
    from: Math.max(scale.minimum, band.min - 0.5),
    to: Math.min(scale.maximum, band.max + 0.5),
  }));
}

/** The axis labels: the bottom of the scale, the lowest total of each band after the first, and the top. */
export function axisTicks(scale: ScoreScale): number[] {
  const ticks = new Set([scale.minimum, ...scale.bands.slice(1).map((band) => band.min), scale.maximum]);
  return [...ticks].sort((a, b) => a - b);
}

/** Indexes of consecutive points that share a segment: each run is one line, and no line joins two segments. */
export function runsOf(points: readonly ResilienceTrendPoint[]): number[][] {
  const runs: number[][] = [];
  points.forEach((point, index) => {
    const run = runs[runs.length - 1];
    const previous = run ? points[run[run.length - 1] ?? -1] : undefined;
    if (run && previous?.segment === point.segment) {
      run.push(index);
    } else {
      runs.push([index]);
    }
  });
  return runs;
}
