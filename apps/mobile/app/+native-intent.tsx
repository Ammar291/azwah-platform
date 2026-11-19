/**
 * Rewrites incoming deep links before Expo Router reads them: the sign-in callback (`auth/callback`) and the
 * signed-out return (`auth/signed-out`) are not screens. See src/auth/nativeIntent.ts.
 */
export { redirectSystemPath } from '@/auth/nativeIntent';
