'use client';
import {useState} from 'react';
import {ArrowRight, Check, X} from 'lucide-react';
import {useResource} from '@/lib/hooks';
import {decideSwap, hm, listSwaps, longDayLabel, swapStatusLabel, swapStatusTone, type ShiftSwap} from '@/lib/schedule';
import {Alert, Badge, Button, Empty, Spinner, Tabs, useAction} from '@/components/ui';

export function SwapLine({swap, children}: {swap: ShiftSwap; children?: React.ReactNode}) {
  return (
    <li className="flex flex-col gap-3 p-4 sm:flex-row sm:items-center">
      <div className="min-w-0 flex-1">
        <p className="flex flex-wrap items-center gap-2 font-semibold">
          {swap.fromName}<ArrowRight className="size-4 text-ink-400" aria-label="à" />{swap.toName}
          <Badge tone={swapStatusTone[swap.status]}>{swapStatusLabel[swap.status]}</Badge>
        </p>
        <p className="text-sm text-ink-700">
          <span className="inline-block first-letter:uppercase">{swap.date ? longDayLabel(swap.date) : '—'}</span>
          {swap.start && ` · ${hm(swap.start)} – ${hm(swap.end)}`}
          {swap.position && ` · ${swap.position}`}
          {swap.locationName && ` · ${swap.locationName}`}
        </p>
        {swap.note && <p className="text-sm text-ink-500">« {swap.note} »</p>}
      </div>
      {children && <div className="flex gap-2">{children}</div>}
    </li>
  );
}

/** Remplacements : le collègue accepte d'abord, le gérant valide ensuite ; le créneau change alors de titulaire. */
export function Swaps({onChanged}: {onChanged: () => Promise<void>}) {
  const {busy, run} = useAction();
  const [scope, setScope] = useState<'open' | 'all'>('open');
  const data = useResource(signal => listSwaps(scope === 'open', signal), [scope]);
  const reload = async () => {
    await data.reload();
    await onChanged();
  };

  return (
    <div>
      <Tabs label="Remplacements" value={scope} onChange={setScope} items={[{value: 'open', label: 'En cours'}, {value: 'all', label: 'Tous'}]} />
      {data.error ? <Alert>{data.error}</Alert> : !data.data ? <Spinner /> : data.data.length === 0 ? (
        <Empty title="Aucun remplacement">Un membre propose un de ses créneaux à un collègue depuis « Mon planning ».</Empty>
      ) : (
        <ul className="divide-y divide-slate-100 rounded-xl border border-slate-200">
          {data.data.map(swap => (
            <SwapLine key={swap.id} swap={swap}>
              {(swap.status === 'AwaitingManager' || swap.status === 'AwaitingColleague') && (
                <Button variant="secondary" size="sm" busy={busy === `no-${swap.id}`} onClick={() => void run(`no-${swap.id}`, async () => {
                  await decideSwap(swap.id, false);
                  await reload();
                }, 'Remplacement refusé.')}><X className="size-4" aria-hidden="true" />Refuser</Button>
              )}
              {swap.status === 'AwaitingManager' && (
                <Button variant="success" size="sm" busy={busy === `ok-${swap.id}`} onClick={() => void run(`ok-${swap.id}`, async () => {
                  await decideSwap(swap.id, true);
                  await reload();
                }, 'Remplacement validé : le planning est à jour.')}><Check className="size-4" aria-hidden="true" />Valider</Button>
              )}
            </SwapLine>
          ))}
        </ul>
      )}
    </div>
  );
}
