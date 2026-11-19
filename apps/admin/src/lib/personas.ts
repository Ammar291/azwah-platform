/** The staff roles a development sign-in can act as. Role names match the API's `role` claims (D-48). */
export const PERSONAS = ['researcher', 'program_administrator', 'system_administrator', 'support_coordinator'] as const;

export type Persona = (typeof PERSONAS)[number];

export function isPersona(value: unknown): value is Persona {
  return typeof value === 'string' && (PERSONAS as readonly string[]).includes(value);
}
