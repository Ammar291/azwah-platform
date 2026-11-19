/** Bounded exponential backoff for automatic retries (decision M-39). */
export interface RetryPolicy {
  /** The ceiling of the first delay. */
  baseMs: number;
  /** No delay is ever longer. */
  maxMs: number;
  /** Automatic attempts after a failure; then the person retries by hand. */
  maxAttempts: number;
}

export const DEFAULT_RETRY: RetryPolicy = { baseMs: 2_000, maxMs: 60_000, maxAttempts: 6 };

/**
 * How long to wait after the `failures`-th failure in a row: a random delay between zero and
 * `min(maxMs, baseMs × 2^(failures − 1))` ("full jitter", so many phones coming back online do not retry in step).
 * Null once the automatic attempts are used up.
 */
export function retryDelay(policy: RetryPolicy, failures: number, random: () => number = Math.random): number | null {
  if (failures < 1 || failures > policy.maxAttempts) {
    return null;
  }
  const ceiling = Math.min(policy.maxMs, policy.baseMs * 2 ** (failures - 1));
  return Math.floor(Math.min(Math.max(random(), 0), 0.999_999) * ceiling);
}
