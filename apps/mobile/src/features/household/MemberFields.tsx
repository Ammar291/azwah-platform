import { useEffect } from 'react';
import { Controller, useWatch, type UseFormReturn } from 'react-hook-form';
import { StyleSheet, View } from 'react-native';

import { FormField } from '@/design-system/FormField';
import { Notice } from '@/design-system/Notice';
import { RadioCardGroup } from '@/design-system/RadioCardGroup';
import { SwitchField } from '@/design-system/SwitchField';
import { spacing } from '@/design-system/tokens';
import { useLocalization } from '@/i18n/useLocalization';

import {
  EMPLOYMENT_STATUSES,
  RELATIONSHIPS,
  typedAge,
  type MemberFormInput,
  type MemberFormOutput,
} from './memberForm';
import { ADULT_AGE } from './rosterRules';

export interface MemberFieldsProps {
  form: UseFormReturn<MemberFormInput, unknown, MemberFormOutput>;
  /** The person filling in the form, worded as "you". */
  isSelf: boolean;
  /** Who else holds each role now, described, so that taking the role over is explained. */
  currentHead: string | null;
  currentEarner: string | null;
  now: Date;
  disabled?: boolean;
}

/**
 * The details the platform keeps about a family member: year and month of birth, head of household, relationship to
 * the head, work status and main earner. No names and no identity numbers. Fields follow the answers: the head has no
 * relationship to choose, and a member under 18 cannot hold either role (D-58), so those choices are not offered. The
 * value lists are engineering proposals awaiting approval (open question 18).
 */
export function MemberFields({ form, isSelf, currentHead, currentEarner, now, disabled = false }: MemberFieldsProps) {
  const { t } = useLocalization();
  const { control, getFieldState, getValues, setValue, trigger, formState } = form;
  const [birthYear, birthMonth, isHead] = useWatch({
    control,
    name: ['birthYear', 'birthMonth', 'isHeadOfHousehold'],
  });
  const age = typedAge({ birthYear, birthMonth }, now);
  const isMinor = age !== null && age >= 0 && age < ADULT_AGE;
  const who = isSelf ? 'self' : 'other';
  const { isSubmitted } = formState;

  // A member who turns out to be under 18 loses any role chosen before the date was complete.
  useEffect(() => {
    if (!isMinor) {
      return;
    }
    if (getValues('isHeadOfHousehold')) {
      setValue('isHeadOfHousehold', false, { shouldDirty: true });
    }
    if (getValues('isPrimaryIncomeEarner')) {
      setValue('isPrimaryIncomeEarner', false, { shouldDirty: true });
    }
  }, [isMinor, getValues, setValue]);

  // A complete date gives an age, and age-based messages ("must be 18 or older") belong to the year: check both birth
  // fields again as soon as the age is known or changes, once the person has been in them.
  useEffect(() => {
    if (age === null) {
      return;
    }
    if (getFieldState('birthYear').isTouched || getFieldState('birthMonth').isTouched || isSubmitted) {
      void trigger(['birthYear', 'birthMonth']);
    }
  }, [age, getFieldState, isSubmitted, trigger]);

  // Once the person has tried to save, a change to one answer re-checks the answers that depend on it.
  const changed =
    <V,>(onChange: (value: V) => void) =>
    (value: V) => {
      onChange(value);
      if (isSubmitted) {
        void trigger();
      }
    };

  return (
    <View style={styles.fields}>
      <FormField
        control={control}
        name="birthYear"
        label={t('household.form.birthYear')}
        hint={t('household.form.birthYearHint')}
        keyboardType="number-pad"
        maxLength={4}
        disabled={disabled}
      />
      <FormField
        control={control}
        name="birthMonth"
        label={t('household.form.birthMonth')}
        hint={t('household.form.birthMonthHint')}
        keyboardType="number-pad"
        maxLength={2}
        disabled={disabled}
      />
      {isMinor ? (
        <Notice tone="info" message={t(`household.form.under18.${who}`)} testID="member-under-18" />
      ) : (
        <Controller
          control={control}
          name="isHeadOfHousehold"
          render={({ field }) => (
            <SwitchField
              label={t(`household.form.isHead.${who}`)}
              description={
                field.value && currentHead
                  ? t('household.form.takesOverHead', { member: currentHead })
                  : t('household.form.isHeadHint')
              }
              value={field.value}
              onValueChange={changed(field.onChange)}
              disabled={disabled}
            />
          )}
        />
      )}
      {isHead ? null : (
        <Controller
          control={control}
          name="relationshipToHousehold"
          render={({ field, fieldState }) => (
            <RadioCardGroup
              density="compact"
              label={t(`household.form.relationship.${who}`)}
              options={RELATIONSHIPS.map((value) => ({ value, label: t(`relationship.${value}`) }))}
              value={field.value}
              onChange={changed(field.onChange)}
              error={fieldState.error?.message}
              disabled={disabled}
            />
          )}
        />
      )}
      <Controller
        control={control}
        name="employmentStatus"
        render={({ field, fieldState }) => (
          <RadioCardGroup
            density="compact"
            label={isMinor ? t(`household.form.employmentOptional.${who}`) : t(`household.form.employment.${who}`)}
            options={EMPLOYMENT_STATUSES.map((value) => ({ value, label: t(`employmentStatus.${value}`) }))}
            value={field.value}
            onChange={changed(field.onChange)}
            error={fieldState.error?.message}
            disabled={disabled}
          />
        )}
      />
      {isMinor ? null : (
        <Controller
          control={control}
          name="isPrimaryIncomeEarner"
          render={({ field }) => (
            <SwitchField
              label={t(`household.form.isPrimaryEarner.${who}`)}
              description={
                field.value && currentEarner
                  ? t('household.form.takesOverEarner', { member: currentEarner })
                  : t('household.form.isPrimaryEarnerHint')
              }
              value={field.value}
              onValueChange={changed(field.onChange)}
              disabled={disabled}
            />
          )}
        />
      )}
    </View>
  );
}

const styles = StyleSheet.create({
  fields: { gap: spacing.lg },
});
