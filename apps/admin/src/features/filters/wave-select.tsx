'use client';

import { Field, NativeSelect } from '@/components/data/field';
import { useWaves } from '@/lib/api/queries';
import { fill } from '@/lib/format';
import { useI18n } from '@/lib/i18n-context';

/**
 * The wave filter, from the platform's own list. Cohorts are not a platform concept, so they are not offered; the
 * pages where a reader would look for them (the overview and the analytics) say so with `hint`.
 */
export function WaveSelect({
  id,
  value,
  onChange,
  hint,
}: {
  id: string;
  value: string | null;
  onChange: (waveId: string | null) => void;
  /** A sentence under the select. When the waves cannot be loaded, that is said instead on every page. */
  hint?: string;
}) {
  const { t } = useI18n();
  const waves = useWaves();
  return (
    <Field id={id} label={t.filters.wave} hint={waves.isError ? t.filters.wavesUnavailable : hint}>
      <NativeSelect id={id} value={value ?? ''} disabled={waves.isError} onChange={(event) => onChange(event.target.value || null)}>
        <option value="">{t.filters.allWaves}</option>
        {waves.data?.items.map((wave) => (
          <option key={wave.id} value={wave.id}>
            {fill(t.filters.waveOption, { code: wave.code, status: t.status.wave[wave.status] })}
          </option>
        ))}
      </NativeSelect>
    </Field>
  );
}
