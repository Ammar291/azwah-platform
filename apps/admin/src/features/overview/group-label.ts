import type { Messages } from '@/lib/messages';

/** A demographic group code from the API in words; a code the dashboard does not know is shown as it is. */
export function groupLabel(t: Messages, kind: 'age' | 'employment', code: string): string {
  const labels = t.groups[kind] as Record<string, string>;
  return labels[code] ?? code;
}
