'use client';
import {price} from '@/content/plans';
import {useTween} from '@/lib/use-tween';

/** Un prix qui défile jusqu'à sa nouvelle valeur (changement de plan, de pays ou de facturation). */
export function AnimatedPrice({amount, currency, className}: {amount: number; currency: string; className?: string}) {
  const value = useTween(amount);
  const decimals = amount % 1 !== 0 && currency !== 'XOF';
  const shown = value === amount ? amount : decimals ? Math.round(value * 100) / 100 : Math.round(value);
  return <span className={`tabular-nums ${className ?? ''}`}>{price(shown, currency)}</span>;
}
