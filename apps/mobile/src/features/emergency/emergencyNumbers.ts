export type EmergencyGroup = 'emergency' | 'support' | 'emirate';

export type EmergencyCoverage =
  | 'uae'
  | 'abu_dhabi'
  | 'dubai'
  | 'sharjah'
  | 'ajman'
  | 'umm_al_quwain'
  | 'ras_al_khaimah'
  | 'fujairah';

export interface EmergencyNumber {
  /** Stable id and translation key under `emergency.services`. */
  id: string;
  group: EmergencyGroup;
  /** Digits only: what the dialler receives. */
  dial: string;
  /** As shown, grouped the way the service writes it. Western digits in both languages. */
  display: string;
  coverage: EmergencyCoverage;
  /** The official page the number was confirmed on (CONTRIBUTING.md rule 9: no number is taken from memory). */
  source: string;
  /** When it was last confirmed there, YYYY-MM-DD. */
  checkedOn: string;
}

export const EMERGENCY_GROUPS: readonly EmergencyGroup[] = ['emergency', 'support', 'emirate'];

/** The government portal's emergency page; it lists the national numbers and each emirate's protection lines. */
const UAE_EMERGENCIES =
  'https://u.ae/en/information-and-services/justice-safety-and-the-law/Safety/handling-emergencies';
const ABU_DHABI_MEDIA_OFFICE = 'https://www.mediaoffice.abudhabi/en';
const CHECKED_ON = '2026-10-28';

/** Also dialled by the "in danger now" notice. */
export const POLICE: EmergencyNumber = {
  id: 'police',
  group: 'emergency',
  dial: '999',
  display: '999',
  coverage: 'uae',
  source: UAE_EMERGENCIES,
  checkedOn: CHECKED_ON,
};

/**
 * UAE emergency and family-protection numbers (D-102), in display order. Each was confirmed on the official page named
 * on 2026-10-28; numbers only news sites still carry were left out (800 4673, the former national mental support line,
 * whose official site is gone). Review them at least every six months and whenever a service announces a change (open
 * question 45). Fujairah and Umm Al Quwain list no line of their own: 999 and 116111 serve them.
 */
export const EMERGENCY_NUMBERS: readonly EmergencyNumber[] = [
  POLICE,
  {
    id: 'ambulance',
    group: 'emergency',
    dial: '998',
    display: '998',
    coverage: 'uae',
    source: UAE_EMERGENCIES,
    checkedOn: CHECKED_ON,
  },
  {
    id: 'civilDefence',
    group: 'emergency',
    dial: '997',
    display: '997',
    coverage: 'uae',
    source: UAE_EMERGENCIES,
    checkedOn: CHECKED_ON,
  },
  {
    // The Child Protection Centre takes reports that are not emergencies; a child in danger now means 999.
    id: 'childProtection',
    group: 'support',
    dial: '116111',
    display: '116111',
    coverage: 'uae',
    source: 'https://moi-cpc.ae/en/CONTACT.US.aspx',
    checkedOn: CHECKED_ON,
  },
  {
    id: 'ewaa',
    group: 'support',
    dial: '8007283',
    display: '800 7283',
    coverage: 'uae',
    source: UAE_EMERGENCIES,
    checkedOn: CHECKED_ON,
  },
  {
    id: 'dubaiFoundation',
    group: 'emirate',
    dial: '800111',
    display: '800 111',
    coverage: 'dubai',
    source: 'https://www.dfwac.ae/services/helpline',
    checkedOn: CHECKED_ON,
  },
  {
    id: 'familyCareAuthority',
    group: 'emirate',
    dial: '800444',
    display: '800 444',
    coverage: 'abu_dhabi',
    source:
      `${ABU_DHABI_MEDIA_OFFICE}/community/department-of-community-development-and-family-care-authority-highlights-` +
      'role-of-family-care-authority-in-achieving-aims-of-abu-dhabi-family-wellbeing-strategy',
    checkedOn: CHECKED_ON,
  },
  {
    id: 'sakina',
    group: 'emirate',
    dial: '800725462',
    display: '800 725462',
    coverage: 'abu_dhabi',
    source:
      `${ABU_DHABI_MEDIA_OFFICE}/health/department-of-health-abu-dhabi-activates-24-7-mental-health-support-hotline-` +
      '800-sakina/',
    checkedOn: CHECKED_ON,
  },
  {
    id: 'sharjahProtection',
    group: 'emirate',
    dial: '800700',
    display: '800 700',
    coverage: 'sharjah',
    source: UAE_EMERGENCIES,
    checkedOn: CHECKED_ON,
  },
  {
    id: 'hemaya',
    group: 'emirate',
    dial: '800446292',
    display: '800 446292',
    coverage: 'ajman',
    source: UAE_EMERGENCIES,
    checkedOn: CHECKED_ON,
  },
  {
    id: 'aman',
    group: 'emirate',
    dial: '072356666',
    display: '07 235 6666',
    coverage: 'ras_al_khaimah',
    source: UAE_EMERGENCIES,
    checkedOn: CHECKED_ON,
  },
];
