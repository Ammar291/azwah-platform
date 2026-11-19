import type { ScoreDistribution } from '@/lib/api/schemas';

/**
 * The identity of a distribution: one instrument version, one module and one rule-set version. Distributions are only
 * ever compared with the same key; no approved comparability rule joins different versions or rule sets.
 */
export function distributionKey(distribution: ScoreDistribution): string {
  return [
    distribution.instrumentCode,
    distribution.versionNumber,
    distribution.sectionCode ?? '',
    distribution.ruleSetCode,
    distribution.ruleSetVersion,
  ].join('|');
}
