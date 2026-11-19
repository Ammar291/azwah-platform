import { createI18n } from '@/i18n/i18n';
import type { Language } from '@/i18n/languages';
import { memberFixture } from '@/test-utils/householdWorlds';

import {
  createMemberSchema,
  memberFormValues,
  memberPatch,
  toMemberDetails,
  typedAge,
  type MemberFormInput,
} from '../memberForm';

const NOW = new Date('2026-10-14T00:00:00Z');

const valid: MemberFormInput = {
  birthYear: '1985',
  birthMonth: '4',
  relationshipToHousehold: 'spouse',
  employmentStatus: 'employed',
  isHeadOfHousehold: false,
  isPrimaryIncomeEarner: false,
};

async function schemaFor({ isSelf = false, language = 'en' }: { isSelf?: boolean; language?: Language } = {}) {
  return createMemberSchema((await createI18n(language)).t, { now: NOW, isSelf });
}

/** The messages a value gets, by field. */
async function errorsFor(values: Partial<MemberFormInput>, options?: { isSelf?: boolean; language?: Language }) {
  const result = (await schemaFor(options)).safeParse({ ...valid, ...values });
  return result.success
    ? {}
    : Object.fromEntries(result.error.issues.map((issue) => [issue.path.join('.'), issue.message]));
}

describe('member form validation', () => {
  it('accepts Arabic-Indic digits typed on an Arabic keyboard', async () => {
    const schema = await schemaFor({ language: 'ar' });

    expect(schema.parse({ ...valid, birthYear: '١٩٨٥', birthMonth: '٤' })).toMatchObject({
      birthYear: '1985',
      birthMonth: '4',
    });
  });

  it('refuses years and months that are not dates of birth', async () => {
    expect(await errorsFor({ birthYear: '85' })).toEqual({
      birthYear: 'Enter a year from 1900 to now.',
    });
    expect(await errorsFor({ birthYear: '2027' })).toHaveProperty('birthYear');
    expect(await errorsFor({ birthYear: '1899' })).toHaveProperty('birthYear');
    expect(await errorsFor({ birthMonth: '13' })).toEqual({ birthMonth: 'Enter 1 to 12.' });
    expect(await errorsFor({ birthMonth: '0' })).toHaveProperty('birthMonth');
  });

  it('refuses a month of birth in the future', async () => {
    expect(await errorsFor({ birthYear: '2026', birthMonth: '11', employmentStatus: null })).toEqual({
      birthMonth: "That month is in the future.",
    });
    expect(await errorsFor({ birthYear: '2026', birthMonth: '10', employmentStatus: null })).toEqual({});
  });

  it('reports every missing answer at once, so each field shows its own error', async () => {
    expect(
      await errorsFor({ birthYear: '', birthMonth: '', relationshipToHousehold: null, employmentStatus: null }),
    ).toEqual({
      birthYear: 'Enter a year from 1900 to now.',
      birthMonth: 'Enter 1 to 12.',
      relationshipToHousehold: "Choose a relationship.",
    });
  });

  it('asks for a relationship unless the person is the head of household', async () => {
    expect(await errorsFor({ relationshipToHousehold: null })).toEqual({
      relationshipToHousehold: "Choose a relationship.",
    });
    expect(await errorsFor({ relationshipToHousehold: null }, { isSelf: true })).toEqual({
      relationshipToHousehold: 'Choose your relationship.',
    });
    expect(await errorsFor({ relationshipToHousehold: null, isHeadOfHousehold: true })).toEqual({});
  });

  it('asks adults for a work status and leaves it optional under 18', async () => {
    expect(await errorsFor({ employmentStatus: null })).toEqual({ employmentStatus: "Choose a work status." });
    expect(await errorsFor({ birthYear: '2015', relationshipToHousehold: 'child', employmentStatus: null })).toEqual({});
    // Turned 18 this month: an adult.
    expect(await errorsFor({ birthYear: '2008', birthMonth: '10', employmentStatus: null })).toHaveProperty(
      'employmentStatus',
    );
  });

  it('keeps the head-of-household and main-earner roles for adults', async () => {
    expect(await errorsFor({ birthYear: '2015', isHeadOfHousehold: true, relationshipToHousehold: null })).toEqual({
      birthYear: 'The head must be 18 or older.',
    });
    expect(await errorsFor({ birthYear: '2015', isPrimaryIncomeEarner: true })).toEqual({
      birthYear: 'The main earner must be 18 or older.',
    });
  });

  it('needs the person setting up the household to be 18 or over', async () => {
    expect(await errorsFor({ birthYear: '2010' }, { isSelf: true })).toEqual({
      birthYear: 'You must be 18 or older.',
    });
  });

  it('writes its messages in Arabic for Arabic speakers', async () => {
    expect(await errorsFor({ employmentStatus: null }, { language: 'ar' })).toEqual({
      employmentStatus: 'اختر الحالة الوظيفية.',
    });
  });
});

describe('member form values', () => {
  it('reads the age from what was typed once the year and month are complete', () => {
    expect(typedAge({ birthYear: '1990', birthMonth: '6' }, NOW)).toBe(36);
    expect(typedAge({ birthYear: '2008', birthMonth: '11' }, NOW)).toBe(17);
    expect(typedAge({ birthYear: '199', birthMonth: '6' }, NOW)).toBeNull();
    expect(typedAge({ birthYear: '١٩٩٠', birthMonth: '٦' }, NOW)).toBe(36);
  });

  it('sends the head of household without a relationship', () => {
    expect(toMemberDetails({ ...valid, isHeadOfHousehold: true })).toEqual({
      birthYear: 1985,
      birthMonth: 4,
      relationshipToHousehold: null,
      employmentStatus: 'employed',
      isHeadOfHousehold: true,
      isPrimaryIncomeEarner: false,
    });
  });

  it('patches only the fields that changed', () => {
    const member = memberFixture({ employmentStatus: 'homemaker' });
    const details = toMemberDetails({ ...memberFormValues(member), employmentStatus: 'employed' });

    expect(memberPatch(member, details)).toEqual({ employmentStatus: 'employed' });
    expect(memberPatch(member, toMemberDetails(memberFormValues(member)))).toEqual({});
  });

  it('clears the relationship when a member becomes the head of household', () => {
    const member = memberFixture();
    const details = toMemberDetails({ ...memberFormValues(member), isHeadOfHousehold: true });

    expect(memberPatch(member, details)).toEqual({ isHeadOfHousehold: true, relationshipToHousehold: null });
  });
});
