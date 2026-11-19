/**
 * Which dashboard sections a caller sees, from the permission codes `GET /api/v1/me/permissions` returns. This decides
 * what to show, never what is allowed: every page checks its permission on the server and the API authorises again.
 */

export const PAGE_PERMISSIONS = {
  overview: 'analytics.read',
  analytics: 'analytics.scores.read',
  exports: 'exports.manage',
  assessments: 'assessments.monitor',
  participation: 'participation.read',
  configuration: 'content.manage',
  results: 'results.read',
  privacyRequests: 'privacy.requests.manage',
  safetyAlerts: 'safety_alerts.read',
} as const;

export type GuardedPage = keyof typeof PAGE_PERMISSIONS;
export type Permission = (typeof PAGE_PERMISSIONS)[GuardedPage];
export type NavKey = GuardedPage | 'status';
export type NavGroup = 'research' | 'monitoring' | 'administration' | 'system';

export interface NavItem {
  href: string;
  key: NavKey;
  group: NavGroup;
  permission: Permission | null;
}

const NAVIGATION: readonly NavItem[] = [
  { href: '/overview', key: 'overview', group: 'research', permission: PAGE_PERMISSIONS.overview },
  { href: '/analytics', key: 'analytics', group: 'research', permission: PAGE_PERMISSIONS.analytics },
  { href: '/exports', key: 'exports', group: 'research', permission: PAGE_PERMISSIONS.exports },
  { href: '/monitoring/assessments', key: 'assessments', group: 'monitoring', permission: PAGE_PERMISSIONS.assessments },
  { href: '/monitoring/participation', key: 'participation', group: 'monitoring', permission: PAGE_PERMISSIONS.participation },
  { href: '/configuration', key: 'configuration', group: 'administration', permission: PAGE_PERMISSIONS.configuration },
  { href: '/results', key: 'results', group: 'administration', permission: PAGE_PERMISSIONS.results },
  { href: '/safety-alerts', key: 'safetyAlerts', group: 'administration', permission: PAGE_PERMISSIONS.safetyAlerts },
  { href: '/privacy-requests', key: 'privacyRequests', group: 'administration', permission: PAGE_PERMISSIONS.privacyRequests },
  { href: '/status', key: 'status', group: 'system', permission: null },
];

export function buildNavigation(permissions: readonly string[]): NavItem[] {
  return NAVIGATION.filter((item) => item.permission === null || permissions.includes(item.permission));
}

/** The page to open after sign-in: the first section the caller may use, or the welcome page. */
export function firstSection(permissions: readonly string[]): string {
  return buildNavigation(permissions).find((item) => item.permission !== null)?.href ?? '/welcome';
}
