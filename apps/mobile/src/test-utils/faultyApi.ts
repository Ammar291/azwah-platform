import { ApiError } from '@/api/client';
import type { ParticipantApi } from '@/api/participant/participantApi';

type Method = keyof ParticipantApi;
type Fault = { kind: 'fail'; error: unknown } | { kind: 'lose' };

/**
 * Wraps a participant API so a test can script what goes wrong, one call at a time:
 * - `fail(method)`: the next call never reaches the server (nothing changes) and rejects with `error`;
 * - `lose(method)`: the server carries out the next call, then the answer is lost, as when a connection drops.
 * Every call is counted, so a test can prove a retry did not repeat a change.
 */
export function withFaults(inner: ParticipantApi) {
  const faults = new Map<Method, Fault[]>();
  const calls: { method: Method; args: unknown[] }[] = [];

  const api = Object.fromEntries(
    (Object.keys(inner) as Method[]).map((method) => {
      const call = inner[method] as (...args: unknown[]) => Promise<unknown>;
      return [
        method,
        async (...args: unknown[]) => {
          calls.push({ method, args });
          const fault = faults.get(method)?.shift();
          if (fault?.kind === 'fail') {
            throw fault.error;
          }
          const result = await call(...args);
          if (fault?.kind === 'lose') {
            throw new ApiError('network');
          }
          return result;
        },
      ];
    }),
  ) as unknown as ParticipantApi;

  const add = (method: Method, fault: Fault) => faults.set(method, [...(faults.get(method) ?? []), fault]);

  return {
    api,
    calls,
    fail(method: Method, error: unknown = new ApiError('network')) {
      add(method, { kind: 'fail', error });
    },
    lose(method: Method) {
      add(method, { kind: 'lose' });
    },
    count(method: Method) {
      return calls.filter((call) => call.method === method).length;
    },
    argsOf(method: Method) {
      return calls.filter((call) => call.method === method).map((call) => call.args);
    },
  };
}
