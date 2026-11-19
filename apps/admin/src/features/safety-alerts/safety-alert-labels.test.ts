import { describe, expect, it } from 'vitest';

import { messages } from '@/lib/i18n';

import { attentionLabel } from './safety-alert-labels';

describe('attentionLabel', () => {
  it.each([
    [1, '1 safety alert needs attention'],
    [3, '3 safety alerts need attention'],
  ])('says %i in English', (count, expected) => {
    expect(attentionLabel(messages.en, 'en', count, String(count))).toBe(expected);
  });

  it.each([
    [1, 'تنبيه سلامة واحد يحتاج إلى متابعة'],
    [2, 'تنبيهان للسلامة يحتاجان إلى متابعة'],
    [3, '3 تنبيهات سلامة تحتاج إلى متابعة'],
    [11, '11 تنبيه سلامة يحتاج إلى متابعة'],
    [100, '100 تنبيه سلامة يحتاج إلى متابعة'],
  ])('says %i in Arabic with the right plural form', (count, expected) => {
    expect(attentionLabel(messages.ar, 'ar', count, String(count))).toBe(expected);
  });
});
