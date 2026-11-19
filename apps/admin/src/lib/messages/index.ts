import type { en } from './en';

type Widen<T> = { [K in keyof T]: T[K] extends string ? string : Widen<T[K]> };

/** The shape of every message file: the English keys, with any string as the value. */
export type Messages = Widen<typeof en>;
