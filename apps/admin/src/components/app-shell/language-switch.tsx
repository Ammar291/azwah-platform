'use client';

import { Languages } from 'lucide-react';

import { setLanguage } from '@/app/actions';
import { Button } from '@/components/ui/button';
import { useI18n } from '@/lib/i18n-context';

/** Switches between English and Arabic; the choice is kept in a cookie and the page re-renders in that direction. */
export function LanguageSwitch() {
  const { t, language } = useI18n();
  return (
    <form action={setLanguage}>
      <input type="hidden" name="language" value={language === 'ar' ? 'en' : 'ar'} />
      <Button type="submit" variant="ghost" size="lg" aria-label={t.language.switchToLabel}>
        <Languages aria-hidden />
        <span lang={language === 'ar' ? 'en' : 'ar'}>{t.language.switchTo}</span>
      </Button>
    </form>
  );
}
