import type { AuthState } from '@/auth/types';

import type { AuthenticationPrompt, DeviceAuthentication, DeviceAuthenticationOutcome } from './deviceAuthentication';
import { LOCK_TIMES, type LockTimes } from './lockTimes';

/** React Native's AppStateStatus. */
export type AppStateValue = 'active' | 'inactive' | 'background' | 'unknown' | 'extension';

/** The part of React Native's AppState the lock listens to. Tests pass a fake. */
export interface AppStateSource {
  readonly currentState: AppStateValue;
  addEventListener(type: 'change', listener: (state: AppStateValue) => void): { remove(): void };
}

/** Time for the lock: the clock and timers. The defaults read the globals when called, so Jest's fake timers apply. */
export interface LockTimers {
  /** The wall clock (Date.now), which the person can set back. */
  now(): number;
  /**
   * A steady clock that setting the phone's clock does not move (performance.now), when the runtime has one. It may
   * not count time the phone spends asleep, so it only ever adds to what the wall clock says.
   */
  monotonicNow?(): number;
  setTimeout(callback: () => void, ms: number): unknown;
  clearTimeout(handle: unknown): void;
}

const hasSteadyClock = typeof performance !== 'undefined' && typeof performance.now === 'function';

export const systemLockTimers: LockTimers = {
  now: () => Date.now(),
  monotonicNow: hasSteadyClock ? () => performance.now() : undefined,
  setTimeout: (callback, ms) => setTimeout(callback, ms),
  clearTimeout: (handle) => clearTimeout(handle as ReturnType<typeof setTimeout>),
};

/** A moment on both clocks. */
interface Moment {
  wall: number;
  steady: number | null;
}

export interface LockState {
  /** Signed in and locked: only the lock screen may show, and no signed-in screen is mounted. */
  locked: boolean;
  /** The app is not in the foreground: the privacy cover hides everything, so the app switcher shows no answers. */
  covered: boolean;
  /** The phone is asking its owner to confirm. */
  unlocking: boolean;
  /** The signed-in screen the person was on when the app locked; unlocking returns there. Memory only. */
  resumeHref: string | null;
}

export interface LockControllerOptions {
  authentication: DeviceAuthentication;
  /** AppState.currentState when the controller starts. */
  appState: AppStateValue;
  timers?: LockTimers;
  times?: LockTimes;
  /** Lock a session restored at start-up (default). False only for the reload that applies a chosen language. */
  lockRestoredSession?: boolean;
}

type AuthStatus = AuthState['status'];

const isAway = (state: AppStateValue) => state === 'inactive' || state === 'background';

const steadyGap = (since: Moment, now: Moment) =>
  since.steady !== null && now.steady !== null ? now.steady - since.steady : null;

/**
 * Time away from the foreground. A wall clock that went backwards meanwhile counts as the limit reached (`Infinity`):
 * the person may have set it back in Settings to dodge the lock. The steady clock can only make the time longer.
 */
function timeAway(since: Moment, now: Moment): number {
  const wall = now.wall - since.wall;
  const steady = steadyGap(since, now);
  return wall < 0 ? Infinity : Math.max(wall, steady ?? wall);
}

/**
 * Time since the last activity, in the foreground, where the clock can only be changed by the system (a time sync).
 * With a steady clock, that counts and a backwards wall clock is ignored; without one, a backwards wall clock counts
 * as the limit reached.
 */
function idleTime(since: Moment, now: Moment): number {
  const wall = now.wall - since.wall;
  const steady = steadyGap(since, now);
  if (steady !== null) {
    return Math.max(wall, steady);
  }
  return wall < 0 ? Infinity : wall;
}

/**
 * Decides when the app locks (spec §4). While someone is signed in it locks: on coming back after `backgroundMs` or
 * more away from the foreground; after `idleMs` without a touch in the foreground (time away does not count, it has
 * its own limit); and when start-up restores a session. Time is measured on the wall clock and, when the runtime has
 * one, a steady clock; whichever says more counts, and a wall clock set back while away counts as the limit reached. Only
 * the phone's own authentication unlocks it; signing out lifts it. No React, no native modules: the provider feeds it
 * auth and app-state changes and activity.
 */
export class LockController {
  private state: LockState;
  private readonly listeners = new Set<() => void>();
  private readonly authentication: DeviceAuthentication;
  private readonly timers: LockTimers;
  private readonly times: LockTimes;
  private readonly lockRestoredSession: boolean;
  private authStatus: AuthStatus | null = null;
  private appState: AppStateValue;
  private lastActivityAt: Moment = { wall: 0, steady: null };
  private leftForegroundAt: Moment | null = null;
  private readonly lockListeners = new Set<() => void>();
  private idleTimer: unknown = null;
  private lastLocation: string | null = null;
  private pendingUnlock: Promise<DeviceAuthenticationOutcome> | null = null;
  private running = true;

  constructor(options: LockControllerOptions) {
    this.authentication = options.authentication;
    this.timers = options.timers ?? systemLockTimers;
    this.times = options.times ?? LOCK_TIMES;
    this.lockRestoredSession = options.lockRestoredSession ?? true;
    this.appState = options.appState;
    this.state = { locked: false, covered: isAway(options.appState), unlocking: false, resumeHref: null };
  }

  readonly getState = (): LockState => this.state;

  readonly subscribe = (listener: () => void): (() => void) => {
    this.listeners.add(listener);
    return () => {
      this.listeners.delete(listener);
    };
  };

  /** Called each time the lock engages (to delete anything sensitive the system might still show). */
  onLock(listener: () => void): () => void {
    this.lockListeners.add(listener);
    return () => {
      this.lockListeners.delete(listener);
    };
  }

  /**
   * Whether something sensitive may be put in front of the person right now: the app is in the foreground and not
   * locked. Read at the last moment, for example just before the share sheet opens.
   */
  mayRevealContent(): boolean {
    return this.appState === 'active' && !this.state.locked;
  }

  private get signedIn(): boolean {
    return this.authStatus === 'signed_in';
  }

  private moment(): Moment {
    return { wall: this.timers.now(), steady: this.timers.monotonicNow?.() ?? null };
  }

  /** Feed every auth state, the current one first. The first settled state being signed in is a restored session. */
  authStateChanged(status: AuthStatus): void {
    const previous = this.authStatus;
    this.authStatus = status;
    if (status !== 'signed_in') {
      this.clearIdle();
      this.leftForegroundAt = null;
      this.lastLocation = null;
      this.update({ locked: false, resumeHref: null });
      return;
    }
    if (previous === 'signed_in') return;
    const restored = previous === null || previous === 'initializing';
    if (restored && this.lockRestoredSession) {
      this.lock();
    } else {
      this.update({ locked: false, resumeHref: null });
      this.startIdle();
    }
  }

  appStateChanged(next: AppStateValue): void {
    const wasActive = this.appState === 'active';
    this.appState = next;
    if (wasActive && next !== 'active') {
      this.clearIdle();
      // The system's own unlock prompt takes the app out of the foreground; that is not time away.
      this.leftForegroundAt = this.signedIn && !this.state.locked && !this.state.unlocking ? this.moment() : null;
    } else if (!wasActive && next === 'active') {
      const leftAt = this.leftForegroundAt;
      this.leftForegroundAt = null;
      if (this.signedIn && !this.state.locked) {
        if (leftAt !== null && timeAway(leftAt, this.moment()) >= this.times.backgroundMs) {
          this.lock();
        } else {
          this.startIdle();
        }
      }
    }
    this.update({ covered: isAway(next) });
  }

  /**
   * The person did something: a touch anywhere, a new screen, an answer chosen (keyboard and screen-reader users do
   * not always touch). Cheap: it only notes the time; the idle timer checks it when it fires.
   */
  readonly touched = (): void => {
    if (this.signedIn && !this.state.locked) {
      this.lastActivityAt = this.moment();
    }
  };

  /**
   * The signed-in screen on show (a path). Ignored while locked or signed out, and for the anchor route. The place to
   * return to after unlocking is used once: the first screen noted after unlocking clears it.
   */
  noteLocation(href: string): void {
    if (this.signedIn && !this.state.locked && href !== '/') {
      this.lastLocation = href;
      this.update({ resumeHref: null });
    }
  }

  isAuthenticationAvailable(): Promise<boolean> {
    return this.authentication.isAvailable().catch(() => false);
  }

  /** Asks the phone to confirm its owner; one prompt at a time. Unlocks only on `success`. */
  unlock(prompt: AuthenticationPrompt): Promise<DeviceAuthenticationOutcome> {
    if (!this.state.locked) return Promise.resolve('success');
    this.pendingUnlock ??= this.authenticate(prompt).finally(() => {
      this.pendingUnlock = null;
    });
    return this.pendingUnlock;
  }

  /** Stops the idle timer (the provider unmounted). Events are still followed; `start` brings the timer back. */
  stop(): void {
    this.running = false;
    this.clearIdle();
  }

  start(): void {
    this.running = true;
    if (this.idleTimer === null) {
      this.scheduleIdleCheck(0);
    }
  }

  private async authenticate(prompt: AuthenticationPrompt): Promise<DeviceAuthenticationOutcome> {
    this.update({ unlocking: true });
    let outcome: DeviceAuthenticationOutcome;
    try {
      outcome = await this.authentication.authenticate(prompt);
    } catch {
      outcome = 'failed';
    }
    if (outcome === 'success' && this.signedIn && this.state.locked) {
      this.update({ locked: false, unlocking: false });
      if (this.appState === 'active') {
        this.startIdle();
      } else {
        this.leftForegroundAt = this.moment();
      }
    } else {
      this.update({ unlocking: false });
    }
    return outcome;
  }

  private lock(): void {
    this.clearIdle();
    this.leftForegroundAt = null;
    this.update({ locked: true, resumeHref: this.lastLocation });
    [...this.lockListeners].forEach((listener) => listener());
  }

  private startIdle(): void {
    this.lastActivityAt = this.moment();
    if (this.appState === 'active') {
      this.scheduleIdleCheck(this.times.idleMs);
    }
  }

  private scheduleIdleCheck(delayMs: number): void {
    this.clearIdle();
    if (this.running && this.signedIn && !this.state.locked && this.appState === 'active') {
      this.idleTimer = this.timers.setTimeout(this.checkIdle, delayMs);
    }
  }

  private readonly checkIdle = (): void => {
    this.idleTimer = null;
    if (!this.running || !this.signedIn || this.state.locked || this.appState !== 'active') return;
    const idleFor = idleTime(this.lastActivityAt, this.moment());
    if (idleFor >= this.times.idleMs) {
      this.lock();
    } else {
      this.scheduleIdleCheck(this.times.idleMs - idleFor);
    }
  };

  private clearIdle(): void {
    if (this.idleTimer !== null) {
      this.timers.clearTimeout(this.idleTimer);
      this.idleTimer = null;
    }
  }

  private update(patch: Partial<LockState>): void {
    const next = { ...this.state, ...patch };
    const changed = (Object.keys(next) as (keyof LockState)[]).some((key) => next[key] !== this.state[key]);
    if (!changed) return;
    this.state = next;
    [...this.listeners].forEach((listener) => listener());
  }
}
