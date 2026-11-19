import type { PublishedVersion, ScoreReport, ScoreReportPart, SessionAnswers } from '@/api/participant/schemas';

import type { StoredReportPart } from './reportStore';

export const SUMMARY_KEY = 'summary';

const sectionKey = (code: string) => `section:${code}`;

/** One piece of a report that can be written now: its key, and the answers it would be written from. */
export interface PlannedPart {
  key: string;
  sectionCode: string | null;
  fingerprint: string;
}

/** What the session's answers allow, in report order, and whether every section is answered. */
export interface ReportPlan {
  parts: PlannedPart[];
  complete: boolean;
}

const pairs = (answers: SessionAnswers) => new Map(answers.responses.map((answer) => [answer.itemId, answer.optionId]));

/**
 * The parts the answers saved on the server allow: one for each section whose required questions are all answered, and
 * the summary once every section is. A fingerprint is only the section's own answers joined, so a changed answer changes
 * it and no other section's. Nothing here reads what an answer means.
 */
export function planReport(version: PublishedVersion, sectionCode: string | null, answers: SessionAnswers): ReportPlan {
  const saved = pairs(answers);
  const sections = [...version.sections]
    .sort((a, b) => a.displayOrder - b.displayOrder)
    .filter((section) => sectionCode === null || section.code === sectionCode);
  const parts: PlannedPart[] = [];
  for (const section of sections) {
    if (section.items.every((item) => !item.isRequired || saved.has(item.id))) {
      const fingerprint = section.items
        .map((item) => `${item.id}:${saved.get(item.id) ?? ''}`)
        .sort()
        .join('|');
      parts.push({ key: sectionKey(section.code), sectionCode: section.code, fingerprint });
    }
  }
  const complete = sections.length > 0 && parts.length === sections.length;
  if (complete) {
    parts.push({ key: SUMMARY_KEY, sectionCode: null, fingerprint: parts.map((part) => part.fingerprint).join('#') });
  }
  return { parts, complete };
}

/** A compact report (D-145): the strengths of the two areas standing highest, the weaknesses of the two lowest, three steps. */
export const REPORT_STRENGTHS = 2;
export const REPORT_WEAKNESSES = 2;
export const REPORT_STEPS = 3;

/**
 * Joins finished parts into a report, or null while any is missing or was written from answers since changed. The total,
 * band and scale are the server's record, the words the model's and the authorities the approved catalogue's. Which areas
 * it keeps follows where the server says each stands (D-114, D-145), never anything read here, so no model and no network
 * are needed, which is what keeps this step instant.
 */
export function assembleReport(
  recorded: Pick<ScoreReport, 'sessionId' | 'language' | 'totalScore' | 'tierCode' | 'scale'>,
  plan: ReportPlan,
  stored: readonly StoredReportPart[],
): ScoreReport | null {
  if (!plan.complete) return null;
  const byKey = new Map(stored.map((part) => [part.key, part]));
  const sections: ScoreReportPart[] = [];
  let summary: { summary: string; modelName: string; generatedAt: string } | null = null;
  for (const planned of plan.parts) {
    const part = byKey.get(planned.key);
    if (!part || part.fingerprint !== planned.fingerprint) return null;
    if (part.kind === 'summary') {
      summary = part.summary;
    } else {
      sections.push(part.part);
    }
  }
  // Sorting is stable, so areas that stand equal keep section order.
  const lowestFirst = [...sections].sort((a, b) => a.standing - b.standing);
  const highestFirst = [...sections].sort((a, b) => b.standing - a.standing);
  const report: Pick<ScoreReport, 'strengths' | 'weaknesses' | 'nextSteps'> = {
    strengths: highestFirst.filter((part) => part.strengths.length > 0).slice(0, REPORT_STRENGTHS).flatMap((part) => part.strengths),
    weaknesses: lowestFirst.filter((part) => part.weaknesses.length > 0).slice(0, REPORT_WEAKNESSES).flatMap((part) => part.weaknesses),
    // The area needing most attention comes first.
    nextSteps: lowestFirst
      .flatMap((part) => part.nextSteps)
      .sort((a, b) => a.standing - b.standing)
      .slice(0, REPORT_STEPS),
  };
  return summary ? { ...recorded, ...report, ...summary } : null;
}
