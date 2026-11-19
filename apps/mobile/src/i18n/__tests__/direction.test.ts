import { planDirection } from '../direction';

describe('planDirection', () => {
  it('does nothing when the layout already matches', () => {
    expect(planDirection('en', false, null, true)).toEqual({ kind: 'ok' });
    expect(planDirection('ar', true, null, true)).toEqual({ kind: 'ok' });
  });

  it('asks for a reload the first time the direction must change', () => {
    expect(planDirection('ar', false, null, true)).toEqual({ kind: 'reload', rtl: true });
    expect(planDirection('en', true, null, true)).toEqual({ kind: 'reload', rtl: false });
  });

  it('gives up instead of reloading again when a reload for this language did not take effect', () => {
    expect(planDirection('ar', false, 'ar', true)).toEqual({ kind: 'give_up', rtl: true });
    expect(planDirection('en', true, 'en', true)).toEqual({ kind: 'give_up', rtl: false });
  });

  it('still reloads for a different language than the one last attempted', () => {
    expect(planDirection('en', true, 'ar', true)).toEqual({ kind: 'reload', rtl: false });
  });

  it('gives up straight away where the direction cannot change (Expo Go)', () => {
    expect(planDirection('ar', false, null, false)).toEqual({ kind: 'give_up', rtl: true });
    expect(planDirection('en', true, null, false)).toEqual({ kind: 'give_up', rtl: false });
    expect(planDirection('en', false, null, false)).toEqual({ kind: 'ok' });
  });
});
