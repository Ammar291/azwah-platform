import { roundOf } from '../roundLabel';

describe('roundOf', () => {
  it('gives the number of a wave code that numbers itself', () => {
    expect(roundOf('dev-wave-2')).toBe('2');
    expect(roundOf('dev_synthetic_wave_1')).toBe('1');
    expect(roundOf('wave_03')).toBe('3');
    expect(roundOf('Wave12')).toBe('12');
  });

  it('shows any other code as recorded, and never makes up a number', () => {
    expect(roundOf('2026_autumn')).toBe('2026_autumn');
    expect(roundOf('microwave_2')).toBe('microwave_2');
    expect(roundOf('wave_2_pilot')).toBe('wave_2_pilot');
    expect(roundOf('')).toBe('');
  });
});
