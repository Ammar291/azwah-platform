import { DEFAULT_RETRY, retryDelay } from '../retryPolicy';

describe('retryDelay', () => {
  it('doubles the ceiling with each failure, up to the maximum', () => {
    const top = () => 0.999_999;
    expect([1, 2, 3, 4, 5, 6].map((failures) => retryDelay(DEFAULT_RETRY, failures, top))).toEqual([
      1_999, 3_999, 7_999, 15_999, 31_999, 59_999,
    ]);
  });

  it('spreads each delay between zero and its ceiling (full jitter)', () => {
    expect(retryDelay(DEFAULT_RETRY, 3, () => 0)).toBe(0);
    expect(retryDelay(DEFAULT_RETRY, 3, () => 0.5)).toBe(4_000);
    const delays = Array.from({ length: 200 }, () => retryDelay(DEFAULT_RETRY, 4) ?? -1);
    expect(Math.min(...delays)).toBeGreaterThanOrEqual(0);
    expect(Math.max(...delays)).toBeLessThan(16_000);
    expect(new Set(delays).size).toBeGreaterThan(1);
  });

  it('stops after the last automatic attempt', () => {
    expect(retryDelay(DEFAULT_RETRY, DEFAULT_RETRY.maxAttempts + 1)).toBeNull();
    expect(retryDelay(DEFAULT_RETRY, 0)).toBeNull();
  });

  it('never goes outside the bounds, whatever the random source gives', () => {
    expect(retryDelay(DEFAULT_RETRY, 2, () => 1)).toBe(3_999);
    expect(retryDelay(DEFAULT_RETRY, 2, () => -3)).toBe(0);
  });
});
