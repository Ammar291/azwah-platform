import { LockController, systemLockTimers, type LockControllerOptions, type LockTimers } from '../lockController';
import { LOCK_TIMES } from '../lockTimes';
import { createFakeDeviceAuthentication } from '../testing/fakeDeviceAuthentication';

const PROMPT = { promptMessage: 'Unlock Azwah', cancelLabel: 'Cancel' };

function controllerFor(options: Partial<LockControllerOptions> = {}) {
  const authentication = createFakeDeviceAuthentication();
  const controller = new LockController({ authentication, appState: 'active', ...options });
  return { controller, authentication };
}

/** Someone who signed in on this run of the app (not a restored session). */
function signedIn(options: Partial<LockControllerOptions> = {}) {
  const harness = controllerFor(options);
  harness.controller.authStateChanged('signed_out');
  harness.controller.authStateChanged('signed_in');
  return harness;
}

const away = (controller: LockController, ms: number, via: ('inactive' | 'background')[] = ['background']) => {
  via.forEach((state) => controller.appStateChanged(state));
  jest.advanceTimersByTime(ms);
  controller.appStateChanged('active');
};

/** The wall clock and timers only, as on a runtime without a steady clock. */
const wallClockOnly: LockTimers = {
  now: () => Date.now(),
  setTimeout: (callback, ms) => setTimeout(callback, ms),
  clearTimeout: (handle) => clearTimeout(handle as ReturnType<typeof setTimeout>),
};

/** Sets the phone's clock back; Jest's steady clock (performance.now) keeps counting. */
const setClockBack = (ms: number) => jest.setSystemTime(Date.now() - ms);

beforeEach(() => jest.useFakeTimers());
afterEach(() => jest.useRealTimers());

describe('the app lock', () => {
  it('keeps both durations in one place', () => {
    expect(LOCK_TIMES).toMatchObject({ backgroundMs: 120_000, idleMs: 600_000 });
  });

  describe('after time in the background', () => {
    it('locks on coming back after 120 s away', () => {
      const { controller } = signedIn();

      away(controller, 120_000);

      expect(controller.getState().locked).toBe(true);
    });

    it('does not lock after 119 s away', () => {
      const { controller } = signedIn();

      away(controller, 119_000);

      expect(controller.getState().locked).toBe(false);
    });

    it('counts from the moment the app leaves the foreground, through inactive on iOS', () => {
      const { controller } = signedIn();

      away(controller, 120_000, ['inactive', 'background']);

      expect(controller.getState().locked).toBe(true);
    });

    it('covers the screen while inactive or in the background, and uncovers it on return', () => {
      const { controller } = signedIn();
      expect(controller.getState().covered).toBe(false);

      controller.appStateChanged('inactive');
      expect(controller.getState().covered).toBe(true);
      controller.appStateChanged('background');
      expect(controller.getState().covered).toBe(true);
      controller.appStateChanged('active');

      expect(controller.getState()).toMatchObject({ covered: false, locked: false });
    });
  });

  describe('without a touch in the foreground', () => {
    it('locks after 600 s', () => {
      const { controller } = signedIn();

      jest.advanceTimersByTime(599_999);
      expect(controller.getState().locked).toBe(false);
      jest.advanceTimersByTime(1);

      expect(controller.getState().locked).toBe(true);
    });

    it('starts counting again at every touch', () => {
      const { controller } = signedIn();

      jest.advanceTimersByTime(599_000);
      controller.touched();
      jest.advanceTimersByTime(599_000);
      expect(controller.getState().locked).toBe(false);
      jest.advanceTimersByTime(1_000);

      expect(controller.getState().locked).toBe(true);
    });

    it('does not count time in the background, which has its own limit', () => {
      const { controller } = signedIn();

      jest.advanceTimersByTime(500_000);
      away(controller, 100_000);
      jest.advanceTimersByTime(599_000);
      expect(controller.getState().locked).toBe(false);
      jest.advanceTimersByTime(1_000);

      expect(controller.getState().locked).toBe(true);
    });
  });

  describe('at start', () => {
    it('locks a session restored on a cold start', () => {
      const { controller } = controllerFor();

      controller.authStateChanged('signed_in');

      expect(controller.getState().locked).toBe(true);
    });

    it('locks a session restored while start-up was still settling the sign-in', () => {
      const { controller } = controllerFor();

      controller.authStateChanged('initializing');
      controller.authStateChanged('signed_in');

      expect(controller.getState().locked).toBe(true);
    });

    it('does not lock someone who just signed in', () => {
      const { controller } = signedIn();

      expect(controller.getState().locked).toBe(false);
    });

    it('does not lock the reload that applies a language just chosen, which then locks as usual', () => {
      const { controller } = controllerFor({ lockRestoredSession: false });

      controller.authStateChanged('signed_in');
      expect(controller.getState().locked).toBe(false);
      away(controller, 120_000);

      expect(controller.getState().locked).toBe(true);
    });
  });

  describe('when nobody is signed in', () => {
    it('never locks', () => {
      const { controller } = controllerFor();
      controller.authStateChanged('signed_out');

      away(controller, 30 * 60_000);
      jest.advanceTimersByTime(30 * 60_000);

      expect(controller.getState().locked).toBe(false);
    });

    it('lifts the lock when the session ends, and forgets where the person was', () => {
      const { controller } = signedIn();
      controller.noteLocation('/sessions/1');
      away(controller, 120_000);

      controller.authStateChanged('signed_out');

      expect(controller.getState()).toMatchObject({ locked: false, resumeHref: null });
    });

    it('is still covered in the background, so the app switcher shows no screen', () => {
      const { controller } = controllerFor();
      controller.authStateChanged('signed_out');

      controller.appStateChanged('background');

      expect(controller.getState().covered).toBe(true);
    });
  });

  describe('unlocking', () => {
    it('unlocks when the phone confirms its owner, then locks again after the next idle period', async () => {
      const { controller, authentication } = controllerFor();
      controller.authStateChanged('signed_in');

      await expect(controller.unlock(PROMPT)).resolves.toBe('success');

      expect(authentication.prompts).toEqual([PROMPT]);
      expect(controller.getState()).toMatchObject({ locked: false, unlocking: false });
      jest.advanceTimersByTime(600_000);
      expect(controller.getState().locked).toBe(true);
    });

    it.each(['failed', 'cancelled', 'unavailable'] as const)('stays locked when the answer is %s', async (answer) => {
      const { controller, authentication } = controllerFor();
      controller.authStateChanged('signed_in');
      authentication.willAnswer(answer);

      await expect(controller.unlock(PROMPT)).resolves.toBe(answer);

      expect(controller.getState()).toMatchObject({ locked: true, unlocking: false });
    });

    it('stays locked when asking the phone throws', async () => {
      const { controller, authentication } = controllerFor();
      controller.authStateChanged('signed_in');
      authentication.willAnswer(Promise.reject(new Error('native failure')));

      await expect(controller.unlock(PROMPT)).resolves.toBe('failed');

      expect(controller.getState().locked).toBe(true);
    });

    it('asks the phone once however often unlock is pressed', async () => {
      const { controller, authentication } = controllerFor();
      controller.authStateChanged('signed_in');
      let answer: (outcome: 'success') => void = () => undefined;
      authentication.willAnswer(new Promise((resolve) => (answer = resolve)));

      const first = controller.unlock(PROMPT);
      const second = controller.unlock(PROMPT);
      expect(controller.getState().unlocking).toBe(true);
      answer('success');

      await expect(Promise.all([first, second])).resolves.toEqual(['success', 'success']);
      expect(authentication.prompts).toHaveLength(1);
    });

    it('does not count the system prompt as time away', async () => {
      const { controller, authentication } = controllerFor();
      controller.authStateChanged('signed_in');
      let answer: (outcome: 'success') => void = () => undefined;
      authentication.willAnswer(new Promise((resolve) => (answer = resolve)));

      const unlocking = controller.unlock(PROMPT);
      away(controller, 180_000, ['inactive']);
      answer('success');
      await unlocking;

      expect(controller.getState().locked).toBe(false);
    });

    it('reports whether the phone has biometrics or a passcode', async () => {
      const { controller, authentication } = controllerFor();
      authentication.setAvailable(false);

      await expect(controller.isAuthenticationAvailable()).resolves.toBe(false);
    });
  });

  describe('when the clock is set back', () => {
    it('measures with a steady clock as well as the wall clock', () => {
      expect(systemLockTimers.monotonicNow).toEqual(expect.any(Function));
    });

    it('locks on coming back when the clock went backwards while the app was away', () => {
      const { controller } = signedIn();

      controller.appStateChanged('background');
      jest.advanceTimersByTime(5_000);
      setClockBack(3_600_000);
      controller.appStateChanged('active');

      expect(controller.getState().locked).toBe(true);
    });

    it('locks on a backwards clock even without a steady clock', () => {
      const { controller } = signedIn({ timers: wallClockOnly });

      controller.appStateChanged('background');
      jest.advanceTimersByTime(5_000);
      setClockBack(3_600_000);
      controller.appStateChanged('active');

      expect(controller.getState().locked).toBe(true);
    });

    it('counts two minutes away on the steady clock when the wall clock was set back by less', () => {
      const { controller } = signedIn();

      controller.appStateChanged('background');
      jest.advanceTimersByTime(150_000);
      setClockBack(100_000);
      controller.appStateChanged('active');

      expect(controller.getState().locked).toBe(true);
    });

    it('still locks ten minutes after the last touch by the steady clock', () => {
      const { controller } = signedIn();

      jest.advanceTimersByTime(300_000);
      setClockBack(3_600_000);
      jest.advanceTimersByTime(299_000);
      expect(controller.getState().locked).toBe(false);
      jest.advanceTimersByTime(1_000);

      expect(controller.getState().locked).toBe(true);
    });

    it('does not lock someone who has just touched the screen when the clock jumps back', () => {
      const { controller } = signedIn();

      jest.advanceTimersByTime(590_000);
      controller.touched();
      setClockBack(3_600_000);
      jest.advanceTimersByTime(10_000);

      expect(controller.getState().locked).toBe(false);
    });

    it('without a steady clock, locks at the next idle check once the clock has gone backwards', () => {
      const { controller } = signedIn({ timers: wallClockOnly });

      jest.advanceTimersByTime(300_000);
      setClockBack(3_600_000);
      jest.advanceTimersByTime(300_000);

      expect(controller.getState().locked).toBe(true);
    });
  });

  describe('edge cases of the app state', () => {
    it('keeps the lock off a session unlocked while the app is away, then locks after two minutes away', async () => {
      const { controller, authentication } = controllerFor();
      controller.authStateChanged('signed_in');
      let answer: (outcome: 'success') => void = () => undefined;
      authentication.willAnswer(new Promise((resolve) => (answer = resolve)));

      const unlocking = controller.unlock(PROMPT);
      controller.appStateChanged('background');
      answer('success');
      await unlocking;
      expect(controller.getState().locked).toBe(false);
      jest.advanceTimersByTime(120_000);
      controller.appStateChanged('active');

      expect(controller.getState().locked).toBe(true);
    });

    it('unlocked while away and back within two minutes, stays unlocked and counts idle time again', async () => {
      const { controller, authentication } = controllerFor();
      controller.authStateChanged('signed_in');
      let answer: (outcome: 'success') => void = () => undefined;
      authentication.willAnswer(new Promise((resolve) => (answer = resolve)));

      const unlocking = controller.unlock(PROMPT);
      controller.appStateChanged('background');
      answer('success');
      await unlocking;
      jest.advanceTimersByTime(60_000);
      controller.appStateChanged('active');
      expect(controller.getState().locked).toBe(false);
      jest.advanceTimersByTime(600_000);

      expect(controller.getState().locked).toBe(true);
    });

    it("starts in the 'unknown' state uncovered, locks a restored session, and counts idle time only once active", () => {
      const restored = controllerFor({ appState: 'unknown' });
      restored.controller.authStateChanged('signed_in');
      expect(restored.controller.getState()).toMatchObject({ locked: true, covered: false });

      const { controller } = signedIn({ appState: 'unknown' });
      jest.advanceTimersByTime(600_000);
      expect(controller.getState().locked).toBe(false);
      controller.appStateChanged('active');
      jest.advanceTimersByTime(600_000);

      expect(controller.getState().locked).toBe(true);
    });

    it('lets sensitive content reach the screen only while unlocked and in the foreground', () => {
      const { controller } = signedIn();
      expect(controller.mayRevealContent()).toBe(true);

      controller.appStateChanged('inactive');
      expect(controller.mayRevealContent()).toBe(false);
      controller.appStateChanged('active');
      jest.advanceTimersByTime(600_000);
      expect(controller.mayRevealContent()).toBe(false);

      const unknown = signedIn({ appState: 'unknown' });
      expect(unknown.controller.mayRevealContent()).toBe(false);
    });
  });

  it('tells subscribers when the lock engages', () => {
    const { controller } = signedIn();
    const onLock = jest.fn();
    controller.onLock(onLock);

    jest.advanceTimersByTime(600_000);
    jest.advanceTimersByTime(600_000);

    expect(onLock).toHaveBeenCalledTimes(1);
  });

  describe('coming back to the same screen', () => {
    it('remembers the signed-in screen the person was on when the app locked', () => {
      const { controller } = signedIn();
      controller.noteLocation('/home');
      controller.noteLocation('/sessions/1');

      away(controller, 120_000);

      expect(controller.getState().resumeHref).toBe('/sessions/1');
    });

    it('ignores the anchor route and anything noted while locked', () => {
      const { controller } = signedIn();
      controller.noteLocation('/history');
      jest.advanceTimersByTime(600_000);

      controller.noteLocation('/');
      controller.noteLocation('/profile');

      expect(controller.getState().resumeHref).toBe('/history');
    });

    it('returns there once: the first screen noted after unlocking clears it', async () => {
      const { controller } = signedIn();
      controller.noteLocation('/sessions/1');
      away(controller, 120_000);

      await controller.unlock(PROMPT);
      expect(controller.getState().resumeHref).toBe('/sessions/1');
      controller.noteLocation('/sessions/1');

      expect(controller.getState().resumeHref).toBeNull();
    });

    it('has nowhere to return to after a cold start', () => {
      const { controller } = controllerFor();

      controller.authStateChanged('signed_in');

      expect(controller.getState().resumeHref).toBeNull();
    });
  });

  it('tells subscribers about each change until they unsubscribe', () => {
    const { controller } = signedIn();
    const listener = jest.fn();
    const unsubscribe = controller.subscribe(listener);

    controller.appStateChanged('background');
    expect(listener).toHaveBeenCalledTimes(1);
    unsubscribe();
    controller.appStateChanged('active');

    expect(listener).toHaveBeenCalledTimes(1);
  });

  it('stops its idle timer when stopped and picks the count up again when started', () => {
    const { controller } = signedIn();

    jest.advanceTimersByTime(300_000);
    controller.stop();
    jest.advanceTimersByTime(600_000);
    expect(controller.getState().locked).toBe(false);
    controller.start();
    jest.advanceTimersByTime(0);

    expect(controller.getState().locked).toBe(true);
  });
});
