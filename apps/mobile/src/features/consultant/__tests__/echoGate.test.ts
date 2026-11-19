import { createEchoGate, type EchoGate } from '../echoGate';

/** 100 ms of a steady tone at this RMS, at 24 kHz. */
const tone = (rms: number) => new Int16Array(2400).fill(rms);

/** The reply plays at `reply` RMS while the microphone hears `microphone`; one 100 ms buffer. */
const step = (gate: EchoGate, reply: number, microphone: number, playing = true) => {
  if (playing) gate.played(tone(reply));
  return gate.decide({ level: microphone, playing, durationMs: 100 });
};

/** A second of the reply with only its echo in the microphone, at the given share of its loudness. */
const echoOnly = (gate: EchoGate, reply: number, share: number, buffers = 10) =>
  Array.from({ length: buffers }, () => step(gate, reply, reply * share));

describe('the echo gate (fallback without echo cancellation, D-125)', () => {
  it('passes the microphone through while nothing plays', () => {
    const gate = createEchoGate();

    expect([0, 900, 20000].map((microphone) => step(gate, 0, microphone, false))).toEqual([
      'microphone',
      'microphone',
      'microphone',
    ]);
  });

  it("never mistakes the reply's own echo for the person, however loud the reply", () => {
    const gate = createEchoGate();

    const decisions = [...echoOnly(gate, 8000, 0.4, 30), ...echoOnly(gate, 12000, 0.4, 30)];

    expect(new Set(decisions)).toEqual(new Set(['silence']));
  });

  it('lets the person talk over the reply once their voice is clearly louder than the echo, three buffers in a row', () => {
    const gate = createEchoGate();
    echoOnly(gate, 8000, 0.3);

    expect([step(gate, 8000, 12000), step(gate, 8000, 12000), step(gate, 8000, 12000)]).toEqual([
      'silence',
      'silence',
      'barge_in',
    ]);
    // The reply is stopped by the caller: the person is heard from now on.
    expect(step(gate, 0, 12000, false)).toBe('microphone');
  });

  it('is cautious before it has learned how much of the speaker this phone hears', () => {
    const gate = createEchoGate();

    expect([step(gate, 8000, 12000), step(gate, 8000, 12000), step(gate, 8000, 12000)]).toEqual([
      'silence',
      'silence',
      'silence',
    ]);
  });

  it('needs the loud buffers in a row', () => {
    const gate = createEchoGate();
    echoOnly(gate, 8000, 0.3);

    const decisions = [12000, 12000, 2400, 12000, 12000].map((microphone) => step(gate, 8000, microphone));

    expect(decisions).not.toContain('barge_in');
  });

  it('never barges in below the floor, even over a quiet reply', () => {
    const gate = createEchoGate();
    echoOnly(gate, 600, 0.5);

    expect([2000, 2000, 2000].map((microphone) => step(gate, 600, microphone))).not.toContain('barge_in');
    expect([3000, 3000, 3000].map((microphone) => step(gate, 600, microphone))).toEqual(['silence', 'silence', 'barge_in']);
  });

  it('keeps sending silence for the echo tail and the gaps between segments, then the microphone again', () => {
    const gate = createEchoGate();
    echoOnly(gate, 8000, 0.4, 5);

    const afterwards = Array.from({ length: 5 }, () => step(gate, 0, 3200, false));

    expect(afterwards).toEqual(['silence', 'silence', 'silence', 'microphone', 'microphone']);
  });

  it('forgets what it learned for a new call', () => {
    const gate = createEchoGate();
    echoOnly(gate, 8000, 0.3);
    gate.reset();

    expect([step(gate, 8000, 12000), step(gate, 8000, 12000), step(gate, 8000, 12000)]).not.toContain('barge_in');
  });
});
