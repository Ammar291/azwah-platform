import type { Href } from 'expo-router';

import type { AssistantAction } from '@/api/participant/schemas';

/** Where each suggested screen leads (D-122). The codes come from the server; the routes and labels are the app's own. */
export const ACTION_ROUTES: Readonly<Record<AssistantAction, Href>> = {
  sos: '/emergency',
  services: '/services',
  home: '/home',
  resilience: '/resilience',
  help: '/help',
  consultant: '/consultant',
  assistant: '/assistant',
};

/** SOS first (the most prominent), the rest in the order suggested, each once, without the screen already open. */
export function orderedActions(actions: readonly AssistantAction[], here?: AssistantAction): AssistantAction[] {
  const unique = [...new Set(actions)].filter((action) => action !== here);
  return [...unique.filter((action) => action === 'sos'), ...unique.filter((action) => action !== 'sos')];
}
