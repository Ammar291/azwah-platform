/**
 * When the app locks (security follow-ups spec §4; the numbers are an engineering call). They live only here:
 * nothing about the lock is configurable from the environment.
 */
export const LOCK_TIMES = {
  /** Coming back to the foreground after at least this long away locks the app. */
  backgroundMs: 120_000,
  /** This long without a touch while the app is in the foreground locks it. */
  idleMs: 600_000,
  /**
   * The reload that applies a language the person just chose (it changes the layout direction) is not a new start
   * when it comes within this long of the choice, so it does not lock (bootstrap.ts).
   */
  languageReloadMs: 30_000,
} as const;

export type LockTimes = Pick<typeof LOCK_TIMES, 'backgroundMs' | 'idleMs'>;
