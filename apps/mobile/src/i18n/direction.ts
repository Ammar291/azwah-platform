import { isRtlLanguage, type Language } from './languages';

export type DirectionPlan =
  | { kind: 'ok' }
  | { kind: 'reload'; rtl: boolean }
  | { kind: 'give_up'; rtl: boolean };

/**
 * React Native applies a new layout direction only after the app reloads. `attemptedFor` is the language we last
 * reloaded for: if the direction is still wrong for that same language, reloading again would loop forever, so we
 * give up and carry on in the current direction. Where the direction cannot change at all (`canChange` false: Expo
 * Go), we give up straight away.
 */
export function planDirection(
  language: Language,
  currentlyRtl: boolean,
  attemptedFor: Language | null,
  canChange: boolean,
): DirectionPlan {
  const rtl = isRtlLanguage(language);
  if (rtl === currentlyRtl) {
    return { kind: 'ok' };
  }
  if (!canChange || attemptedFor === language) {
    return { kind: 'give_up', rtl };
  }
  return { kind: 'reload', rtl };
}
