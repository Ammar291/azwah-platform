import { brand } from './brand';

const p = brand.palette;

/**
 * Semantic colour roles. Primary (pine) is the working colour: actions, links, selection. Secondary (gold) and
 * accent (sand) are brand furniture only: never text, data or status. `border` and `borderSoft` are decorative;
 * anything a person must see to use it (inputs, options) uses `borderStrong`, which meets 3:1.
 */
export const colors = {
  background: p.paper,
  surface: p.white,
  surfaceMuted: p.parchment,
  text: p.ink,
  textSecondary: p.ink2,
  textMuted: p.ink3,
  border: p.line,
  borderSoft: p.lineSoft,
  borderStrong: p.stone,
  primary: p.pine,
  primaryStrong: p.ink,
  primaryMid: p.pineMid,
  primarySoft: p.pineSoft,
  onPrimary: p.white,
  secondary: p.gold,
  secondarySoft: p.goldSoft,
  accent: p.sand,
  accentSoft: p.sandSoft,
  scrim: 'rgba(11, 43, 31, 0.5)',
} as const;

/** Reserved for status. Always paired with text or a glyph, never colour alone. */
export const statusColors = {
  success: '#1a6b4a',
  successSoft: '#e2f0e8',
  warning: '#7a5408',
  warningSoft: '#f7edd5',
  danger: '#b13a2c',
  /** The danger button while pressed. */
  dangerStrong: '#8f2c20',
  dangerSoft: '#f8e4e1',
  info: p.pine,
  infoSoft: p.pineSoft,
} as const;

export type StatusTone = 'info' | 'success' | 'warning' | 'danger';

export const spacing = { xxs: 2, xs: 4, sm: 8, md: 12, lg: 16, xl: 24, xxl: 32, xxxl: 48 } as const;

export const radii = { sm: 6, md: 10, lg: 16, pill: 999 } as const;

export const borderWidths = { hairline: 1, strong: 2, accent: 4 } as const;

export const fontFamilies = brand.fonts;

/**
 * Line heights are generous so Arabic diacritics and descenders never clip. Markazi Text (headings) has a much smaller
 * x-height than Readex Pro (body), so its sizes run larger than a single-family scale would: a 26 heading reads about
 * as large as 19 in Readex, one clear step above body text.
 */
export const typeScale = {
  display: { fontFamily: fontFamilies.heading, fontSize: 42, lineHeight: 54 },
  title: { fontFamily: fontFamilies.heading, fontSize: 34, lineHeight: 44 },
  heading: { fontFamily: fontFamilies.heading, fontSize: 26, lineHeight: 34 },
  /** The title in the top bar: the screen's name, a step quieter than the content's own heading. */
  barTitle: { fontFamily: fontFamilies.heading, fontSize: 23, lineHeight: 30 },
  body: { fontFamily: fontFamilies.body, fontSize: 16, lineHeight: 26 },
  bodyStrong: { fontFamily: fontFamilies.bodyStrong, fontSize: 16, lineHeight: 26 },
  label: { fontFamily: fontFamilies.bodyStrong, fontSize: 14, lineHeight: 22 },
  caption: { fontFamily: fontFamilies.body, fontSize: 13, lineHeight: 20 },
} as const;

/** Buttons, options and header actions are at least this tall and wide (CONTRIBUTING.md asks 44; Android asks 48). */
export const minTouchTarget = 48;

/** Text follows the system text size up to 200%; layouts wrap rather than clip. */
export const maxFontScale = 2;

/** Content stays readable on tablets. */
export const maxContentWidth = 640;

/**
 * Pressed states change tone (see the components) rather than fading, so labels keep their contrast under the finger.
 * `pressedOpacity` remains for the few controls whose colours are fixed by someone else (the UAE PASS button).
 */
export const motion = { pressedOpacity: 0.85, disabledOpacity: 0.45, dialogFade: 'fade' } as const;
