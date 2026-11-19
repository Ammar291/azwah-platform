import { createMemorySecretStore } from '@/storage/testing/memorySecretStore';
import { createNodeAesGcm } from '@/storage/testing/nodeAesGcm';

import { fromBase64, toBase64 } from '../base64';
import { createFieldCipher, DRAFT_KEY_NAME, UnreadableValueError } from '../fieldCipher';
import { resolveDeviceProtection } from '../resolveDeviceProtection';
import { utf8Decode, utf8Encode } from '../utf8';

// Node's encoders, as references; the app's TypeScript setup has no Node or DOM types.
declare const TextEncoder: new () => { encode(text: string): Uint8Array };
declare const Buffer: { from(bytes: number[]): { toString(encoding: 'base64'): string } };

const node = (text: string) => Array.from(new TextEncoder().encode(text));

describe('utf8', () => {
  it.each(['', 'plain ascii', 'العربية والإنجليزية', 'emoji 🧭 and ✓', '0199e0a1-0000-7000-8000-000000000100'])(
    'encodes %p as Node does and decodes it back',
    (text) => {
      expect(Array.from(utf8Encode(text))).toEqual(node(text));
      expect(utf8Decode(utf8Encode(text))).toBe(text);
    },
  );

  it('refuses bytes that are not UTF-8', () => {
    expect(() => utf8Decode(new Uint8Array([0xc3]))).toThrow();
    expect(() => utf8Decode(new Uint8Array([0xff, 0x41]))).toThrow();
  });
});

describe('base64', () => {
  it.each([[[]], [[0]], [[1, 2]], [[255, 254, 253]], [Array.from({ length: 300 }, (_, i) => i % 256)]])(
    'round-trips %#',
    (bytes) => {
      const encoded = toBase64(new Uint8Array(bytes));
      expect(encoded).toBe(Buffer.from(bytes).toString('base64'));
      expect(Array.from(fromBase64(encoded))).toEqual(bytes);
    },
  );

  it('refuses text that is not base64', () => {
    expect(() => fromBase64('a$b=')).toThrow();
    expect(() => fromBase64('abc')).toThrow();
  });
});

describe('field cipher', () => {
  const setup = () => {
    const secrets = createMemorySecretStore();
    const cipher = createFieldCipher({ aes: createNodeAesGcm(), secrets });
    return { secrets, cipher };
  };

  it('seals a value so the stored text never contains it, and opens it again', async () => {
    const { cipher } = setup();
    const optionId = '0199e0a1-0000-7000-8000-000000000105';

    const sealed = await cipher.encrypt(optionId, 'draft/s1/i1');

    expect(sealed).toMatch(/^v1:[A-Za-z0-9+/]+=*$/);
    expect(sealed).not.toContain(optionId);
    await expect(cipher.decrypt(sealed, 'draft/s1/i1')).resolves.toBe(optionId);
  });

  it('uses a fresh nonce, so the same value never seals the same way twice', async () => {
    const { cipher } = setup();

    const first = await cipher.encrypt('o1', 'draft/s1/i1');
    const second = await cipher.encrypt('o1', 'draft/s1/i1');

    expect(first).not.toBe(second);
  });

  it('keeps one random 256-bit key in the keystore and nowhere else', async () => {
    const { cipher, secrets } = setup();

    await cipher.encrypt('o1', 'draft/s1/i1');

    const key = await secrets.get(DRAFT_KEY_NAME);
    expect(key && fromBase64(key)).toHaveLength(32);
  });

  it('refuses a value moved to another row (bound by its context)', async () => {
    const { cipher } = setup();
    const sealed = await cipher.encrypt('o1', 'draft/s1/i1');

    await expect(cipher.decrypt(sealed, 'draft/s1/i2')).rejects.toBeInstanceOf(UnreadableValueError);
    await expect(cipher.decrypt(sealed, 'draft/s2/i1')).rejects.toBeInstanceOf(UnreadableValueError);
  });

  it('refuses a tampered value', async () => {
    const { cipher } = setup();
    const sealed = await cipher.encrypt('o1', 'draft/s1/i1');
    const bytes = fromBase64(sealed.slice(3));
    bytes[14] = (bytes[14] ?? 0) ^ 0x01;

    await expect(cipher.decrypt(`v1:${toBase64(bytes)}`, 'draft/s1/i1')).rejects.toBeInstanceOf(
      UnreadableValueError,
    );
    await expect(cipher.decrypt('v2:abcd', 'draft/s1/i1')).rejects.toBeInstanceOf(UnreadableValueError);
    await expect(cipher.decrypt('not sealed', 'draft/s1/i1')).rejects.toBeInstanceOf(UnreadableValueError);
  });

  it('reads values sealed before a restart with the key kept in the keystore', async () => {
    const secrets = createMemorySecretStore();
    const sealed = await createFieldCipher({ aes: createNodeAesGcm(), secrets }).encrypt('o1', 'draft/s1/i1');

    const afterRestart = createFieldCipher({ aes: createNodeAesGcm(), secrets });

    await expect(afterRestart.decrypt(sealed, 'draft/s1/i1')).resolves.toBe('o1');
  });

  it('can never read a value again once its key is destroyed, and seals new values with a new key', async () => {
    const { cipher, secrets } = setup();
    const sealed = await cipher.encrypt('o1', 'draft/s1/i1');
    const oldKey = await secrets.get(DRAFT_KEY_NAME);

    await cipher.destroyKey();

    expect(await secrets.get(DRAFT_KEY_NAME)).toBeNull();
    await expect(cipher.decrypt(sealed, 'draft/s1/i1')).rejects.toBeInstanceOf(UnreadableValueError);
    const next = await cipher.encrypt('o2', 'draft/s1/i1');
    expect(await secrets.get(DRAFT_KEY_NAME)).not.toBe(oldKey);
    await expect(cipher.decrypt(next, 'draft/s1/i1')).resolves.toBe('o2');
  });

  it('gives a second key of its own on the same keystore: destroying either leaves the other', async () => {
    const { cipher, secrets } = setup();
    const other = cipher.withKey('azwah.other.key.v1');
    const draft = await cipher.encrypt('o1', 'draft/s1/i1');
    const kept = await other.encrypt('o2', 'draft/s1/i1');

    expect(await secrets.get('azwah.other.key.v1')).not.toBe(await secrets.get(DRAFT_KEY_NAME));
    await expect(other.decrypt(draft, 'draft/s1/i1')).rejects.toBeInstanceOf(UnreadableValueError);
    await cipher.destroyKey();
    await expect(other.decrypt(kept, 'draft/s1/i1')).resolves.toBe('o2');
    const afterRestart = createFieldCipher({ aes: createNodeAesGcm(), secrets }).withKey('azwah.other.key.v1');
    await expect(afterRestart.decrypt(kept, 'draft/s1/i1')).resolves.toBe('o2');
    await other.destroyKey();
    expect(await secrets.get('azwah.other.key.v1')).toBeNull();
  });

  it('treats a value as unreadable when the keystore lost its key', async () => {
    const { cipher, secrets } = setup();
    const sealed = await cipher.encrypt('o1', 'draft/s1/i1');
    await secrets.remove(DRAFT_KEY_NAME);

    const afterReset = createFieldCipher({ aes: createNodeAesGcm(), secrets });

    await expect(afterReset.decrypt(sealed, 'draft/s1/i1')).rejects.toBeInstanceOf(UnreadableValueError);
  });

  it('makes one key even when several values are sealed at once', async () => {
    const { cipher, secrets } = setup();
    const set = jest.spyOn(secrets, 'set');

    const sealed = await Promise.all(['a', 'b', 'c'].map((value) => cipher.encrypt(value, `draft/s1/${value}`)));

    expect(set).toHaveBeenCalledTimes(1);
    await expect(Promise.all(sealed.map((value, i) => cipher.decrypt(value, `draft/s1/${'abc'[i]}`)))).resolves.toEqual([
      'a',
      'b',
      'c',
    ]);
  });
});

describe('resolveDeviceProtection', () => {
  it('gives a working cipher when the keystore and AES-GCM work', async () => {
    const cipher = await resolveDeviceProtection({ aes: createNodeAesGcm, secrets: createMemorySecretStore });

    expect(cipher).not.toBeNull();
    await expect(cipher?.decrypt(await cipher.encrypt('x', 'probe'), 'probe')).resolves.toBe('x');
  });

  it('gives nothing when the keystore is not available, so drafts stay in memory', async () => {
    const secrets = createMemorySecretStore();
    jest.spyOn(secrets, 'isAvailable').mockResolvedValue(false);

    await expect(resolveDeviceProtection({ aes: createNodeAesGcm, secrets: () => secrets })).resolves.toBeNull();
  });

  it('gives nothing when the AES module is missing or broken', async () => {
    await expect(
      resolveDeviceProtection({
        aes: () => {
          throw new Error('Cannot find native module');
        },
        secrets: createMemorySecretStore,
      }),
    ).resolves.toBeNull();

    const broken = createNodeAesGcm();
    jest.spyOn(broken, 'open').mockResolvedValue(new Uint8Array([1, 2, 3]));
    await expect(resolveDeviceProtection({ aes: () => broken, secrets: createMemorySecretStore })).resolves.toBeNull();
  });

  it('gives nothing when the key cannot be stored', async () => {
    const secrets = createMemorySecretStore();
    jest.spyOn(secrets, 'set').mockRejectedValue(new Error('Keychain locked'));

    await expect(resolveDeviceProtection({ aes: createNodeAesGcm, secrets: () => secrets })).resolves.toBeNull();
  });
});
