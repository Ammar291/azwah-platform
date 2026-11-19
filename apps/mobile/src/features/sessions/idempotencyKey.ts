/**
 * A UUID for the `Idempotency-Key` header (D-54). Uses the platform's crypto.randomUUID when the runtime has it;
 * otherwise a version 4 UUID from Math.random, which is enough for a key whose only job is to be unique per
 * submission of one session by one person.
 */
export function createIdempotencyKey(): string {
  const cryptoApi = (globalThis as { crypto?: { randomUUID?: () => string } }).crypto;
  if (typeof cryptoApi?.randomUUID === 'function') {
    return cryptoApi.randomUUID();
  }
  return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (char) => {
    const random = Math.floor(Math.random() * 16);
    return (char === 'x' ? random : (random & 0x3) | 0x8).toString(16);
  });
}
