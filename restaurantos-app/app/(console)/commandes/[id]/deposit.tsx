'use client';
import {Wallet} from 'lucide-react';
import {applyDeposit, hasDeductibleDeposit} from '@/lib/deposits';
import {money} from '@/lib/format';
import {useResource} from '@/lib/hooks';
import {hhmm, listReservations} from '@/lib/reservations';
import type {Order} from '@/lib/types';
import {Button, Card, useAction} from '@/components/ui';

/**
 * Les acomptes payés des réservations du jour dans l'établissement, à déduire de l'addition : d'abord celle de la table
 * de la commande. Rien ne s'affiche s'il n'y en a pas.
 */
export function DepositsForOrder({order, amountDue, onApplied}: {order: Order; amountDue: number; onApplied: () => Promise<void>}) {
  const {busy, run} = useAction();
  const {data} = useResource(async signal => {
    const start = new Date();
    start.setHours(0, 0, 0, 0);
    const end = new Date(start.getTime() + 86_400_000);
    return (await listReservations(order.locationId, start, end, signal).catch(() => [])).filter(hasDeductibleDeposit);
  }, [order.locationId, order.id]);
  if (!data || data.length === 0 || amountDue <= 0) return null;
  const sorted = [...data].sort((a, b) => Number(b.tableSessionId === order.tableSessionId && !!b.tableSessionId) - Number(a.tableSessionId === order.tableSessionId && !!a.tableSessionId));

  return (
    <Card className="mb-4 p-4">
      <h3 className="flex items-center gap-2 text-sm font-semibold"><Wallet className="size-4 text-ink-500" aria-hidden="true" />Acompte de réservation</h3>
      <ul className="mt-2 divide-y divide-slate-100">
        {sorted.slice(0, 5).map(reservation => {
          const deposit = reservation.deposit!;
          const sameTable = !!order.tableSessionId && reservation.tableSessionId === order.tableSessionId;
          const tooLarge = deposit.settled > amountDue;
          return (
            <li key={reservation.id} className="flex flex-wrap items-center gap-2 py-2 text-sm">
              <span className="min-w-0 flex-1">
                <span className="font-medium">{reservation.guestName || 'Client'}</span> · {reservation.partySize} pers. · {hhmm(new Date(reservation.startAtUtc))}
                {sameTable && <span className="ml-1 text-emerald-700">· cette table</span>}
                <span className="block text-xs text-ink-500">{tooLarge ? `Acompte supérieur au reste à payer : remboursez d’abord ${money(deposit.settled - amountDue, deposit.currencyCode)} depuis la réservation.` : `Acompte payé : ${money(deposit.settled, deposit.currencyCode)}`}</span>
              </span>
              <Button size="sm" variant={sameTable ? 'primary' : 'secondary'} disabled={tooLarge || !!busy} busy={busy === reservation.id} onClick={() => void run(reservation.id, async () => {
                await applyDeposit(reservation.id, order.id);
                await onApplied();
              }, 'Acompte déduit de l’addition.')}>Déduire {money(deposit.settled, deposit.currencyCode)}</Button>
            </li>
          );
        })}
      </ul>
    </Card>
  );
}
