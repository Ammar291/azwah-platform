import { cloneElement, isValidElement, type ComponentProps, type ReactElement, type ReactNode } from 'react';

import { cn } from '@/lib/utils';

/**
 * A labelled filter or form control, with an optional hint or error underneath. The hint, or the error while there is
 * one, becomes the control's accessible description, so screen readers announce it with the control.
 */
export function Field({
  id,
  label,
  hint,
  error,
  children,
  className,
}: {
  id: string;
  label: string;
  hint?: ReactNode;
  error?: string | null;
  children: ReactNode;
  className?: string;
}) {
  const describedBy = error ? `${id}-error` : hint ? `${id}-hint` : undefined;
  const control =
    describedBy && isValidElement(children)
      ? cloneElement(children as ReactElement<{ 'aria-describedby'?: string }>, { 'aria-describedby': describedBy })
      : children;
  return (
    <div className={cn('flex min-w-0 flex-col gap-1.5', className)}>
      <label htmlFor={id} className="text-sm font-medium">
        {label}
      </label>
      {control}
      {error ? (
        <p id={`${id}-error`} className="text-sm text-status-danger">
          {error}
        </p>
      ) : hint ? (
        <p id={`${id}-hint`} className="text-xs text-muted-foreground">
          {hint}
        </p>
      ) : null}
    </div>
  );
}

const controlClasses =
  'h-9 w-full min-w-0 rounded-md border border-input bg-card px-2.5 text-sm text-foreground outline-none transition-colors focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/40 disabled:cursor-not-allowed disabled:opacity-50 aria-invalid:border-status-danger';

/** A native select: keyboard, screen-reader and right-to-left behaviour come from the browser. */
export function NativeSelect({ className, ...props }: ComponentProps<'select'>) {
  return <select className={cn(controlClasses, 'pe-8', className)} {...props} />;
}

export function NativeInput({ className, ...props }: ComponentProps<'input'>) {
  return <input className={cn(controlClasses, className)} {...props} />;
}
