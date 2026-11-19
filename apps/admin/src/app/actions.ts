'use server';

import { cookies } from 'next/headers';

import { isLanguage, LANGUAGE_COOKIE } from '@/lib/i18n';

export async function setLanguage(formData: FormData): Promise<void> {
  const value = formData.get('language');
  if (!isLanguage(value)) {
    return;
  }
  const cookieStore = await cookies();
  cookieStore.set(LANGUAGE_COOKIE, value, { path: '/', sameSite: 'lax', maxAge: 60 * 60 * 24 * 365 });
}
