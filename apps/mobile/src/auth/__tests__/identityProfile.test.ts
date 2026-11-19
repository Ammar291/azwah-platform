import { fullName, givenName, initials, profileFromIdTokenClaims, type IdentityProfile } from '../identityProfile';

const claims = {
  sub: 'uaepass-stg|1234',
  name: 'Mariam Ahmed Al Mansoori',
  given_name: 'Mariam',
  family_name: 'Al Mansoori',
  gender: 'female',
  birthdate: '1988-03-14',
  azwah_profile: {
    source: 'uae_pass_staging',
    name_ar: 'مريم أحمد المنصوري',
    given_name_ar: 'مريم',
    nationality_en: 'United Arab Emirates',
    nationality_ar: 'الإمارات العربية المتحدة',
    user_type: 'SOP3',
    emirates_id_masked: '784-••••-•••••42-1',
    mobile_masked: '+971 •• ••• ••67',
    email_masked: 'm•••@example.ae',
  },
};

describe('profileFromIdTokenClaims', () => {
  it('reads the details the provider released, masked as it sent them', () => {
    expect(profileFromIdTokenClaims(claims)).toEqual<IdentityProfile>({
      source: 'uae_pass_staging',
      nameEn: 'Mariam Ahmed Al Mansoori',
      nameAr: 'مريم أحمد المنصوري',
      givenNameEn: 'Mariam',
      givenNameAr: 'مريم',
      nationalityEn: 'United Arab Emirates',
      nationalityAr: 'الإمارات العربية المتحدة',
      gender: 'female',
      birthdate: '1988-03-14',
      emiratesIdMasked: '784-••••-•••••42-1',
      mobileMasked: '+971 •• ••• ••67',
      emailMasked: 'm•••@example.ae',
    });
  });

  it('reads a Google account’s details as from Google (D-147)', () => {
    const profile = profileFromIdTokenClaims({
      sub: 'google|1234',
      name: 'Test Google Person',
      given_name: 'Test',
      azwah_profile: { source: 'google', email_masked: 't•••@example.com' },
    });

    expect(profile).toMatchObject({
      source: 'google',
      nameEn: 'Test Google Person',
      givenNameEn: 'Test',
      emailMasked: 't•••@example.com',
      emiratesIdMasked: null,
    });
  });

  it('is null when the provider released no profile (a development persona)', () => {
    expect(profileFromIdTokenClaims({ sub: 'dev-participant-lead' })).toBeNull();
    expect(profileFromIdTokenClaims({ sub: 's', name: 'Someone' })).toBeNull();
    expect(profileFromIdTokenClaims('not claims')).toBeNull();
  });

  it('drops unusable values instead of failing, and never claims UAE PASS for an unknown source', () => {
    const profile = profileFromIdTokenClaims({
      ...claims,
      gender: 'unknown',
      birthdate: '14/03/1988',
      name: '',
      azwah_profile: { ...claims.azwah_profile, source: 'elsewhere', email_masked: 42, nationality_ar: null },
    });

    expect(profile).toMatchObject({
      source: 'unverified',
      gender: null,
      birthdate: null,
      nameEn: null,
      emailMasked: null,
      nationalityAr: null,
      nameAr: 'مريم أحمد المنصوري',
    });
  });
});

describe('names', () => {
  const profile = profileFromIdTokenClaims(claims);

  it('greets in the app’s language, falling back to the other one', () => {
    expect(givenName(profile, 'en')).toBe('Mariam');
    expect(givenName(profile, 'ar')).toBe('مريم');
    expect(givenName(profile && { ...profile, givenNameAr: null }, 'ar')).toBe('Mariam');
    expect(givenName(profile && { ...profile, givenNameEn: null, givenNameAr: null }, 'en')).toBe('Mariam');
    expect(givenName(null, 'en')).toBeNull();
  });

  it('gives the full name and initials', () => {
    expect(fullName(profile, 'ar')).toBe('مريم أحمد المنصوري');
    expect(fullName(profile && { ...profile, nameEn: null }, 'en')).toBe('مريم أحمد المنصوري');
    expect(initials(profile)).toBe('MM');
    expect(initials(null)).toBeNull();
  });
});
