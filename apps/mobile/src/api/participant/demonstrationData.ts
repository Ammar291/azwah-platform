import type { ParticipantApi } from './participantApi';

/**
 * Development fixtures only: the fixture API (src/dev-fixtures) can forget everything it made up, so a demonstration
 * can start again. The real API has no such thing; `demonstrationDataOf` is how a screen finds out, without importing
 * the fixture modules (they stay out of release bundles, see app-shell/services.ts).
 */
export interface DemonstrationData {
  /** Empties every simulated account's data, in memory and in settings; each starts again from its first state. */
  resetDemonstration(): Promise<void>;
}

export function demonstrationDataOf(api: ParticipantApi): DemonstrationData | null {
  const candidate = api as ParticipantApi & Partial<DemonstrationData>;
  return typeof candidate.resetDemonstration === 'function' ? (candidate as DemonstrationData) : null;
}
