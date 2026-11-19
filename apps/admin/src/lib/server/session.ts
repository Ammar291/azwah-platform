import 'server-only';

import { EncryptJWT, jwtDecrypt } from 'jose';
import { z } from 'zod';

import { PERSONAS } from '@/lib/personas';

/**
 * The signed-in user's session, sealed into an HttpOnly cookie as a JWE (`dir`, `A256GCM`). It holds the tokens the
 * dashboard's server uses to call the API; the browser cannot read or forge it. Roles are never stored here: they come
 * from `GET /api/v1/me/permissions` on every request.
 */
const sessionSchema = z.discriminatedUnion('kind', [
  z.object({
    kind: z.literal('oidc'),
    accessToken: z.string().min(1),
    refreshToken: z.string().min(1).nullable(),
    accessTokenExpiresAt: z.number().int(),
    issuedAt: z.number().int(),
  }),
  z.object({
    kind: z.literal('development'),
    persona: z.enum(PERSONAS),
    issuedAt: z.number().int(),
  }),
]);

export type Session = z.infer<typeof sessionSchema>;
export type OidcSession = Extract<Session, { kind: 'oidc' }>;

export function sessionCookieName(secure: boolean): string {
  return secure ? '__Host-azwah_session' : 'azwah_session';
}

/** Seals a session that expires `maxAgeSeconds` after it was first issued, however often it is sealed again. */
export async function sealSession(session: Session, secret: Uint8Array, maxAgeSeconds: number): Promise<string> {
  const issuedAtSeconds = Math.floor(session.issuedAt / 1000);
  return new EncryptJWT({ session })
    .setProtectedHeader({ alg: 'dir', enc: 'A256GCM' })
    .setIssuedAt(issuedAtSeconds)
    .setExpirationTime(issuedAtSeconds + maxAgeSeconds)
    .encrypt(secret);
}

/** The session in a sealed value, or null when it is missing, forged, expired or malformed. */
export async function unsealSession(
  value: string | undefined,
  secret: Uint8Array,
  now: number = Date.now(),
): Promise<Session | null> {
  if (!value) return null;
  try {
    const { payload } = await jwtDecrypt(value, secret, { currentDate: new Date(now) });
    const parsed = sessionSchema.safeParse(payload.session);
    return parsed.success ? parsed.data : null;
  } catch {
    return null;
  }
}
