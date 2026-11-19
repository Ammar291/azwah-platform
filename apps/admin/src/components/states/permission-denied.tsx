'use client';

import { LockKeyhole } from 'lucide-react';

import { useI18n } from '@/lib/i18n-context';

import { StateLayout } from './state-layout';

/** Shown by a page whose permission the caller lacks. Hidden navigation is never the control; this page check is. */
export function PermissionDenied() {
  const { t } = useI18n();
  // The whole page is this state, so its title is the page's heading.
  return <StateLayout role="alert" headingLevel={1} icon={<LockKeyhole />} tone="warning" title={t.states.deniedTitle} body={t.states.deniedBody} />;
}
