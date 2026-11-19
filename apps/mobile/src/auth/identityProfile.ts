import { z } from 'zod';

/**
 * Where the details came from. Only the confirmation screen shows them; nothing here is ever stored or sent. A source
 * the app does not recognise is `unverified`: shown without claiming UAE PASS. `google` is a Google account (D-147).
 */
export type IdentityProfileSource = 'uae_pass_staging' | 'uae_pass' | 'google' | 'simulated' | 'unverified';

/**
 * The person's details as their identity provider released them in the ID token (D-96, D-97). They live in memory
 * with the session and are shown once, for the person to confirm. The provider masks the Emirates ID, mobile number
 * and email before they reach the phone; the platform API never receives any of it.
 */
export interface IdentityProfile {
  source: IdentityProfileSource;
  nameEn: string | null;
  nameAr: string | null;
  givenNameEn: string | null;
  givenNameAr: string | null;
  nationalityEn: string | null;
  nationalityAr: string | null;
  gender: 'male' | 'female' | null;
  /** YYYY-MM-DD, when the provider releases it. */
  birthdate: string | null;
  emiratesIdMasked: string | null;
  mobileMasked: string | null;
  emailMasked: string | null;
}

const text = z.string().trim().min(1).max(200);
/** A claim that is absent, null or unusable reads as null rather than failing the whole profile. */
const optionalText = text.nullish().catch(null);

const profileClaimSchema = z.object({
  source: z.enum(['uae_pass_staging', 'uae_pass', 'google', 'unverified']).catch('unverified'),
  name_ar: optionalText,
  given_name_ar: optionalText,
  nationality_en: optionalText,
  nationality_ar: optionalText,
  emirates_id_masked: optionalText,
  mobile_masked: optionalText,
  email_masked: optionalText,
});

const idTokenProfileSchema = z.object({
  name: optionalText,
  given_name: optionalText,
  gender: z.enum(['male', 'female']).nullish().catch(null),
  birthdate: z
    .string()
    .regex(/^\d{4}-\d{2}-\d{2}$/)
    .nullish()
    .catch(null),
  azwah_profile: profileClaimSchema.nullish().catch(null),
});

/**
 * Reads the profile from ID token claims. Null when the provider released none (a development persona, or a provider
 * that shares no personal claims): the person then confirms without seeing details.
 */
export function profileFromIdTokenClaims(claims: unknown): IdentityProfile | null {
  const parsed = idTokenProfileSchema.safeParse(claims);
  if (!parsed.success || !parsed.data.azwah_profile) {
    return null;
  }
  const { name, given_name, gender, birthdate, azwah_profile: extra } = parsed.data;
  return {
    source: extra.source,
    nameEn: name ?? null,
    nameAr: extra.name_ar ?? null,
    givenNameEn: given_name ?? null,
    givenNameAr: extra.given_name_ar ?? null,
    nationalityEn: extra.nationality_en ?? null,
    nationalityAr: extra.nationality_ar ?? null,
    gender: gender ?? null,
    birthdate: birthdate ?? null,
    emiratesIdMasked: extra.emirates_id_masked ?? null,
    mobileMasked: extra.mobile_masked ?? null,
    emailMasked: extra.email_masked ?? null,
  };
}

/** The name to greet someone by, in the app's language when the provider gave it, otherwise in the other one. */
export function givenName(profile: IdentityProfile | null, language: 'ar' | 'en'): string | null {
  if (!profile) return null;
  const preferred = language === 'ar' ? profile.givenNameAr : profile.givenNameEn;
  const other = language === 'ar' ? profile.givenNameEn : profile.givenNameAr;
  return preferred ?? other ?? firstWord(language === 'ar' ? profile.nameAr : profile.nameEn);
}

/** The full name in the app's language, falling back to the other language. */
export function fullName(profile: IdentityProfile | null, language: 'ar' | 'en'): string | null {
  if (!profile) return null;
  return language === 'ar' ? (profile.nameAr ?? profile.nameEn) : (profile.nameEn ?? profile.nameAr);
}

/** Up to two initials for an avatar, from the English name when there is one. */
export function initials(profile: IdentityProfile | null): string | null {
  const name = profile?.nameEn ?? profile?.nameAr;
  if (!name) return null;
  const words = name.split(/\s+/).filter(Boolean);
  const first = words[0]?.[0] ?? '';
  const last = words.length > 1 ? (words[words.length - 1]?.[0] ?? '') : '';
  return `${first}${last}`.toUpperCase() || null;
}

function firstWord(value: string | null): string | null {
  return value?.split(/\s+/).find(Boolean) ?? null;
}
