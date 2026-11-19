'use client';

import { useId, useRef, useState, type FormEvent } from 'react';
import { toast } from 'sonner';
import { z } from 'zod';

import { Alert, AlertDescription } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Dialog, DialogClose, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog';
import { useResolveSafetyAlert } from '@/lib/api/queries';
import { SAFETY_RESOLUTIONS, type AdminSafetyAlert, type SafetyResolution } from '@/lib/api/schemas';
import { useI18n } from '@/lib/i18n-context';

import { alertRefusal, isStale } from './safety-alert-labels';

const resolveSchema = z.object({ resolution: z.enum(SAFETY_RESOLUTIONS) });

/**
 * Resolving an alert: one outcome, then one POST through the BFF. The form unmounts when the dialog closes, so every
 * opening starts empty. Once the alert turns out to be gone or already resolved, the dialog stops offering to resolve it.
 */
export function ResolveAlertDialog({
  alert,
  open,
  onOpenChange,
}: {
  alert: AdminSafetyAlert | null;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}) {
  return (
    <Dialog open={open} onOpenChange={(next) => onOpenChange(next)}>
      <DialogContent>{alert ? <ResolveAlertForm alert={alert} onResolved={() => onOpenChange(false)} /> : null}</DialogContent>
    </Dialog>
  );
}

function ResolveAlertForm({ alert, onResolved }: { alert: AdminSafetyAlert; onResolved: () => void }) {
  const { t } = useI18n();
  const messages = t.safetyAlerts;
  const resolve = useResolveSafetyAlert();
  const [resolution, setResolution] = useState<SafetyResolution | null>(null);
  const [missing, setMissing] = useState(false);
  const [failure, setFailure] = useState<string | null>(null);
  const [settled, setSettled] = useState(false);
  const outcomeId = useId();
  const firstOutcome = useRef<HTMLInputElement>(null);

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (resolve.isPending || settled) return;
    const parsed = resolveSchema.safeParse({ resolution });
    if (!parsed.success) {
      setMissing(true);
      firstOutcome.current?.focus();
      return;
    }
    setMissing(false);
    setFailure(null);
    try {
      await resolve.mutateAsync({ alertId: alert.id, resolution: parsed.data.resolution });
      toast.success(messages.resolvedToast);
      onResolved();
    } catch (error) {
      setFailure(alertRefusal(t, error, messages.notResolved));
      setSettled(isStale(error));
    }
  }

  const concern = messages.concerns[alert.category];
  return (
    <form onSubmit={submit} noValidate className="flex flex-col gap-4">
      <DialogHeader>
        <DialogTitle>{messages.dialogTitle}</DialogTitle>
        <DialogDescription>{messages.dialogDescription}</DialogDescription>
      </DialogHeader>
      <p className="rounded-md bg-muted px-3 py-2 text-sm">
        {concern}
        {alert.householdReference ? (
          <>
            {' · '}
            <span dir="ltr" className="font-mono text-xs">
              {alert.householdReference}
            </span>
          </>
        ) : null}
      </p>
      <fieldset className="flex flex-col gap-2" aria-describedby={missing ? `${outcomeId}-error` : undefined}>
        <legend className="mb-1 text-sm font-medium">{messages.outcome}</legend>
        {missing ? (
          <p id={`${outcomeId}-error`} role="alert" className="text-sm text-status-danger">
            {messages.outcomeRequired}
          </p>
        ) : null}
        {SAFETY_RESOLUTIONS.map((value, index) => {
          const optionId = `${outcomeId}-${value}`;
          return (
            <div
              key={value}
              className="relative flex min-h-11 items-start gap-2.5 rounded-md border border-border px-3 py-2 has-checked:border-primary has-checked:bg-accent"
            >
              <input
                ref={index === 0 ? firstOutcome : undefined}
                id={optionId}
                type="radio"
                name={outcomeId}
                value={value}
                checked={resolution === value}
                aria-describedby={`${optionId}-hint`}
                onChange={() => setResolution(value)}
                className="mt-0.5 size-4 shrink-0 accent-[var(--primary)]"
              />
              <div className="flex flex-col gap-0.5">
                <label htmlFor={optionId} className="text-sm font-medium after:absolute after:inset-0">
                  {messages.resolutions[value]}
                </label>
                <p id={`${optionId}-hint`} className="text-xs text-muted-foreground">
                  {messages.resolutionHints[value]}
                </p>
              </div>
            </div>
          );
        })}
      </fieldset>
      {failure ? (
        <Alert variant="destructive">
          <AlertDescription>{failure}</AlertDescription>
        </Alert>
      ) : null}
      <DialogFooter>
        <DialogClose render={<Button type="button" variant="outline" size="lg" />}>{messages.cancel}</DialogClose>
        <Button type="submit" size="lg" disabled={resolve.isPending || settled}>
          {resolve.isPending ? messages.submitting : messages.submit}
        </Button>
      </DialogFooter>
    </form>
  );
}
