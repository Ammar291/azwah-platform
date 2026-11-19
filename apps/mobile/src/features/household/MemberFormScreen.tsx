import { zodResolver } from '@hookform/resolvers/zod';
import { useMemo, useRef, useState, type ReactNode } from 'react';
import { useForm } from 'react-hook-form';

import type { Member, MemberDetails } from '@/api/participant/schemas';
import { AppScreen } from '@/components/AppScreen';
import { ProblemState } from '@/components/ProblemState';
import { useSingleFlight } from '@/components/useSingleFlight';
import { useUnsavedChangesGuard } from '@/components/useUnsavedChangesGuard';
import { AppButton } from '@/design-system/AppButton';
import { AppText } from '@/design-system/AppText';
import { useLocalization } from '@/i18n/useLocalization';

import { memberReference } from './memberLabels';
import {
  createMemberSchema,
  toMemberDetails,
  type MemberFormInput,
  type MemberFormOutput,
} from './memberForm';
import { MemberFields } from './MemberFields';
import { roleHolders } from './rosterRules';

/** The part of a TanStack mutation the form needs. */
export interface MemberSaveAction<T> {
  mutateAsync: (details: MemberDetails) => Promise<T>;
  isPending: boolean;
  error: unknown;
}

export interface MemberFormScreenProps<T> {
  title: string;
  intro?: string;
  /** Shown under the introduction, for example what is and is not asked. */
  notice?: ReactNode;
  initialValues: MemberFormInput;
  isSelf: boolean;
  /** The other active members, so that taking over a role from one of them is explained. */
  others: readonly Member[];
  submitLabel: string;
  save: MemberSaveAction<T>;
  onSaved: (result: T) => void;
  /** More under the form, for example removing the member; `allowLeave` lets it navigate away without the question. */
  children?: (helpers: { allowLeave: () => void }) => ReactNode;
}

/**
 * One member's details as a full screen: validation as the person goes (after they leave a field, then on every
 * change once they have tried to save), a save button that sends once however often it is pressed, the save's
 * problem with a retry beside the button, and a question before leaving with unsaved changes.
 */
export function MemberFormScreen<T>({
  title,
  intro,
  notice,
  initialValues,
  isSelf,
  others,
  submitLabel,
  save,
  onSaved,
  children,
}: MemberFormScreenProps<T>) {
  const localization = useLocalization();
  const { t } = localization;
  const now = useMemo(() => new Date(), []);
  const schema = useMemo(() => createMemberSchema(t, { now, isSelf }), [t, now, isSelf]);
  const form = useForm<MemberFormInput, unknown, MemberFormOutput>({
    resolver: zodResolver(schema),
    defaultValues: initialValues,
    mode: 'onTouched',
  });
  const guard = useUnsavedChangesGuard(form.formState.isDirty);
  const once = useSingleFlight();
  // After a successful save the screen is on its way out: further presses must not save again.
  const saved = useRef(false);
  const [leaving, setLeaving] = useState(false);
  const { head, primaryEarner } = roleHolders(others);

  const submit = () =>
    once(
      form.handleSubmit(async (values) => {
        if (saved.current) {
          return;
        }
        let result: T;
        try {
          result = await save.mutateAsync(toMemberDetails(values));
        } catch {
          return; // The mutation keeps the error; it is shown with a retry.
        }
        saved.current = true;
        setLeaving(true);
        guard.allowLeave();
        onSaved(result);
      }),
    );

  return (
    <AppScreen
      title={title}
      keyboardAware
      footer={
        <>
          {save.error ? <ProblemState error={save.error} variant="inline" onRetry={() => void submit()} /> : null}
          <AppButton label={submitLabel} loading={save.isPending || leaving} onPress={() => void submit()} />
        </>
      }
    >
      {intro ? <AppText>{intro}</AppText> : null}
      {notice}
      <MemberFields
        form={form}
        isSelf={isSelf}
        currentHead={head ? memberReference(head, localization) : null}
        currentEarner={primaryEarner ? memberReference(primaryEarner, localization) : null}
        now={now}
        disabled={save.isPending || leaving}
      />
      {children?.({ allowLeave: guard.allowLeave })}
      {guard.dialog}
    </AppScreen>
  );
}
