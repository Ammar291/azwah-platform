import type {
  AuthenticationPrompt,
  DeviceAuthentication,
  DeviceAuthenticationOutcome,
} from '../deviceAuthentication';

type Answer = DeviceAuthenticationOutcome | Promise<DeviceAuthenticationOutcome>;

/**
 * A phone lock for tests: available unless told otherwise, and confirming the person unless the next answers are
 * scripted with `willAnswer`. Every prompt is recorded.
 */
export function createFakeDeviceAuthentication({ available = true }: { available?: boolean } = {}) {
  let isAvailable = available;
  const answers: Answer[] = [];
  const prompts: AuthenticationPrompt[] = [];

  const authentication: DeviceAuthentication = {
    isAvailable: async () => isAvailable,
    authenticate: async (prompt) => {
      prompts.push(prompt);
      return answers.shift() ?? 'success';
    },
  };

  return {
    ...authentication,
    prompts,
    setAvailable(value: boolean) {
      isAvailable = value;
    },
    /** The answers to the next prompts, in order; a promise holds the prompt open until it settles. */
    willAnswer(...next: Answer[]) {
      answers.push(...next);
    },
  };
}

export type FakeDeviceAuthentication = ReturnType<typeof createFakeDeviceAuthentication>;
