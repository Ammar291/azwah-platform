import type { ReactNode } from 'react';

import { cn } from '@/lib/utils';

/** One labelled number from the platform, in the sans with proportional figures (tabular digits are for columns). */
export function FigureStat({
  label,
  value,
  detail,
  emphasis = 'normal',
  className,
}: {
  label: string;
  value: ReactNode;
  detail?: ReactNode;
  emphasis?: 'primary' | 'normal' | 'quiet';
  className?: string;
}) {
  return (
    <div className={cn('flex min-w-0 flex-col gap-1', className)}>
      <p className="text-sm text-muted-foreground">{label}</p>
      <p
        className={cn(
          'leading-none font-semibold text-foreground',
          emphasis === 'primary' && 'text-4xl text-primary sm:text-5xl',
          emphasis === 'normal' && 'text-3xl',
          emphasis === 'quiet' && 'text-xl',
        )}
      >
        {value}
      </p>
      {detail ? <p className="text-xs text-muted-foreground">{detail}</p> : null}
    </div>
  );
}
