/**
 * Azwah's own symbols for the organisations in the knowledge graph (D-115). The owner asked for these instead of official
 * logos, so each is an original drawing that says what the organisation does and borrows nothing from its branding: every
 * mark has the same pine tile and the same sand line, so they read as one Azwah set, not as anyone's emblem. The drawings
 * are plain SVG text because both the screen and the report's PDF draw them.
 */

const TILE = '#114332';
const INK = '#F5F3EC';
const ACCENT = '#C9A24B';

const stroke = `fill="none" stroke="${INK}" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"`;
const accentStroke = `fill="none" stroke="${ACCENT}" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"`;

const GLYPHS: Readonly<Record<string, string>> = {
  // A roof sheltering two people.
  family_care_authority: `<path d="M14 31 32 16l18 15" ${stroke}/><circle cx="25" cy="33" r="3.5" fill="${ACCENT}"/><circle cx="39" cy="33" r="3.5" fill="${ACCENT}"/><path d="M19 47c0-5 3-7 6-7s6 2 6 7M33 47c0-5 3-7 6-7s6 2 6 7" ${stroke}/>`,
  // A person lifted by a rising arc.
  zayed_authority: `<circle cx="32" cy="19" r="5" fill="${ACCENT}"/><path d="M32 26v11M24 31h16" ${stroke}/><path d="M15 36a17 17 0 0 0 34 0" ${stroke}/>`,
  // A phone with a pulse across it.
  sahatna: `<rect x="21" y="12" width="22" height="40" rx="4" ${stroke}/><path d="M25 32h4l2-5 3 10 2-5h3" ${accentStroke}/><path d="M29 46h6" ${stroke}/>`,
  // A heart on a pulse line.
  seha: `<path d="M32 47S15 37 15 25a8.5 8.5 0 0 1 17-2 8.5 8.5 0 0 1 17 2c0 12-17 22-17 22Z" ${stroke}/><path d="M20 30h7l3-6 4 11 3-5h7" ${accentStroke}/>`,
  // Paths meeting at one point.
  tamm: `<circle cx="32" cy="32" r="5" fill="${ACCENT}"/><path d="M32 27V15M32 37v12M27 32H15M37 32h12" ${stroke}/><circle cx="32" cy="13" r="2.5" fill="${INK}"/><circle cx="32" cy="51" r="2.5" fill="${INK}"/><circle cx="13" cy="32" r="2.5" fill="${INK}"/><circle cx="51" cy="32" r="2.5" fill="${INK}"/>`,
  // Three linked rings.
  community_development_authority: `<circle cx="32" cy="23" r="9" ${stroke}/><circle cx="23" cy="39" r="9" ${stroke}/><circle cx="41" cy="39" r="9" ${accentStroke}/>`,
  // Rising bars above a line of people.
  sharjah_statistics_community_development: `<path d="M20 34v-7M32 34V18M44 34v-12" ${stroke}/><path d="M14 39h36" ${accentStroke}/><circle cx="20" cy="46" r="2.5" fill="${INK}"/><circle cx="32" cy="46" r="2.5" fill="${INK}"/><circle cx="44" cy="46" r="2.5" fill="${INK}"/>`,
  // A sprout growing from a cupped line.
  family_development_foundation: `<path d="M32 42V24" ${stroke}/><path d="M32 30c-8 0-12-4-12-11 8 0 12 4 12 11Z" ${accentStroke}/><path d="M32 25c0-6 4-10 11-10 0 7-4 10-11 10Z" ${stroke}/><path d="M17 41c3 7 9 10 15 10s12-3 15-10" ${stroke}/>`,
  // A hinged joint between two bones.
  tarmeem: `<path d="M22 13v14" ${stroke}/><path d="M39 51 30 38" ${stroke}/><circle cx="26" cy="32" r="6" ${accentStroke}/><path d="M16 13h12M33 51h12" ${stroke}/>`,
  // A larger arc holding a smaller one.
  supreme_council_motherhood_childhood: `<path d="M16 42a17 17 0 1 1 32 0" ${stroke}/><path d="M24 42a9 9 0 1 1 16 0" ${accentStroke}/><path d="M16 47h32" ${stroke}/>`,
  // An open book with a star above it.
  national_academy_childhood_development: `<path d="M32 28c-5-4-11-4-17-3v21c6-1 12-1 17 3 5-4 11-4 17-3V25c-6-1-12-1-17 3Z" ${stroke}/><path d="M32 28v21" ${stroke}/><path d="m32 11 1.8 4.2 4.2 1.8-4.2 1.8L32 23l-1.8-4.2-4.2-1.8 4.2-1.8Z" fill="${ACCENT}"/>`,
  // A head with waves of thought.
  acpn: `<path d="M21 49v-7c-4-3-6-7-6-12 0-9 7-16 16-16s16 7 16 16c0 3-1 5-2 7l3 6h-5v4c0 2-1 3-3 3h-6v-1" ${stroke}/><path d="M24 26c2-3 4-3 6 0s4 3 6 0" ${accentStroke}/><path d="M24 34c2-3 4-3 6 0s4 3 6 0" ${accentStroke}/>`,
  // A map pin holding a small building.
  municipal_presence_centres: `<path d="M32 52S16 37 16 26a16 16 0 0 1 32 0c0 11-16 26-16 26Z" ${stroke}/><path d="M25 33v-9l7-4 7 4v9Z" ${accentStroke}/>`,
};

/** An organisation added to the graph before it has a drawing still gets a plain tile, not a borrowed logo. */
const FALLBACK = `<circle cx="32" cy="32" r="12" ${stroke}/>`;

/** Whether this organisation has its own drawing. */
export function hasAuthorityMark(code: string): boolean {
  return Object.hasOwn(GLYPHS, code);
}

/** The whole mark as SVG text. Only codes from this file reach the markup, never text from outside the app. */
export function authorityMarkSvg(code: string, size = 64): string {
  const glyph = Object.hasOwn(GLYPHS, code) ? GLYPHS[code] : FALLBACK;
  return `<svg xmlns="http://www.w3.org/2000/svg" width="${size}" height="${size}" viewBox="0 0 64 64"><rect width="64" height="64" rx="14" fill="${TILE}"/>${glyph}</svg>`;
}
