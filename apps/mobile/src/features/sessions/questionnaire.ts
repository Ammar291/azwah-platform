import { z } from 'zod';

import type { PublishedItem, PublishedVersion, SingleChoiceItem } from '@/api/participant/schemas';

/*
 * The questionnaire as the app uses it: the published version's items for one session, and the rules an answer must
 * meet before it is sent. Everything comes from the server's schema; nothing here is questionnaire content.
 */

const byOrder = <T extends { displayOrder: number }>(a: T, b: T) => a.displayOrder - b.displayOrder;

/** The questions a session asks, with their options, exactly as the published version gives them. */
export function selectItems(version: PublishedVersion, sectionCode: string | null) {
  const sections = [...version.sections].sort(byOrder).filter((s) => sectionCode === null || s.code === sectionCode);
  return {
    sectionTitle: sectionCode === null ? null : (sections[0]?.title ?? null),
    items: sections.flatMap((section) =>
      [...section.items].sort(byOrder).map((item) => ({ ...item, options: [...item.options].sort(byOrder) })),
    ),
  };
}

/**
 * The item types this app can show and send (decision M-43). Each has a renderer in QuestionRenderer and an answer
 * rule in answerSchemaFor. The contract permits `single_choice` only.
 */
export const SUPPORTED_ITEM_TYPES = ['single_choice'] as const;

export const isSupportedItem = (item: PublishedItem): item is SingleChoiceItem => item.itemType === 'single_choice';

/** A questionnaire can be answered only when the app supports every one of its questions. */
export const isAnswerable = (items: readonly PublishedItem[]) => items.length > 0 && items.every(isSupportedItem);

/** The answer to a single-choice item: one of that item's own options. */
export function answerSchemaFor(item: SingleChoiceItem) {
  const options = new Set(item.options.map((option) => option.id));
  return z.string().refine((optionId) => options.has(optionId));
}

export interface AnswerCheck {
  /** Required questions with no answer, in questionnaire order. */
  missingRequired: SingleChoiceItem[];
  /** Questions whose answer is not one of their options, or that are not in this questionnaire. */
  invalid: string[];
  answered: number;
  total: number;
}

/**
 * Checks answers (itemId → optionId) against the questionnaire with a Zod schema built from it: each answer must be
 * an option of its question, no answer may name a question outside it, and every required question needs one.
 */
export function checkAnswers(items: readonly PublishedItem[], answers: Readonly<Record<string, string>>): AnswerCheck {
  const supported = items.filter(isSupportedItem);
  const shape = Object.fromEntries(
    supported.map((item) => [item.id, item.isRequired ? answerSchemaFor(item) : answerSchemaFor(item).optional()]),
  );
  const result = z.strictObject(shape).safeParse(answers);
  const missingRequired = supported.filter((item) => item.isRequired && answers[item.id] === undefined);
  const missing = new Set(missingRequired.map((item) => item.id));
  const invalid = new Set<string>();
  for (const issue of result.success ? [] : result.error.issues) {
    if (issue.code === 'unrecognized_keys') {
      issue.keys.forEach((key) => invalid.add(key));
    } else {
      const itemId = String(issue.path[0] ?? '');
      if (itemId && !missing.has(itemId)) {
        invalid.add(itemId);
      }
    }
  }
  return {
    missingRequired,
    invalid: [...invalid],
    answered: supported.filter((item) => answers[item.id] !== undefined && !invalid.has(item.id)).length,
    total: items.length,
  };
}

/** Whether a draft could be sent: its question is in the questionnaire and its option belongs to that question. */
export function isValidAnswer(items: readonly PublishedItem[], itemId: string, optionId: string): boolean {
  const item = items.find((candidate) => candidate.id === itemId);
  return item !== undefined && isSupportedItem(item) && answerSchemaFor(item).safeParse(optionId).success;
}
