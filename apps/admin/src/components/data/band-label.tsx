'use client';

import { bandLabel, isKnownBand } from '@/lib/bands';
import { useI18n } from '@/lib/i18n-context';

/** The band's name from the specification for the codes it names; any other code exactly as recorded; none a dash. */
export function BandLabel({ code }: { code: string | null }) {
  const { t } = useI18n();
  if (code === null) return <>{t.common.none}</>;
  if (isKnownBand(code)) return <>{bandLabel(t, code)}</>;
  return (
    <span className="font-mono text-xs" dir="ltr">
      {code}
    </span>
  );
}
