import { ApiError } from '@/lib/api/errors';
import { fill } from '@/lib/format';
import type { Language } from '@/lib/i18n';
import type { Messages } from '@/lib/messages';

/** A signal code in the staff's words; a code this dashboard does not know yet is shown exactly as recorded. */
export function signalName(t: Messages, code: string): string {
  const names: Record<string, string> = t.safetyAlerts.signals;
  return Object.hasOwn(names, code) ? names[code] : code;
}

/** The platform's refusal in the dashboard's own words, never its text. `fallback` names what was not done. */
export function alertRefusal(t: Messages, error: unknown, fallback: string): string {
  const messages = t.safetyAlerts;
  if (!(error instanceof ApiError)) return fallback;
  switch (error.status) {
    case 400:
      return messages.invalid;
    case 401:
      return t.states.signedOutBody;
    case 403:
      return messages.denied;
    case 404:
      return messages.notFound;
    case 409:
      return error.code === 'safeguarding.alert_resolved' ? messages.alreadyResolved : messages.changed;
    case 429:
      return messages.rateLimited;
    default:
      return fallback;
  }
}

/** After these the row on screen is stale and the dialog can do nothing more with it. */
export function isStale(error: unknown): boolean {
  return error instanceof ApiError && (error.status === 404 || error.status === 409);
}

/** The member's relationship to the head in the staff's words; null names no member, an unknown code is shown as recorded. */
export function relationshipName(t: Messages, code: string | null): string {
  if (code === null) return t.common.none;
  const names: Record<string, string> = t.safetyAlerts.relationships;
  return Object.hasOwn(names, code) ? names[code] : code;
}

/** Where an alert came from; a source this dashboard does not know yet is shown exactly as recorded. */
export function sourceName(t: Messages, code: string): string {
  const names: Record<string, string> = t.safetyAlerts.sources;
  return Object.hasOwn(names, code) ? names[code] : code;
}

/** How an alert was found; a detector this dashboard does not know yet is shown exactly as recorded. */
export function detectorName(t: Messages, code: string): string {
  const names: Record<string, string> = t.safetyAlerts.detectors;
  return Object.hasOwn(names, code) ? names[code] : code;
}

/** "N safety alerts need attention" in the right plural form of the dashboard language. */
export function attentionLabel(t: Messages, language: Language, count: number, formatted: string): string {
  const forms: Record<string, string> = t.safetyAlerts.badge;
  const form = new Intl.PluralRules(language).select(count);
  return fill(Object.hasOwn(forms, form) ? forms[form] : forms.other, { count: formatted });
}
