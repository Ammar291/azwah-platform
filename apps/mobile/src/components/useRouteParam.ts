import { useLocalSearchParams } from 'expo-router';

/**
 * One route or query parameter as a string. IDs in routes are only handles for the server, which decides access
 * from the token and its own records and answers 404 for anything the caller may not see.
 */
export function useRouteParam(name: string): string | undefined {
  const value = useLocalSearchParams()[name];
  return Array.isArray(value) ? value[0] : value;
}
