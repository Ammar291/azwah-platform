import { describe, expect, it } from 'vitest';

import { ar } from './messages/ar';
import { en } from './messages/en';
import { plural } from './plural';

describe('plural', () => {
  it('uses the singular for one and the plural otherwise in English', () => {
    expect(plural('en', en.configuration.sectionCount, 1, '1')).toBe('1 section');
    expect(plural('en', en.configuration.sectionCount, 7, '7')).toBe('7 sections');
    expect(plural('en', en.configuration.itemCount, 0, '0')).toBe('0 items');
    expect(plural('en', en.exports.fieldCount, 1, '1')).toBe('1 field');
    expect(plural('en', en.exports.fieldCount, 12, '12')).toBe('12 fields');
  });

  it('uses the Arabic form for one, two, three to ten, eleven and over, and hundreds', () => {
    const forms = ar.configuration.itemCount;

    expect(plural('ar', forms, 1, '1')).toBe('بند واحد');
    expect(plural('ar', forms, 2, '2')).toBe('بندان');
    expect(plural('ar', forms, 4, '4')).toBe('4 بنود');
    expect(plural('ar', forms, 10, '10')).toBe('10 بنود');
    expect(plural('ar', forms, 13, '13')).toBe('13 بندًا');
    expect(plural('ar', forms, 39, '39')).toBe('39 بندًا');
    expect(plural('ar', forms, 100, '100')).toBe('100 بند');
    expect(plural('ar', ar.configuration.sectionCount, 7, '7')).toBe('7 أقسام');
    expect(plural('ar', ar.exports.fieldCount, 2, '2')).toBe('حقلان');
  });
});
