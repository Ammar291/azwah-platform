/**
 * A member ID shortened to its last 12 hex digits, as the assessment table shortens household IDs: version 7 IDs
 * begin with their creation time, so a prefix repeats between people created close together.
 */
export function shortMemberId(memberId: string): string {
  return memberId.slice(-12);
}
