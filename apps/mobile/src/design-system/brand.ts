/**
 * The replaceable brand. Everything specific to Azwah's look lives here: to rebrand, change this file, the assets it
 * names, the fonts in fonts.ts and the splash and icon entries in app.json. Components never read the palette
 * directly; they use the roles in tokens.ts.
 *
 * Colours come from the Azwah logo sheet (the same values as the earlier investor demo). The emblem is the
 * Azwah mark only: no government logo, crest or endorsement.
 */
export const brand = {
  /** Translation key of the product name, so it reads in both languages. */
  nameKey: 'app.name',
  // Static images use require() (Expo's convention; SDK 57 ships no *.png module declarations).
  emblem: require('../../assets/brand/azwah-emblem.png') as number,
  emblemAspectRatio: 160 / 142,
  /**
   * The four commitments at the base of the logo sheet, in its order. Labels are translation keys under `pillars`;
   * the artwork is decorative (the label carries the meaning).
   */
  pillars: [
    { key: 'relationships', image: require('../../assets/brand/pillars/relationships.png') as number, aspectRatio: 133 / 95 },
    { key: 'care', image: require('../../assets/brand/pillars/care.png') as number, aspectRatio: 152 / 70 },
    { key: 'empowerment', image: require('../../assets/brand/pillars/empowerment.png') as number, aspectRatio: 125 / 105 },
    { key: 'hope', image: require('../../assets/brand/pillars/hope.png') as number, aspectRatio: 103 / 93 },
  ],
  palette: {
    paper: '#faf8f3',
    white: '#ffffff',
    parchment: '#f5f3ec',
    ink: '#0b2b1f',
    ink2: '#3e4c45',
    ink3: '#5e6a63',
    stone: '#7d8781',
    line: '#ddd9cd',
    lineSoft: '#eae7de',
    pine: '#114332',
    pineMid: '#4f7e6a',
    pineSoft: '#e3ece6',
    gold: '#b8842c',
    goldSoft: '#f5ebd8',
    sand: '#cba773',
    sandSoft: '#f3ebdd',
  },
  /** Font names as registered in fonts.ts. Markazi Text and Readex Pro both cover Arabic and Latin. */
  fonts: {
    heading: 'MarkaziText_600SemiBold',
    body: 'ReadexPro_400Regular',
    bodyStrong: 'ReadexPro_600SemiBold',
  },
} as const;
