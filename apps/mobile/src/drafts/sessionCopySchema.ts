import { z } from 'zod';

import {
  assignmentSchema,
  publishedVersionSchema,
  sessionAnswersSchema,
  sessionSchema,
} from '@/api/participant/schemas';

/** Copies are read back through the same schemas as the API, so an app update that changes a shape drops old copies. */
export const sessionCopyContentSchema = z.object({
  session: sessionSchema,
  assignment: assignmentSchema,
  version: publishedVersionSchema,
});

export { sessionAnswersSchema };
