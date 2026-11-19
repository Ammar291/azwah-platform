import type { TFunction } from 'i18next';
import { z } from 'zod';

import {
  employmentStatusSchema,
  relationshipSchema,
  type EmploymentStatus,
  type Member,
  type MemberDetails,
  type MemberPatch,
  type Relationship,
} from '@/api/participant/schemas';
import { toWesternDigits } from '@/i18n/format';

import { ADULT_AGE, ageOn, MIN_BIRTH_YEAR } from './rosterRules';

export const RELATIONSHIPS = relationshipSchema.options;
export const EMPLOYMENT_STATUSES = employmentStatusSchema.options;

/**
 * What the member form holds: the details the platform keeps about one person (no names, no identity numbers).
 * Year and month are text as typed; Arabic-Indic digits from an Arabic keyboard are accepted.
 */
export interface MemberFormInput {
  birthYear: string;
  birthMonth: string;
  relationshipToHousehold: Relationship | null;
  employmentStatus: EmploymentStatus | null;
  isHeadOfHousehold: boolean;
  isPrimaryIncomeEarner: boolean;
}

/** The year and month typed, or null while they are not yet a year from 1900 and a month from 1 to 12. */
export function typedBirth(birthYear: string, birthMonth: string): { year: number; month: number } | null {
  const year = toWesternDigits(birthYear.trim());
  const month = toWesternDigits(birthMonth.trim());
  if (!/^\d{4}$/.test(year) || !/^\d{1,2}$/.test(month)) {
    return null;
  }
  const value = { year: Number(year), month: Number(month) };
  return value.year >= MIN_BIRTH_YEAR && value.month >= 1 && value.month <= 12 ? value : null;
}

/** The age the typed year and month give on `now`, or null while they are incomplete. */
export function typedAge(values: Pick<MemberFormInput, 'birthYear' | 'birthMonth'>, now: Date): number | null {
  const birth = typedBirth(values.birthYear, values.birthMonth);
  return birth ? ageOn(birth.year, birth.month, now) : null;
}

export interface MemberSchemaOptions {
  now: Date;
  /** The person filling in the form: they manage the household, so they must be an adult. */
  isSelf: boolean;
}

/**
 * Field-level validation for one member, with the approved roster rules that concern a single person (D-50, D-58):
 * a relationship unless they are the head, a work status for adults, and only adults as head, main earner or the
 * person setting up the household. Rules across the roster (one head, one main earner) are handled by moving the
 * role, and the server checks everything again.
 */
export function createMemberSchema(t: TFunction, { now, isSelf }: MemberSchemaOptions) {
  const digits = (pattern: RegExp, valid: (value: number) => boolean, message: string) =>
    z
      .string()
      .transform((value) => toWesternDigits(value.trim()))
      .refine((value) => pattern.test(value) && valid(Number(value)), { message });

  return z
    .object({
      birthYear: digits(
        /^\d{4}$/,
        (year) => year >= MIN_BIRTH_YEAR && year <= now.getUTCFullYear(),
        t('household.form.errors.birthYear'),
      ),
      birthMonth: digits(/^\d{1,2}$/, (month) => month >= 1 && month <= 12, t('household.form.errors.birthMonth')),
      relationshipToHousehold: relationshipSchema.nullable(),
      employmentStatus: employmentStatusSchema.nullable(),
      isHeadOfHousehold: z.boolean(),
      isPrimaryIncomeEarner: z.boolean(),
    })
    .superRefine(
      (values, ctx) => {
        if (!values.isHeadOfHousehold && values.relationshipToHousehold === null) {
          ctx.addIssue({
            code: 'custom',
            path: ['relationshipToHousehold'],
            message: t(isSelf ? 'household.form.errors.relationshipRequiredSelf' : 'household.form.errors.relationshipRequired'),
          });
        }
        // Only once the year and month are themselves valid; their own messages cover the rest.
        const birth = typedBirth(String(values.birthYear), String(values.birthMonth));
        if (!birth || birth.year > now.getUTCFullYear()) {
          return;
        }
        const age = ageOn(birth.year, birth.month, now);
        const birthIssue = (key: string, path: 'birthYear' | 'birthMonth' = 'birthYear') =>
          ctx.addIssue({ code: 'custom', path: [path], message: t(key) });
        if (age < 0) {
          birthIssue('household.form.errors.birthMonthFuture', 'birthMonth');
          return;
        }
        if (age >= ADULT_AGE && values.employmentStatus === null) {
          ctx.addIssue({
            code: 'custom',
            path: ['employmentStatus'],
            message: t(isSelf ? 'household.form.errors.employmentRequiredSelf' : 'household.form.errors.employmentRequired'),
          });
        }
        if (age >= ADULT_AGE) {
          return;
        }
        if (isSelf) {
          birthIssue('household.form.errors.selfUnder18');
        } else if (values.isHeadOfHousehold) {
          birthIssue('household.form.errors.headUnder18');
        } else if (values.isPrimaryIncomeEarner) {
          birthIssue('household.form.errors.primaryEarnerUnder18');
        }
      },
      { when: () => true },
    );
}

export type MemberFormOutput = z.output<ReturnType<typeof createMemberSchema>>;

export function emptyMemberForm(): MemberFormInput {
  return {
    birthYear: '',
    birthMonth: '',
    relationshipToHousehold: null,
    employmentStatus: null,
    isHeadOfHousehold: false,
    isPrimaryIncomeEarner: false,
  };
}

export function memberFormValues(member: Member): MemberFormInput {
  return {
    birthYear: String(member.birthYear),
    birthMonth: String(member.birthMonth),
    relationshipToHousehold: member.relationshipToHousehold,
    employmentStatus: member.employmentStatus,
    isHeadOfHousehold: member.isHeadOfHousehold,
    isPrimaryIncomeEarner: member.isPrimaryIncomeEarner,
  };
}

/** The request body for one member. The head of household has no relationship to themselves. */
export function toMemberDetails(values: MemberFormOutput): MemberDetails {
  return {
    birthYear: Number(values.birthYear),
    birthMonth: Number(values.birthMonth),
    relationshipToHousehold: values.isHeadOfHousehold ? null : values.relationshipToHousehold,
    employmentStatus: values.employmentStatus,
    isHeadOfHousehold: values.isHeadOfHousehold,
    isPrimaryIncomeEarner: values.isPrimaryIncomeEarner,
  };
}

const DETAIL_FIELDS = [
  'birthYear',
  'birthMonth',
  'relationshipToHousehold',
  'employmentStatus',
  'isHeadOfHousehold',
  'isPrimaryIncomeEarner',
] as const satisfies readonly (keyof MemberDetails)[];

/** A merge patch with only the fields that differ from what the server holds; empty when nothing changed. */
export function memberPatch(member: Member, details: MemberDetails): MemberPatch {
  const patch: Record<string, unknown> = {};
  for (const field of DETAIL_FIELDS) {
    if (member[field] !== details[field]) {
      patch[field] = details[field];
    }
  }
  return patch as MemberPatch;
}

export function sameDetails(member: Member, details: MemberDetails): boolean {
  return Object.keys(memberPatch(member, details)).length === 0;
}
