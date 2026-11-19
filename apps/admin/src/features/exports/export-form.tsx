'use client';

import { useId, useRef, useState, type FormEvent } from 'react';
import { toast } from 'sonner';
import { z } from 'zod';

import { Field, NativeInput, NativeSelect } from '@/components/data/field';
import { ErrorState } from '@/components/states/error-state';
import { Alert, AlertDescription } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { WaveSelect } from '@/features/filters/wave-select';
import { ApiError } from '@/lib/api/errors';
import { useCreateExport, useExportCatalog, type ExportRequestBody } from '@/lib/api/queries';
import type { ExportCatalog } from '@/lib/api/schemas';
import { useI18n } from '@/lib/i18n-context';
import type { Messages } from '@/lib/messages';

const SNAKE = /^[a-z][a-z0-9]*(_[a-z0-9]+)*$/;

type FieldError = 'datasetRequired' | 'columnsRequired' | 'instrumentCodeInvalid';

function requestSchema(catalog: ExportCatalog) {
  return z.object({
    dataset: z.string().refine((code) => catalog.datasets.some((dataset) => dataset.code === code), { message: 'datasetRequired' }),
    columns: z.array(z.string()).min(1, { message: 'columnsRequired' }),
    waveId: z.string().nullable(),
    instrumentCode: z
      .string()
      .transform((value) => value.trim())
      .refine((value) => value === '' || SNAKE.test(value), { message: 'instrumentCodeInvalid' }),
  });
}

/** The platform's own words for a refused request, never its text. */
function refusal(t: Messages, error: unknown): string {
  // The dashboard's own refusal: the page is open at another address than the one it is configured for.
  if (error instanceof ApiError && error.code === 'bff.cross_origin') return t.states.wrongOriginBody;
  if (error instanceof ApiError && error.code === 'analytics.export_storage_not_configured') return t.exports.storageNotConfigured;
  if (error instanceof ApiError && error.code === 'requests.idempotency_key_reused') return t.exports.keyReused;
  if (error instanceof ApiError && error.code === 'analytics.export_limit_reached') return t.exports.limitReached;
  if (error instanceof ApiError && error.code === 'analytics.export_daily_limit_reached') return t.exports.dailyLimitReached;
  if (error instanceof ApiError && error.status === 429) return t.exports.rateLimited;
  if (error instanceof ApiError && error.status === 400) return t.exports.invalidRequest;
  return t.exports.notRequested;
}

export function columnLabel(t: Messages, code: string): string {
  return (t.exports.columnLabels as Record<string, string>)[code] ?? code;
}

export function datasetLabel(t: Messages, code: string): string {
  return (t.exports.datasets as Record<string, string>)[code] ?? code;
}

/**
 * The export request: dataset, fields and filters from the platform's catalog only, none preselected. One
 * Idempotency-Key per request: kept while the same request is retried after a failure, new once one succeeds.
 */
export function ExportForm() {
  const { t } = useI18n();
  const catalog = useExportCatalog();
  const create = useCreateExport();
  const [dataset, setDataset] = useState('');
  const [columns, setColumns] = useState<string[]>([]);
  const [waveId, setWaveId] = useState<string | null>(null);
  const [instrumentCode, setInstrumentCode] = useState('');
  const [errors, setErrors] = useState<Partial<Record<'dataset' | 'columns' | 'instrumentCode', FieldError>>>({});
  const [failure, setFailure] = useState<string | null>(null);
  const attempt = useRef<{ key: string; fingerprint: string } | null>(null);
  const ids = { dataset: useId(), wave: useId(), code: useId(), columns: useId() };

  if (catalog.isError) {
    return (
      <div className="flex flex-col gap-3">
        <Alert>
          <AlertDescription>{t.exports.catalogUnavailable}</AlertDescription>
        </Alert>
        <ErrorState error={catalog.error} onRetry={() => void catalog.refetch()} />
      </div>
    );
  }
  if (!catalog.data) return <Skeleton role="status" aria-label={t.common.loading} className="h-56" />;

  const catalogData = catalog.data;
  const offered = catalogData.datasets.find((candidate) => candidate.code === dataset)?.columns ?? [];

  function chooseDataset(code: string) {
    setDataset(code);
    setColumns([]);
  }

  function toggle(column: string, checked: boolean) {
    setColumns((current) => (checked ? [...current, column] : current.filter((candidate) => candidate !== column)));
  }

  async function submit(event: FormEvent) {
    event.preventDefault();
    const parsed = requestSchema(catalogData).safeParse({ dataset, columns, waveId, instrumentCode });
    if (!parsed.success) {
      const found: typeof errors = {};
      for (const issue of parsed.error.issues) {
        const field = issue.path[0] as keyof typeof errors;
        found[field] ??= issue.message as FieldError;
      }
      setErrors(found);
      return;
    }
    setErrors({});
    setFailure(null);
    const ordered = offered.filter((column) => parsed.data.columns.includes(column));
    const body: ExportRequestBody = {
      dataset: parsed.data.dataset,
      columns: ordered,
      filters: { waveId: parsed.data.waveId, instrumentCode: parsed.data.instrumentCode || null },
    };
    const fingerprint = JSON.stringify(body);
    if (!attempt.current || attempt.current.fingerprint !== fingerprint) {
      attempt.current = { key: crypto.randomUUID(), fingerprint };
    }
    try {
      await create.mutateAsync({ body, idempotencyKey: attempt.current.key });
      attempt.current = null;
      setDataset('');
      setColumns([]);
      setWaveId(null);
      setInstrumentCode('');
      toast.success(t.exports.requested);
    } catch (error) {
      const message = refusal(t, error);
      setFailure(message);
      toast.error(message);
    }
  }

  const fieldErrors = (field: keyof typeof errors) => (errors[field] ? t.exports[errors[field]] : null);

  return (
    <form onSubmit={submit} noValidate aria-describedby={`${ids.columns}-note`} className="flex flex-col gap-5">
      <p id={`${ids.columns}-note`} className="max-w-prose text-sm text-muted-foreground">
        {t.exports.requestNote}
      </p>
      <div className="grid gap-4 sm:grid-cols-3">
        <Field id={ids.dataset} label={t.exports.dataset} error={fieldErrors('dataset')}>
          <NativeSelect id={ids.dataset} value={dataset} aria-invalid={errors.dataset ? true : undefined} onChange={(event) => chooseDataset(event.target.value)}>
            <option value="">{t.exports.chooseDataset}</option>
            {catalogData.datasets.map((candidate) => (
              <option key={candidate.code} value={candidate.code}>
                {datasetLabel(t, candidate.code)}
              </option>
            ))}
          </NativeSelect>
        </Field>
        <WaveSelect id={ids.wave} value={waveId} onChange={setWaveId} />
        <Field id={ids.code} label={t.exports.instrumentCode} hint={t.exports.instrumentCodeHint} error={fieldErrors('instrumentCode')}>
          <NativeInput
            id={ids.code}
            dir="ltr"
            autoComplete="off"
            spellCheck={false}
            value={instrumentCode}
            aria-invalid={errors.instrumentCode ? true : undefined}
            onChange={(event) => setInstrumentCode(event.target.value)}
          />
        </Field>
      </div>
      {dataset ? (
        <fieldset className="flex flex-col gap-2" aria-describedby={`${ids.columns}-hint`}>
          <legend className="text-sm font-medium">{t.exports.columns}</legend>
          {errors.columns ? (
            <p id={`${ids.columns}-hint`} className="text-sm text-status-danger">
              {t.exports.columnsRequired}
            </p>
          ) : (
            <p id={`${ids.columns}-hint`} className="text-xs text-muted-foreground">
              {t.exports.columnsHint}
            </p>
          )}
          <div className="grid gap-x-6 gap-y-1 sm:grid-cols-2 lg:grid-cols-3">
            {offered.map((column) => (
              <label key={column} className="flex min-h-10 items-start gap-2.5 rounded-md px-1 py-1.5 hover:bg-muted">
                <input
                  type="checkbox"
                  value={column}
                  checked={columns.includes(column)}
                  onChange={(event) => toggle(column, event.target.checked)}
                  className="mt-0.5 size-4 accent-[var(--primary)]"
                />
                <span className="flex flex-col">
                  <span className="text-sm">{columnLabel(t, column)}</span>
                  {/* self-start: an LTR flex item would otherwise line up on the left under an Arabic label. */}
                  <span className="self-start font-mono text-xs text-muted-foreground" dir="ltr">
                    {column}
                  </span>
                </span>
              </label>
            ))}
          </div>
        </fieldset>
      ) : null}
      {failure ? (
        <Alert variant="destructive">
          <AlertDescription>{failure}</AlertDescription>
        </Alert>
      ) : null}
      <div>
        <Button type="submit" size="lg" disabled={create.isPending}>
          {create.isPending ? t.exports.submitting : t.exports.submit}
        </Button>
      </div>
    </form>
  );
}
