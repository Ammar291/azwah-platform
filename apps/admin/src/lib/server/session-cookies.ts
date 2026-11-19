import 'server-only';

import type { NextResponse } from 'next/server';

import type { AdminConfig } from './config';
import { cookieNamesFor, joinCookie, splitCookie } from './cookie-chunks';
import { sessionCookieName } from './session';

export interface CookieReader {
  get(name: string): { value: string } | undefined;
}

const read = (cookies: CookieReader) => (name: string) => cookies.get(name)?.value;

function baseOptions(config: AdminConfig) {
  return { httpOnly: true, secure: config.secureCookies, sameSite: 'lax' as const, path: '/' };
}

/** The sealed session from the request's cookies, joined from chunks when needed. */
export function readSessionValue(cookies: CookieReader, config: AdminConfig): string | undefined {
  return joinCookie(sessionCookieName(config.secureCookies), read(cookies));
}

/** Writes the sealed session, and expires any chunk of an older, longer value. */
export function writeSessionCookies(response: NextResponse, cookies: CookieReader, sealed: string, config: AdminConfig): void {
  const name = sessionCookieName(config.secureCookies);
  const chunks = splitCookie(name, sealed);
  const written = new Set(chunks.map((chunk) => chunk.name));
  for (const stale of cookieNamesFor(name, read(cookies))) {
    if (!written.has(stale)) response.cookies.set(stale, '', { ...baseOptions(config), maxAge: 0 });
  }
  for (const chunk of chunks) {
    response.cookies.set(chunk.name, chunk.value, { ...baseOptions(config), maxAge: config.sessionMaxAgeSeconds });
  }
}

/** Expires the session cookie and all its chunks. */
export function clearSessionCookies(response: NextResponse, cookies: CookieReader, config: AdminConfig): void {
  const name = sessionCookieName(config.secureCookies);
  const names = new Set([name, ...cookieNamesFor(name, read(cookies))]);
  for (const stale of names) response.cookies.set(stale, '', { ...baseOptions(config), maxAge: 0 });
}

export function transactionCookieName(secure: boolean): string {
  return secure ? '__Host-azwah_auth_tx' : 'azwah_auth_tx';
}

export function writeTransactionCookie(response: NextResponse, value: string, config: AdminConfig, maxAge: number): void {
  response.cookies.set(transactionCookieName(config.secureCookies), value, { ...baseOptions(config), maxAge });
}

export function clearTransactionCookie(response: NextResponse, config: AdminConfig): void {
  response.cookies.set(transactionCookieName(config.secureCookies), '', { ...baseOptions(config), maxAge: 0 });
}
