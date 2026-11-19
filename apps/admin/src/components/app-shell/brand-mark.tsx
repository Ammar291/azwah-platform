'use client';

import { useI18n } from '@/lib/i18n-context';
import { cn } from '@/lib/utils';

/**
 * The product name, set in the brand's heading face. No emblem: the dashboard uses the name only. `compact` is the
 * phone top bar's version: a smaller name without the tagline, so the bar keeps to one or two short lines.
 */
export function BrandMark({ className, compact = false }: { className?: string; compact?: boolean }) {
  const { t } = useI18n();
  return (
    <span className={cn('flex flex-col leading-tight', className)}>
      <span className={cn('font-heading font-semibold', compact ? 'text-xl leading-tight' : 'text-2xl')}>{t.app.name}</span>
      {compact ? null : <span className="text-xs opacity-80">{t.app.tagline}</span>}
    </span>
  );
}
