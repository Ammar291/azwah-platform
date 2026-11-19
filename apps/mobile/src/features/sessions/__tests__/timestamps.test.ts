import { sameInstant } from '../timestamps';

describe('sameInstant', () => {
  it("treats a save's 100 ns time and the database's microsecond time as the same instant", () => {
    // Rounded and truncated by the database, and with either offset spelling.
    expect(sameInstant('2026-10-15T09:00:12.3456789+00:00', '2026-10-15T09:00:12.345679+00:00')).toBe(true);
    expect(sameInstant('2026-10-15T09:00:12.3456789+00:00', '2026-10-15T09:00:12.345678Z')).toBe(true);
    expect(sameInstant('2026-10-15T09:00:12.9999996Z', '2026-10-15T09:00:13Z')).toBe(true);
    expect(sameInstant('2026-10-15T13:00:12.5+04:00', '2026-10-15T09:00:12.500000Z')).toBe(true);
  });

  it('tells different instants apart, down to a microsecond', () => {
    expect(sameInstant('2026-10-15T09:00:12.345678Z', '2026-10-15T09:00:12.345680Z')).toBe(false);
    expect(sameInstant('2026-10-15T09:00:12Z', '2026-10-15T09:00:13Z')).toBe(false);
    expect(sameInstant('2026-10-15T09:00:12Z', null)).toBe(false);
    expect(sameInstant(null, null)).toBe(true);
  });

  it('never matches something that is not a timestamp, unless it is the same text', () => {
    expect(sameInstant('soon', 'soon')).toBe(true);
    expect(sameInstant('soon', '2026-10-15T09:00:12Z')).toBe(false);
  });
});
