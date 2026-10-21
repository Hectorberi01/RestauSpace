'use client';
import {money} from '@/lib/format';
import {Badge} from './ui';

/** Écart de caisse : juste, excédent (ambre) ou manque (rouge). */
export function Difference({value, currency}: {value: number; currency: string}) {
  if (value === 0) return <Badge tone="green">Juste</Badge>;
  return <Badge tone={value < 0 ? 'red' : 'amber'}>{value > 0 ? '+' : '−'}{money(Math.abs(value), currency)}</Badge>;
}
