import type { TFunction } from 'i18next';
import { z } from 'zod';

import { privacyRequestTypeSchema, type PrivacyRequestType } from '@/api/participant/schemas';

export const PRIVACY_REQUEST_TYPES = privacyRequestTypeSchema.options;

/** What the request form holds: a type (none chosen at first) and the member it is about. No free text. */
export interface PrivacyRequestFormInput {
  type: PrivacyRequestType | null;
  memberId: string;
}

export interface PrivacyRequestFormOutput {
  type: PrivacyRequestType;
  memberId: string;
}

/** A type must be chosen, and the member must be one the server says the caller may act for (D-49). */
export function createPrivacyRequestSchema(t: TFunction, actableMemberIds: readonly string[]) {
  return z.object({
    type: privacyRequestTypeSchema.nullable().transform((type, context) => {
      if (type === null) {
        context.addIssue({ code: 'custom', message: t('profile.data.requests.errors.type') });
        return z.NEVER;
      }
      return type;
    }),
    memberId: z
      .string()
      .refine((memberId) => actableMemberIds.includes(memberId), { message: t('profile.data.requests.errors.member') }),
  });
}
