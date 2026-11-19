/**
 * The round as people read it in "Round {{wave}}". A wave code that numbers itself ("dev_wave_2", "dev-wave-2") gives
 * that number, so the screens say "Round 2"; any other code is shown as recorded. Display only: nothing is decided
 * from it, and no number is made up for a code that carries none.
 */
export function roundOf(waveCode: string): string {
  const number = /(?:^|[-_])wave[-_]?(\d{1,3})$/i.exec(waveCode.trim())?.[1];
  return number === undefined ? waveCode : String(Number(number));
}
