import ar from '../resources/ar.json';
import en from '../resources/en.json';

type Tree = { [key: string]: string | Tree };

function flatten(tree: Tree, prefix = ''): Record<string, string> {
  return Object.entries(tree).reduce<Record<string, string>>((all, [key, value]) => {
    const path = prefix ? `${prefix}.${key}` : key;
    return typeof value === 'string' ? { ...all, [path]: value } : { ...all, ...flatten(value, path) };
  }, {});
}

const english = flatten(en);
const arabic = flatten(ar);
// Values that are intentionally not Arabic script on the Arabic side (language endonyms live in languages.ts because
// they are never translated). "SOS" is the international distress signal and reads the same in Arabic-speaking
// countries; its spoken label (home.actions.emergencyLabel) is Arabic.
const NOT_ARABIC_SCRIPT = new Set<string>(['home.actions.emergency', 'scoreReport.step', 'scoreReport.flow.procedureStep']);

describe('translations', () => {
  it('define exactly the same keys in Arabic and English', () => {
    expect(Object.keys(arabic).sort()).toEqual(Object.keys(english).sort());
  });

  it('have no empty strings', () => {
    for (const [key, value] of Object.entries({ ...english, ...arabic })) {
      expect([key, value.trim().length > 0]).toEqual([key, true]);
    }
  });

  it('write every Arabic string in Arabic script', () => {
    for (const [key, value] of Object.entries(arabic)) {
      if (!NOT_ARABIC_SCRIPT.has(key)) {
        expect([key, /[\u0600-\u06FF]/.test(value)]).toEqual([key, true]);
      }
    }
  });

  it('keep the same interpolation placeholders in both languages', () => {
    const placeholders = (text: string) => (text.match(/{{\s*\w+\s*}}/g) ?? []).sort();
    for (const key of Object.keys(english)) {
      expect([key, placeholders(arabic[key] ?? '')]).toEqual([key, placeholders(english[key] ?? '')]);
    }
  });
});
