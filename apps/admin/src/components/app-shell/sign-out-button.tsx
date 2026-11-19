'use client';

import { LogOut } from 'lucide-react';

import { Button } from '@/components/ui/button';
import { useI18n } from '@/lib/i18n-context';

/** A same-origin form post, so the server can check where the request came from before ending the session. */
export function SignOutButton() {
  const { t } = useI18n();
  return (
    <form action="/auth/sign-out" method="post">
      <Button type="submit" variant="outline" size="lg">
        <LogOut aria-hidden className="rtl:rotate-180" />
        {t.shell.signOut}
      </Button>
    </form>
  );
}
