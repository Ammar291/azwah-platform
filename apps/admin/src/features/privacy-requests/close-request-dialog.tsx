'use client';

import { useId, useRef, useState, type FormEvent, type ReactNode } from 'react';
import { toast } from 'sonner';
import { z } from 'zod';

import { Alert, AlertDescription } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Dialog, DialogClose, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog';
import { ApiError } from '@/lib/api/errors';
import { useClosePrivacyRequest } from '@/lib/api/queries';
import { PRIVACY_RESOLUTIONS, type AdminPrivacyRequest, type PrivacyResolution } from '@/lib/api/schemas';
import { fill } from '@/lib/format';
import { useI18n } from '@/lib/i18n-context';
import type { Messages } from '@/lib/messages';

import { shortMemberId } from './short-member-id';

const closeSchema = z.object({ resolution: z.enum(PRIVACY_RESOLUTIONS), confirmed: z.literal(true) });

/** A refusal after which this request can no longer be closed from the dialog: it is gone or already closed. */
function isFinal(error: unknown): boolean {
  return error instanceof ApiError && (error.code === 'privacy.request_already_closed' || error.status === 404);
}

/** The platform's refusal in the dashboard's own words, never its text. */
function refusal(t: Messages, error: unknown): string {
  const messages = t.privacyRequests;
  if (!(error instanceof ApiError)) return messages.notClosed;
  if (error.code === 'privacy.request_already_closed') return messages.alreadyClosed;
  // The dashboard's own refusal, not the platform's: the page is open at another address than the configured one.
  if (error.code === 'bff.cross_origin') return t.states.wrongOriginBody;
  switch (error.status) {
    case 400:
      return messages.invalid;
    case 401:
      return t.states.signedOutBody;
    case 403:
      return messages.denied;
    case 404:
      return messages.notFound;
    case 409:
      return messages.changed;
    case 429:
      return messages.rateLimited;
    default:
      return messages.notClosed;
  }
}

/**
 * Closing a request: an outcome and an explicit confirmation, then one POST through the BFF. The form unmounts when
 * the dialog closes, so every opening starts empty. A missing choice moves focus to it and is announced; once the
 * request turns out to be gone or already closed, the dialog no longer offers to close it.
 */
export function CloseRequestDialog({
  request,
  open,
  onOpenChange,
}: {
  request: AdminPrivacyRequest | null;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}) {
  return (
    <Dialog open={open} onOpenChange={(next) => onOpenChange(next)}>
      <DialogContent>{request ? <CloseRequestForm request={request} onClosed={() => onOpenChange(false)} /> : null}</DialogContent>
    </Dialog>
  );
}

function CloseRequestForm({ request, onClosed }: { request: AdminPrivacyRequest; onClosed: () => void }) {
  const { t, f } = useI18n();
  const messages = t.privacyRequests;
  const close = useClosePrivacyRequest();
  const [resolution, setResolution] = useState<PrivacyResolution | null>(null);
  const [confirmed, setConfirmed] = useState(false);
  const [errors, setErrors] = useState<{ resolution?: true; confirmed?: true }>({});
  const [failure, setFailure] = useState<string | null>(null);
  const [settled, setSettled] = useState(false);
  const ids = { outcome: useId(), confirm: useId() };
  const firstOutcome = useRef<HTMLInputElement>(null);
  const confirmation = useRef<HTMLInputElement>(null);

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (close.isPending || settled) return;
    const parsed = closeSchema.safeParse({ resolution, confirmed });
    if (!parsed.success) {
      const found: typeof errors = {};
      for (const issue of parsed.error.issues) found[issue.path[0] as keyof typeof errors] = true;
      setErrors(found);
      (found.resolution ? firstOutcome : confirmation).current?.focus();
      return;
    }
    setErrors({});
    setFailure(null);
    try {
      await close.mutateAsync({ requestId: request.id, resolution: parsed.data.resolution });
      toast.success(messages.closedToast);
      onClosed();
    } catch (error) {
      setFailure(refusal(t, error));
      setSettled(isFinal(error));
    }
  }

  const details: [string, ReactNode][] = [
    [
      messages.columns.household,
      <span key="household" dir="ltr" className="font-mono text-xs">
        {request.householdReference}
      </span>,
    ],
    [
      messages.columns.member,
      <span key="member" dir="ltr" className="font-mono text-xs" title={fill(messages.memberTitle, { id: request.memberId })}>
        {shortMemberId(request.memberId)}
      </span>,
    ],
    [messages.columns.type, messages.types[request.type]],
    [messages.columns.created, f.date(request.createdAt)],
  ];

  return (
    <form onSubmit={submit} noValidate className="flex flex-col gap-4">
      <DialogHeader>
        <DialogTitle>{messages.dialogTitle}</DialogTitle>
        <DialogDescription>{messages.dialogDescription}</DialogDescription>
      </DialogHeader>
      <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-1 rounded-md bg-muted px-3 py-2">
        {details.map(([label, value]) => (
          <div key={label} className="contents">
            <dt className="text-muted-foreground">{label}</dt>
            <dd className="min-w-0">{value}</dd>
          </div>
        ))}
      </dl>
      <fieldset className="flex flex-col gap-2" aria-describedby={errors.resolution ? `${ids.outcome}-error` : undefined}>
        <legend className="mb-1 text-sm font-medium">{messages.outcome}</legend>
        {errors.resolution ? (
          <p id={`${ids.outcome}-error`} role="alert" className="text-sm text-status-danger">
            {messages.outcomeRequired}
          </p>
        ) : null}
        {PRIVACY_RESOLUTIONS.map((value, index) => {
          const optionId = `${ids.outcome}-${value}`;
          return (
            <div
              key={value}
              className="relative flex min-h-11 items-start gap-2.5 rounded-md border border-border px-3 py-2 has-checked:border-primary has-checked:bg-accent"
            >
              <input
                ref={index === 0 ? firstOutcome : undefined}
                id={optionId}
                type="radio"
                name={ids.outcome}
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
      <div className="flex flex-col gap-1">
        <label className="flex min-h-11 items-start gap-2.5 text-sm">
          <input
            ref={confirmation}
            type="checkbox"
            checked={confirmed}
            aria-invalid={errors.confirmed ? true : undefined}
            aria-describedby={errors.confirmed ? `${ids.confirm}-error` : undefined}
            onChange={(event) => setConfirmed(event.target.checked)}
            className="mt-0.5 size-4 shrink-0 accent-[var(--primary)]"
          />
          <span>{messages.confirm}</span>
        </label>
        {errors.confirmed ? (
          <p id={`${ids.confirm}-error`} role="alert" className="text-sm text-status-danger">
            {messages.confirmRequired}
          </p>
        ) : null}
      </div>
      {failure ? (
        <Alert variant="destructive">
          <AlertDescription>{failure}</AlertDescription>
        </Alert>
      ) : null}
      <DialogFooter>
        <DialogClose render={<Button type="button" variant="outline" size="lg" />}>{messages.cancel}</DialogClose>
        <Button type="submit" size="lg" disabled={close.isPending || settled}>
          {close.isPending ? messages.submitting : messages.submit}
        </Button>
      </DialogFooter>
    </form>
  );
}
