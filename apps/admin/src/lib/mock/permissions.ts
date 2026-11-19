import 'server-only';

import type { Persona } from '@/lib/personas';

/**
 * Mock data source only: what `GET /api/v1/me/permissions` would grant each persona, copied from the API's
 * `PermissionMap` (backend/FamilyResilience.Api/Authorization/PermissionMap.cs). With the real API the dashboard never
 * reads this table; the platform answers instead.
 */
export const MOCK_PERMISSIONS: Record<Persona, string[]> = {
  researcher: ['analytics.read', 'analytics.scores.read', 'exports.manage', 'questionnaires.read'],
  program_administrator: ['analytics.read', 'assessments.monitor', 'participation.read', 'privacy.requests.manage', 'questionnaires.read'],
  system_administrator: [
    'content.manage',
    'eligibility.run',
    'partners.manage',
    'questionnaires.read',
    'results.read',
    'rules.manage',
    'safety_alerts.manage',
    'safety_alerts.read',
    'scoring.run',
  ],
  support_coordinator: ['questionnaires.read', 'referrals.manage'],
};
