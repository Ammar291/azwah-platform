import type { LockTimers } from '../lockController';

/**
 * A clock for the lock that moves only when told, so screen tests can pass ten minutes without Jest's fake timers
 * (which would also hold back React and the test library). `advance` moves the wall clock, the steady clock and the
 * timers; `setWallClockBack` moves only the wall clock, as a person changing the phone's time would.
 */
export function createFakeLockTimers(start = 1_000_000) {
  let now = start;
  let wallOffset = 0;
  let nextId = 1;
  const due = new Map<number, { at: number; callback: () => void }>();

  const timers: LockTimers = {
    now: () => now + wallOffset,
    monotonicNow: () => now,
    setTimeout: (callback, ms) => {
      const id = nextId++;
      due.set(id, { at: now + ms, callback });
      return id;
    },
    clearTimeout: (handle) => {
      due.delete(handle as number);
    },
  };

  return {
    ...timers,
    /** Moves the clock on, running each timer that falls due on the way, in order. */
    advance(ms: number) {
      const until = now + ms;
      for (;;) {
        const next = [...due.entries()].sort(([, a], [, b]) => a.at - b.at)[0];
        if (!next || next[1].at > until) break;
        due.delete(next[0]);
        now = next[1].at;
        next[1].callback();
      }
      now = until;
    },
    setWallClockBack(ms: number) {
      wallOffset -= ms;
    },
  };
}

export type FakeLockTimers = ReturnType<typeof createFakeLockTimers>;
