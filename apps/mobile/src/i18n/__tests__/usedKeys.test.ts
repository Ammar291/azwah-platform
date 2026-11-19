import en from '../resources/en.json';

// Node's fs and path, typed locally: the app's TypeScript setup deliberately has no Node types.
// eslint-disable-next-line @typescript-eslint/no-require-imports -- Node built-in in a test; no Node types (above)
const { readdirSync, readFileSync, statSync } = require('fs') as {
  readdirSync(path: string): string[];
  readFileSync(path: string, encoding: 'utf8'): string;
  statSync(path: string): { isDirectory(): boolean };
};
// eslint-disable-next-line @typescript-eslint/no-require-imports -- as above
const { join } = require('path') as { join(...parts: string[]): string };
declare const __dirname: string;

type Tree = { [key: string]: string | Tree };

const ROOT = join(__dirname, '..', '..', '..');

function sourceFiles(dir: string): string[] {
  return readdirSync(dir).flatMap((name) => {
    const path = join(dir, name);
    if (statSync(path).isDirectory()) {
      return name === '__tests__' || name === 'node_modules' ? [] : sourceFiles(path);
    }
    return /\.(ts|tsx)$/.test(name) ? [path] : [];
  });
}

const lookup = (path: string): string | Tree | undefined =>
  path.split('.').reduce<string | Tree | undefined>(
    (node, part) => (node && typeof node === 'object' ? node[part] : undefined),
    en as Tree,
  );

const files = [...sourceFiles(join(ROOT, 'app')), ...sourceFiles(join(ROOT, 'src'))];
const sources = files.map((file) => ({ file, text: readFileSync(file, 'utf8') }));

/** `t('a.b')`, `t(x ? 'a.b' : 'c.d')` and key fields such as `labelKey: 'a.b'`. */
const LITERAL_KEYS = [
  /\bt\(\s*'([a-zA-Z][\w.]*)'/g,
  /\bt\(\s*[\w.]+\s*\?\s*'([a-zA-Z][\w.]*)'\s*:\s*'([a-zA-Z][\w.]*)'/g,
  /\b(?:labelKey|actionKey|key):\s*'([a-z][\w]*\.[\w.]+)'/g,
];

/** `t(`a.b.${x}.c`)`: every child of `a.b` must have `c`. */
const TEMPLATE_KEYS = /\bt\(\s*`([a-zA-Z][\w.]*)\.\$\{[^}]+\}((?:\.[\w]+)*)`/g;

describe('translation keys used in code', () => {
  it('exist in the English resources (and so, by the resources test, in Arabic)', () => {
    const missing: string[] = [];
    for (const { file, text } of sources) {
      for (const pattern of LITERAL_KEYS) {
        for (const match of text.matchAll(pattern)) {
          for (const key of match.slice(1).filter(Boolean)) {
            if (key !== 'access_token' && typeof lookup(key) !== 'string') {
              missing.push(`${file}: ${key}`);
            }
          }
        }
      }
    }
    expect(missing).toEqual([]);
  });

  it('exist for every child of a template key', () => {
    const missing: string[] = [];
    for (const { file, text } of sources) {
      for (const match of text.matchAll(TEMPLATE_KEYS)) {
        const [, prefix = '', suffix = ''] = match;
        const parent = lookup(prefix);
        if (!parent || typeof parent !== 'object') {
          missing.push(`${file}: ${prefix}`);
          continue;
        }
        for (const child of Object.keys(parent)) {
          const key = `${prefix}.${child}${suffix}`;
          if (typeof lookup(key) !== 'string') {
            missing.push(`${file}: ${key}`);
          }
        }
      }
    }
    expect(missing).toEqual([]);
  });

  it('cover every value the API can send for the enums shown to people', () => {
    const {
      assignmentStatusSchema,
      employmentStatusSchema,
      relationshipSchema,
      consentTypeSchema,
      resultStateSchema,
      privacyRequestTypeSchema,
      privacyRequestStatusSchema,
      privacyRequestResolutionSchema,
      recommendationSchema,
    } = jest.requireActual<typeof import('@/api/participant/schemas')>('@/api/participant/schemas');
    const expectAll = (prefix: string, values: readonly string[], suffix = '') =>
      expect(values.filter((value) => typeof lookup(`${prefix}.${value}${suffix}`) !== 'string')).toEqual([]);

    expectAll('assignmentStatus', assignmentStatusSchema.options);
    expectAll('employmentStatus', employmentStatusSchema.options);
    expectAll('relationship', relationshipSchema.options);
    expectAll('consent.types', consentTypeSchema.options, '.title');
    expectAll('result.state', resultStateSchema.options);
    expectAll('profile.data.requests.types', privacyRequestTypeSchema.options, '.title');
    expectAll('profile.data.requests.types', privacyRequestTypeSchema.options, '.description');
    expectAll('profile.data.requests.status', privacyRequestStatusSchema.options);
    expectAll('profile.data.requests.resolution', privacyRequestResolutionSchema.options);
    expectAll('home.nextSteps.referral', recommendationSchema.shape.referralStatus.unwrap().options);
  });

  it('describe every error kind', () => {
    const kinds = [
      'not_configured',
      'network',
      'timeout',
      'server',
      'invalid_response',
      'bad_request',
      'unauthorized',
      'forbidden',
      'not_found',
      'conflict',
      'payload_too_large',
      'rate_limited',
      'unexpected',
    ];
    expect(kinds.filter((kind) => typeof lookup(`errors.kinds.${kind}.message`) !== 'string')).toEqual([]);
  });
});
