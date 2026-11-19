import type {
  AssistantAction,
  AssistantMessage,
  AssistantReplyRequest,
  AssistantSource,
  ReportAuthority,
  SafetyNotice,
  ScoreReport,
  ScoreReportPart,
  ScoreReportSummary,
  SupportAuthority,
  SupportResource,
} from '@/api/participant/schemas';

/*
 * DEVELOPMENT FIXTURES ONLY. No AI model is connected in development fixtures: the assistant's "reply" says so and
 * repeats the question, so the chat can be tried end to end, and the support directory holds three synthetic entries.
 * Nothing here is real advice or a real organisation, and every text says so in plain words a demonstration can
 * show: each reply calls itself simulated (the assistant screen carries a notice too), and each directory entry is
 * named an example, says in its summary that it is not a real service, and links to the reserved example.org. The
 * suggested screens, sources and memory below are picked by fixed word lists, only so the app's buttons, cards and
 * memory can be seen: the real assistant decides them on the server (D-122, D-123, D-126).
 */

const QUOTE_LIMIT = 120;

/** The simulated reply to the last message, in the conversation's language. */
export function simulatedAssistantReply(messages: readonly AssistantMessage[], language: 'en' | 'ar'): string {
  const question = lastQuestion(messages);
  const quoted = question.length > QUOTE_LIMIT ? `${question.slice(0, QUOTE_LIMIT)}…` : question;
  return language === 'ar'
    ? `هذا ردّ محاكى: لا يوجد نموذج ذكاء اصطناعي متصل. عند ربط مساعد عزوة سيجيب عن سؤالك مستعينًا بما تحفظه عزوة عن أسرتك. سؤالك: «${quoted}»`
    : `This is a simulated reply: no AI model is connected. Once the Azwah assistant is connected, it will answer using what Azwah holds about your family. You asked: "${quoted}"`;
}

function lastQuestion(messages: readonly AssistantMessage[]): string {
  return [...messages].reverse().find((message) => message.role === 'user')?.text.trim() ?? '';
}

/** A tiny, obvious list (simulation only): the real server adds SOS from a moderation check (D-126). */
const DANGER_WORDS = ['suicide', 'kill myself', 'hurt myself', 'hits me', 'unsafe', 'violence', 'انتحار', 'أؤذي نفسي', 'يضربني', 'عنف'];
const MONEY_WORDS = ['debt', 'budget', 'دين', 'ديون', 'ميزانية'];

/** The server refreshes the memory from this many messages (D-123). */
const MEMORY_FROM_MESSAGES = 30;

const mentions = (text: string, words: readonly string[]) => {
  const lower = text.toLowerCase();
  return words.some((word) => lower.includes(word));
};

/** A directory entry as a reply names it, in the request's language. */
export function sourceOf(resource: SupportResource, language: 'en' | 'ar'): AssistantSource {
  return {
    id: resource.id,
    name: language === 'ar' ? resource.nameAr : resource.nameEn,
    provider: language === 'ar' ? resource.providerNameAr : resource.providerNameEn,
    url: resource.url,
  };
}

/**
 * SIMULATED, development fixtures only: the real server screens text with its own approved phrase list (D-135). This
 * stand-in matches one phrase so the crisis support card can be seen without a server.
 */
export function simulatedSafetyNotice(text: string): SafetyNotice | null {
  return text.toLowerCase().includes('kill myself') ? 'crisis_support' : null;
}

/** The whole simulated v2 reply: text, suggested screens, named directory entries and (for long chats) a memory. */
export function simulatedAssistantAnswer({ language, messages }: AssistantReplyRequest): {
  reply: string;
  actions: AssistantAction[];
  sources: AssistantSource[];
  memory: string | null;
  safetyNotice: SafetyNotice | null;
} {
  const question = lastQuestion(messages);
  const safetyNotice = simulatedSafetyNotice(question);
  const actions: AssistantAction[] = safetyNotice || mentions(question, DANGER_WORDS) ? ['sos'] : [];
  const sources = mentions(question, MONEY_WORDS)
    ? FIXTURE_SUPPORT_RESOURCES.filter((resource) => resource.categories.includes('financial')).map((resource) =>
        sourceOf(resource, language),
      )
    : [];
  const memory =
    messages.length >= MEMORY_FROM_MESSAGES
      ? language === 'ar'
        ? `[ملخص محاكى للتطوير] تبادلت الأسرة ${messages.length} رسالة مع المساعد.`
        : `[Simulated summary for development] The family exchanged ${messages.length} messages with the assistant.`
      : null;
  return { reply: simulatedAssistantReply(messages, language), actions, sources, memory, safetyNotice };
}

/**
 * A report in the real one's shape that says plainly it is simulated, wherever it appears (the PDF too), so that no one
 * can mistake it for an assessment. It uses no answers and gives no advice.
 */
export function simulatedScoreReport(
  recorded: Pick<ScoreReport, 'sessionId' | 'totalScore' | 'tierCode' | 'scale'>,
  language: 'en' | 'ar',
  generatedAt: string,
): ScoreReport {
  const words =
    language === 'ar'
      ? {
          summary: '[تقرير محاكى للتطوير] لا يوجد نموذج ذكاء اصطناعي متصل، وهذا النص لا يستند إلى إجابات أسرتك.',
          title: 'مثال نائب',
          detail: 'يظهر هنا ما يكتبه النموذج عند ربطه.',
          step: 'خطوة نائبة للتطوير، وليست نصيحة.',
        }
      : {
          summary: "[Simulated report for development] No AI model is connected, and this text isn't based on your family's answers.",
          title: 'Placeholder example',
          detail: 'What the model writes appears here once one is connected.',
          step: 'A placeholder step for development, not advice.',
        };
  const point = { title: words.title, detail: words.detail };
  return {
    ...recorded,
    language,
    summary: words.summary,
    strengths: [point],
    weaknesses: [point],
    nextSteps: [
      {
        sectionCode: 'dev_placeholder',
        area: words.title,
        reason: words.detail,
        steps: [words.step],
        standing: 0.5,
        authorities: [placeholderAuthority(language)],
      },
    ],
    modelName: 'simulated',
    generatedAt,
  };
}

/** Not a real authority: it shows how a step looks once the owner has added theirs (D-114). */
function placeholderAuthority(language: 'en' | 'ar'): ReportAuthority {
  const words =
    language === 'ar'
      ? {
          name: '[عنصر نائب للتطوير] جهة حكومية',
          purpose: 'ليست جهة حقيقية. تظهر هنا الجهات المعتمدة عند إضافتها.',
          step: 'خطوة إجراء نائبة، وليست إجراءً حقيقيًا.',
          document: 'مستند نائب.',
          need: 'حاجة نائبة',
        }
      : {
          name: '[DEV PLACEHOLDER] Government authority',
          purpose: 'Not a real authority. The approved ones appear here once they are added.',
          step: 'A placeholder procedure step, not a real procedure.',
          document: 'A placeholder document.',
          need: 'A placeholder need',
        };
  return {
    code: 'dev_placeholder_authority',
    kind: 'government',
    name: words.name,
    purpose: words.purpose,
    emirates: ['abu_dhabi'],
    services: [
      {
        code: 'dev_placeholder_service',
        name: words.name,
        summary: words.purpose,
        need: words.need,
        procedure: [words.step, words.step],
        documents: [words.document],
        website: 'https://example.org/azwah-dev/service',
        via: null,
      },
    ],
    website: 'https://example.org/azwah-dev/authority',
    phone: '000',
    checkedOn: '2026-01-01',
  };
}

const devText = (en: string, ar: string) => ({ en: `[DEV PLACEHOLDER] ${en}`, ar: `[عنصر نائب للتطوير] ${ar}` });

/**
 * The real graph's organisation codes with placeholder words, so the All services page and its symbols can be seen without a
 * server. The real facts live only in the API's knowledge graph; `fixtureAuthorities.test.ts` keeps the codes matched to it.
 */
export const FIXTURE_SUPPORT_AUTHORITIES: readonly SupportAuthority[] = [
  ['family_care_authority', 'government', 'Family care', 'رعاية الأسرة', 'abu_dhabi'],
  ['zayed_authority', 'government', 'People of Determination', 'أصحاب الهمم', 'abu_dhabi'],
  ['sahatna', 'government', 'Health app', 'تطبيق صحي', 'abu_dhabi'],
  ['seha', 'public_provider', 'Hospitals', 'مستشفيات', 'abu_dhabi'],
  ['tamm', 'government', 'Government services', 'خدمات حكومية', 'abu_dhabi'],
  ['community_development_authority', 'government', 'Community development', 'تنمية المجتمع', 'dubai'],
  ['sharjah_statistics_community_development', 'government', 'Statistics', 'إحصاء', 'sharjah'],
  ['family_development_foundation', 'government', 'Family development', 'تنمية الأسرة', 'abu_dhabi'],
  ['tarmeem', 'private', 'Orthopaedic hospital', 'مستشفى عظام', 'abu_dhabi'],
  ['supreme_council_motherhood_childhood', 'government', 'Motherhood and childhood', 'الأمومة والطفولة', 'uae'],
  ['national_academy_childhood_development', 'government', 'Childhood academy', 'أكاديمية الطفولة', 'abu_dhabi'],
  ['acpn', 'private', 'Psychiatry centre', 'مركز نفسي', 'abu_dhabi'],
  ['municipal_presence_centres', 'government', 'Municipal centres', 'مراكز بلدية', 'abu_dhabi'],
].map(([code, kind, en, ar, emirate]) => ({
  code: code as string,
  kind: kind as SupportAuthority['kind'],
  name: devText(en as string, ar as string),
  purpose: devText('Not real details. The API holds the real ones.', 'ليست تفاصيل حقيقية. تحفظ الواجهة البرمجية التفاصيل الحقيقية.'),
  emirates: [emirate as SupportAuthority['emirates'][number]],
  website: 'https://example.org/azwah-dev/authority',
  phone: '000',
  services: [
    {
      code: `${code as string}_dev_service`,
      name: devText('A service', 'خدمة'),
      summary: devText('Not a real service.', 'ليست خدمة حقيقية.'),
      procedure: [devText('A placeholder step.', 'خطوة نائبة.')],
      documents: [],
      website: null,
      via: null,
    },
  ],
  checkedOn: '2026-01-01',
}));

/** One area's text, in the real one's shape, plainly simulated. It uses no answers and gives no advice. */
export function simulatedScoreReportPart(sectionCode: string, language: 'en' | 'ar', generatedAt: string): ScoreReportPart {
  const { strengths, weaknesses, nextSteps, modelName } = simulatedScoreReport(
    { sessionId: '', totalScore: 0, tierCode: '', scale: { minimum: 0, maximum: 0, bands: [] } },
    language,
    generatedAt,
  );
  return {
    sectionCode,
    language,
    strengths,
    weaknesses,
    nextSteps: nextSteps.map((step) => ({ ...step, sectionCode })),
    modelName,
    generatedAt,
    standing: 0.5,
  };
}

/** A report's summary, in the real one's shape, plainly simulated. */
export function simulatedScoreReportSummary(language: 'en' | 'ar', generatedAt: string): ScoreReportSummary {
  const { summary, modelName } = simulatedScoreReport(
    { sessionId: '', totalScore: 0, tierCode: '', scale: { minimum: 0, maximum: 0, bands: [] } },
    language,
    generatedAt,
  );
  return { language, summary, modelName, generatedAt };
}

/** Name, summary and provider. */
type Texts = [name: string, summary: string, provider: string];

const entry = (id: string, code: string, en: Texts, ar: Texts, categories: string[]): SupportResource => ({
  id,
  code,
  versionNumber: 1,
  nameEn: `Example ${en[0]} (sample entry)`,
  summaryEn: `Sample entry, not a real service: ${en[1]}`,
  providerNameEn: en[2],
  nameAr: `${ar[0]} (مثال توضيحي)`,
  summaryAr: `مثال توضيحي وليس خدمة حقيقية: ${ar[1]}`,
  providerNameAr: ar[2],
  url: `https://example.org/azwah-dev/${code}`,
  categories,
  audiences: ['families'],
  serviceLanguages: ['ar', 'en'],
});

export const FIXTURE_SUPPORT_RESOURCES: readonly SupportResource[] = [
  entry(
    '0199e0a1-0000-7000-9000-000000002001',
    'dev_family_counselling',
    ['family counselling service', 'talk through family worries with a counsellor.', 'Sample provider A'],
    ['خدمة إرشاد أسري', 'تحدّث مع مرشد عن هموم الأسرة.', 'جهة نموذجية أ'],
    ['counselling'],
  ),
  entry(
    '0199e0a1-0000-7000-9000-000000002002',
    'dev_debt_advice',
    ['household budget and debt advice', 'help planning a budget and handling debts.', 'Sample provider B'],
    ['استشارات ميزانية الأسرة والديون', 'مساعدة في تخطيط الميزانية والتعامل مع الديون.', 'جهة نموذجية ب'],
    ['financial'],
  ),
  entry(
    '0199e0a1-0000-7000-9000-000000002003',
    'dev_parenting_classes',
    ['parenting classes', 'short courses for parents of young children.', 'Sample provider C'],
    ['دورات في التربية', 'دورات قصيرة لآباء وأمهات الأطفال الصغار.', 'جهة نموذجية ج'],
    ['parenting'],
  ),
];

/** Every entry whose name, summary or provider holds each typed word, in either language. */
export function searchFixtureResources(text: string): SupportResource[] {
  const words = text.toLowerCase().split(/\s+/).filter(Boolean);
  return FIXTURE_SUPPORT_RESOURCES.filter((resource) => {
    const haystack = [
      resource.nameEn,
      resource.summaryEn,
      resource.providerNameEn,
      resource.nameAr,
      resource.summaryAr,
      resource.providerNameAr,
    ]
      .join(' ')
      .toLowerCase();
    return words.length > 0 && words.every((word) => haystack.includes(word));
  });
}
