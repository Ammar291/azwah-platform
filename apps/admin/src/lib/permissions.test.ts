import { describe, expect, it } from 'vitest';

import { buildNavigation, firstSection, PAGE_PERMISSIONS } from './permissions';

const researcher = ['analytics.read', 'analytics.scores.read', 'exports.manage', 'questionnaires.read'];
const programAdministrator = [
  'analytics.read',
  'assessments.monitor',
  'participation.read',
  'privacy.requests.manage',
  'questionnaires.read',
];
const systemAdministrator = [
  'content.manage',
  'eligibility.run',
  'partners.manage',
  'questionnaires.read',
  'results.read',
  'rules.manage',
  'scoring.run',
];

const hrefs = (permissions: string[]) => buildNavigation(permissions).map((item) => item.href);

describe('buildNavigation', () => {
  it('gives researchers the overview, analytics and exports', () => {
    expect(hrefs(researcher)).toEqual(['/overview', '/analytics', '/exports', '/status']);
  });

  it('gives program administrators the overview, monitoring and privacy requests, never scores or exports', () => {
    expect(hrefs(programAdministrator)).toEqual([
      '/overview',
      '/monitoring/assessments',
      '/monitoring/participation',
      '/privacy-requests',
      '/status',
    ]);
  });

  it('puts privacy requests in the administration group, for its own permission only', () => {
    const item = buildNavigation(['privacy.requests.manage']).find((candidate) => candidate.href === '/privacy-requests');

    expect(item).toMatchObject({ key: 'privacyRequests', group: 'administration', permission: 'privacy.requests.manage' });
    expect(hrefs(['participation.read', 'assessments.monitor'])).not.toContain('/privacy-requests');
  });

  it('gives system administrators the read-only configuration and household results', () => {
    expect(hrefs(systemAdministrator)).toEqual(['/configuration', '/results', '/status']);
  });

  it('shows household results for results.read only, never to researchers or program administrators', () => {
    const item = buildNavigation(['results.read']).find((candidate) => candidate.href === '/results');

    expect(item).toMatchObject({ key: 'results', group: 'administration', permission: 'results.read' });
    expect(hrefs([...researcher, ...programAdministrator])).not.toContain('/results');
    expect(hrefs(systemAdministrator.filter((permission) => permission !== 'results.read'))).toEqual(['/configuration', '/status']);
  });

  it('shows safety alerts in the administration group, for safety_alerts.read only', () => {
    const item = buildNavigation(['safety_alerts.read']).find((candidate) => candidate.href === '/safety-alerts');

    expect(item).toMatchObject({ key: 'safetyAlerts', group: 'administration', permission: 'safety_alerts.read' });
    expect(hrefs(['safety_alerts.manage'])).not.toContain('/safety-alerts');
    expect(hrefs([...researcher, ...programAdministrator, ...systemAdministrator])).not.toContain('/safety-alerts');
    expect(hrefs([...systemAdministrator, 'safety_alerts.read'])).toEqual(['/configuration', '/results', '/safety-alerts', '/status']);
  });

  it('gives a role with no dashboard section only the status page', () => {
    expect(hrefs(['referrals.manage', 'questionnaires.read'])).toEqual(['/status']);
    expect(hrefs([])).toEqual(['/status']);
  });

  it('shows the union for a caller with several roles', () => {
    expect(hrefs([...researcher, ...programAdministrator])).toEqual([
      '/overview',
      '/analytics',
      '/exports',
      '/monitoring/assessments',
      '/monitoring/participation',
      '/privacy-requests',
      '/status',
    ]);
  });

  it('ignores permission codes it does not know', () => {
    expect(hrefs(['made.up', 'analytics.read'])).toEqual(['/overview', '/status']);
  });
});

describe('firstSection', () => {
  it('opens the first section the caller may use', () => {
    expect(firstSection(researcher)).toBe('/overview');
    expect(firstSection(systemAdministrator)).toBe('/configuration');
  });

  it('opens the welcome page when no section applies', () => {
    expect(firstSection(['referrals.manage'])).toBe('/welcome');
  });
});

describe('PAGE_PERMISSIONS', () => {
  it('names the permission of every guarded page', () => {
    expect(PAGE_PERMISSIONS).toEqual({
      overview: 'analytics.read',
      analytics: 'analytics.scores.read',
      exports: 'exports.manage',
      assessments: 'assessments.monitor',
      participation: 'participation.read',
      configuration: 'content.manage',
      results: 'results.read',
      privacyRequests: 'privacy.requests.manage',
      safetyAlerts: 'safety_alerts.read',
    });
  });
});
