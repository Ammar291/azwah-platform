import { FIXTURE_SUPPORT_AUTHORITIES } from '@/dev-fixtures/fixtureAssistant';

// The graph the API ships: it lives outside the app, so this is what keeps the app's symbols in step with it.
import graph from '../../../../../../backend/FamilyResilience.Infrastructure/Content/support-knowledge-graph.v1.json';
import { authorityMarkSvg, hasAuthorityMark } from '../authorityMarks';

const codes = graph.authorities.map((authority) => authority.code);

describe('the symbols for organisations', () => {
  it('has a drawing for every organisation in the knowledge graph', () => {
    expect(codes).toHaveLength(13);
    expect(codes.filter((code) => !hasAuthorityMark(code))).toEqual([]);
  });

  it('draws each organisation differently', () => {
    expect(new Set(codes.map((code) => authorityMarkSvg(code))).size).toBe(codes.length);
  });

  it('loads nothing and writes no text, so a mark cannot reach the network or spell a borrowed name', () => {
    for (const code of codes) {
      const svg = authorityMarkSvg(code);
      expect(svg).not.toMatch(/<(text|image|use|script|a)\b/);
      expect(svg).not.toMatch(/href=/);
    }
  });

  it('falls back to a plain tile for an unknown code, and never puts the code in the markup', () => {
    const svg = authorityMarkSvg('<script>alert(1)</script>');

    expect(hasAuthorityMark('constructor')).toBe(false);
    expect(svg).not.toContain('script');
    expect(svg).toContain('<circle');
  });

  it('keeps the development fixtures on the real codes', () => {
    expect(FIXTURE_SUPPORT_AUTHORITIES.map((authority) => authority.code)).toEqual(codes);
  });
});
