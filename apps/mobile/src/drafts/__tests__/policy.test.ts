import { resolveAppMode } from '@/config';

import { describeSaveStatus } from '../saveStatus';

describe('resolveAppMode', () => {
  it('uses development fixtures only when asked for in a development build', () => {
    expect(resolveAppMode('true', true)).toBe('development_fixtures');
    expect(resolveAppMode(' TRUE ', true)).toBe('development_fixtures');
    expect(resolveAppMode('false', true)).toBe('api');
    expect(resolveAppMode(undefined, true)).toBe('api');
  });

  it('never uses fixtures in a release build', () => {
    expect(resolveAppMode('true', false)).toBe('api');
  });
});

describe('describeSaveStatus', () => {
  const status = (phase: Parameters<typeof describeSaveStatus>[0]['phase'], extra: Partial<Parameters<typeof describeSaveStatus>[0]> = {}) =>
    describeSaveStatus({ persistence: 'device', phase, hasAnswers: true, hasConflicts: false, ...extra });

  it('says nothing before any answer exists', () => {
    expect(status('idle', { hasAnswers: false })).toBe('none');
  });

  it('says where an unsent answer is kept', () => {
    expect(status('pending')).toBe('savedOnDevice');
    expect(status('pending', { persistence: 'memory' })).toBe('keptInMemory');
  });

  it('says saved only once the server has the answers', () => {
    expect(status('syncing')).toBe('saving');
    expect(status('synced')).toBe('saved');
    expect(status('idle')).toBe('saved');
  });

  it('says answers wait for the connection, and where they wait', () => {
    expect(status('offline')).toBe('offlineOnDevice');
    expect(status('offline', { persistence: 'memory' })).toBe('offlineInMemory');
  });

  it('admits a failed save, automatic retries included', () => {
    expect(status('waiting')).toBe('retrying');
    expect(status('failed')).toBe('notSaved');
  });

  it('says when the server refused answers', () => {
    expect(status('rejected')).toBe('rejected');
  });

  it('puts a conflict first, whatever else is happening', () => {
    expect(status('synced', { hasConflicts: true })).toBe('conflict');
    expect(status('offline', { hasConflicts: true })).toBe('conflict');
  });
});
