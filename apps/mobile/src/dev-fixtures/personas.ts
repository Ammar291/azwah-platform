import type { IdentityProfile } from '@/auth/identityProfile';

/**
 * DEVELOPMENT FIXTURES ONLY. Synthetic people for the simulated sign-in; none of them is a real person, and their
 * "UAE PASS" details are invented and labelled `simulated` (no Emirates ID, number or address is real: the masked
 * values follow the shape the development provider sends, D-96). Their names and descriptions in the persona picker
 * are translation keys under `devFixtures.personas`.
 */
const householdLeadProfile: IdentityProfile = {
  source: 'simulated',
  nameEn: 'Layla Hassan Al Amiri',
  nameAr: 'ليلى حسن العامري',
  givenNameEn: 'Layla',
  givenNameAr: 'ليلى',
  nationalityEn: 'United Arab Emirates',
  nationalityAr: 'الإمارات العربية المتحدة',
  gender: 'female',
  birthdate: '1984-03-14',
  emiratesIdMasked: '784-••••-•••••12-3',
  mobileMasked: '+971 •• ••• ••45',
  emailMasked: 'l•••@example.test',
};

const newParticipantProfile: IdentityProfile = {
  source: 'simulated',
  nameEn: 'Omar Khalid Al Suwaidi',
  nameAr: 'عمر خالد السويدي',
  givenNameEn: 'Omar',
  givenNameAr: 'عمر',
  nationalityEn: 'United Arab Emirates',
  nationalityAr: 'الإمارات العربية المتحدة',
  gender: 'male',
  birthdate: '1991-11-02',
  emiratesIdMasked: '784-••••-•••••78-9',
  mobileMasked: '+971 •• ••• ••81',
  emailMasked: 'o•••@example.test',
};

export const DEV_PERSONAS = [
  { code: 'household_lead', subject: 'dev-mock|household_lead', profile: householdLeadProfile },
  { code: 'new_participant', subject: 'dev-mock|new_participant', profile: newParticipantProfile },
] as const;

export type DevPersonaCode = (typeof DEV_PERSONAS)[number]['code'];

export function personaForSubject(subject: string): DevPersonaCode | null {
  return DEV_PERSONAS.find((persona) => persona.subject === subject)?.code ?? null;
}
