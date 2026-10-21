'use client';
import {useEffect, useRef, useState} from 'react';
import {Bike, Check, ChefHat, Clock, Flame, MoreVertical, Play, Send, ShoppingBag, UtensilsCrossed} from 'lucide-react';
import {ApiError, describe, post, put} from '@/lib/api';
import {minutesSince, priorityLabel, time} from '@/lib/format';
import {courseLabel, finishTicket, fireCourse, fireLabel, forgetOrder, handOver, type BoardTicket} from '@/lib/kitchen';
import type {TicketPriority} from '@/lib/types';
import {useConsole} from '@/components/console';
import {Button, Field, Input, Modal, cx, useAction, useToast} from '@/components/ui';
import {Cover} from '../menu/images';

const LATE = 20;

export function lateness(ticket: BoardTicket, now: number) {
  return minutesSince(ticket.createdAtUtc, now);
}

function Context({ticket}: {ticket: BoardTicket}) {
  const type = ticket.order?.type;
  if (type === 'Delivery') return <span className="flex items-center gap-1"><Bike className="size-3.5" aria-hidden="true" />Livraison</span>;
  if (type === 'Takeaway') return <span className="flex items-center gap-1"><ShoppingBag className="size-3.5" aria-hidden="true" />À emporter</span>;
  return (
    <span className="flex items-center gap-1">
      <UtensilsCrossed className="size-3.5" aria-hidden="true" />
      {ticket.table ? `Table ${ticket.table.label}` : 'Sur place'}
      {ticket.table?.area && <span className="text-ink-500">· {ticket.table.area}</span>}
    </span>
  );
}

function PriorityMenu({ticket, onChanged}: {ticket: BoardTicket; onChanged: () => Promise<void>}) {
  const {busy, run} = useAction();
  const [open, setOpen] = useState(false);
  const [cancelling, setCancelling] = useState(false);
  const [reason, setReason] = useState('');
  const ref = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (!open) return;
    const close = (event: MouseEvent) => !ref.current?.contains(event.target as Node) && setOpen(false);
    document.addEventListener('mousedown', close);
    return () => document.removeEventListener('mousedown', close);
  }, [open]);
  return (
    <div ref={ref} className="relative">
      <button type="button" aria-haspopup="menu" aria-expanded={open} aria-label="Options du ticket" disabled={!!busy} onClick={() => setOpen(value => !value)} className="grid size-10 place-items-center rounded-xl border border-slate-200 text-ink-600 hover:bg-slate-50">
        <MoreVertical className="size-4" />
      </button>
      {open && (
        <div role="menu" className="absolute right-0 bottom-11 z-20 w-44 rounded-xl border border-slate-200 bg-white p-1 shadow-lg">
          <p className="px-3 py-1.5 text-xs font-semibold text-ink-500">Priorité</p>
          {(Object.keys(priorityLabel) as TicketPriority[]).map(priority => (
            <button
              key={priority}
              type="button"
              role="menuitemradio"
              aria-checked={ticket.priority === priority}
              onClick={() => {
                setOpen(false);
                if (priority !== ticket.priority) {
                  void run('priority', async () => {
                    await put(`/kitchen/tickets/${ticket.id}/priority`, {priority});
                    await onChanged();
                  });
                }
              }}
              className="flex w-full items-center justify-between rounded-lg px-3 py-2 text-left text-sm hover:bg-slate-50">
              {priorityLabel[priority]}
              {ticket.priority === priority && <Check className="size-4 text-brand-600" aria-hidden="true" />}
            </button>
          ))}
          <div className="my-1 border-t border-slate-100" />
          <button type="button" role="menuitem" onClick={() => {
            setOpen(false);
            setReason('');
            setCancelling(true);
          }} className="flex w-full items-center rounded-lg px-3 py-2 text-left text-sm text-red-600 hover:bg-red-50">Annuler le ticket</button>
        </div>
      )}
      <Modal open={cancelling} onClose={() => setCancelling(false)} title="Annuler le ticket" footer={
        <>
          <Button variant="secondary" onClick={() => setCancelling(false)}>Garder</Button>
          <Button variant="danger" busy={busy === 'cancel'} onClick={() => run('cancel', async () => {
            await post(`/kitchen/tickets/${ticket.id}/cancel`, {reason: reason.trim() || null});
            setCancelling(false);
            await onChanged();
          }, 'Ticket annulé.')}>Annuler le ticket</Button>
        </>
      }>
        <div className="space-y-3">
          <p className="text-sm text-ink-700">Le ticket quitte l’écran de la cuisine. <strong>La commande n’est pas annulée</strong> : si le client ne veut plus rien, annulez-la depuis la commande, ce qui annule aussi le ticket.</p>
          <Field label="Motif (facultatif)"><Input maxLength={500} value={reason} onChange={event => setReason(event.target.value)} placeholder="Préparé hors cuisine, doublon…" /></Field>
        </div>
      </Modal>
    </div>
  );
}

export function TicketCard({ticket, stationId, now, onChanged}: {ticket: BoardTicket; stationId: string; now: number; onChanged: () => Promise<void>}) {
  const {can} = useConsole();
  const toast = useToast();
  const operate = can('kitchen.operate');
  const {busy, run} = useAction();
  const items = stationId === 'all' ? ticket.items : ticket.items.filter(item => item.stationId === stationId);
  const minutes = lateness(ticket, now);
  const late = ticket.status !== 'Ready' && minutes >= LATE;
  const ready = ticket.items.filter(item => item.status === 'Ready').length;
  const progress = ticket.items.length ? ready / ticket.items.length : 0;
  const productOf = (orderItemId: string) => ticket.order?.items.find(item => item.id === orderItemId)?.productId;
  const number = ticket.order?.number;

  const act = (key: string, path: string, success?: string) =>
    run(key, async () => {
      await post(`/kitchen/tickets/${ticket.id}/${path}`);
      await onChanged();
    }, success);

  const deliver = async () => {
    const ok = await run('deliver', async () => {
      await handOver(ticket);
      await onChanged();
    });
    if (!ok || !ticket.order || !can('orders.process')) return;
    try {
      forgetOrder(ticket.order.id);
      if (ticket.order.status !== 'Completed') await post(`/orders/${ticket.order.id}/complete`);
      toast('success', `Commande #${number} remise.`);
    } catch (error) {
      if (error instanceof ApiError && error.code === 'orders.order.not_settled') {
        toast('success', `Commande #${number} remise. Reste à encaisser : terminez-la depuis Commandes une fois réglée.`);
      } else {
        toast('error', `Ticket clos, mais la commande n’a pas pu être terminée : ${describe(error)}`);
      }
    }
    await onChanged();
  };

  const handLabel = ticket.order?.type === 'Delivery' ? 'Remettre au livreur' : ticket.order?.type === 'Takeaway' ? 'Remettre au client' : 'Servir la commande';

  return (
    <article className={cx('rounded-2xl border bg-white p-4 shadow-sm', ticket.priority === 'Rush' ? 'border-red-300 ring-1 ring-red-200' : 'border-slate-200')}>
      <header className="flex items-center gap-3">
        <span className="text-lg font-bold">{number ? `#${number}` : 'Commande'}</span>
        <span className="text-sm text-ink-500">{time(ticket.createdAtUtc)}</span>
        {ticket.priority !== 'Normal' && (
          <span className="flex items-center gap-1 rounded-full bg-red-50 px-2 py-0.5 text-xs font-semibold text-red-700"><Flame className="size-3" aria-hidden="true" />{priorityLabel[ticket.priority]}</span>
        )}
        <span className={cx('ml-auto flex items-center gap-1 rounded-full px-2 py-0.5 text-xs font-semibold', late ? 'bg-red-50 text-red-700' : minutes >= 10 ? 'bg-amber-50 text-amber-700' : 'bg-slate-100 text-ink-600')}>
          <Clock className="size-3" aria-hidden="true" />{Math.max(0, Math.floor(minutes))} min
        </span>
      </header>
      <div className="mt-2 flex flex-wrap items-center gap-x-4 gap-y-1 text-sm text-ink-600">
        <Context ticket={ticket} />
        {ticket.order && ticket.order.items.length > 0 && (
          <span>{ticket.order.items.reduce((sum, item) => sum + item.quantity, 0)} article{ticket.order.items.reduce((sum, item) => sum + item.quantity, 0) > 1 ? 's' : ''}</span>
        )}
      </div>

      <ul className="mt-3 space-y-2.5 border-t border-slate-100 pt-3">
        {items.map(item => {
          const productId = productOf(item.orderItemId);
          const done = item.status === 'Ready';
          const held = !!item.isHeld;
          const canToggle = operate && ticket.status !== 'Ready' && !done && !held;
          return (
            <li key={item.id} className={cx('flex items-center gap-3', held && 'opacity-60')}>
              <span className="size-11 shrink-0 overflow-hidden rounded-lg">
                {productId ? <Cover productId={productId} name={item.name} seed={item.name} className="size-full" small /> : <span className="grid size-full place-items-center bg-slate-100"><ChefHat className="size-5 text-ink-500" aria-hidden="true" /></span>}
              </span>
              <div className="min-w-0 flex-1">
                <p className={cx('text-sm font-semibold', done && 'text-ink-500 line-through')}>{item.quantity} × {item.name}{item.variantName && ` · ${item.variantName}`}</p>
                {item.modifiers.length > 0 && <p className="truncate text-xs text-ink-600">{item.modifiers.join(', ')}</p>}
                {item.note && <p className="text-xs font-semibold text-amber-800">{item.note}</p>}
                {held && item.course && <p className="text-xs font-semibold text-sky-700">En attente · {courseLabel[item.course]}</p>}
              </div>
              {canToggle && ticket.status === 'InProgress' && (
                <button
                  type="button"
                  disabled={!!busy}
                  onClick={() => act(`item-${item.id}`, `items/${item.id}/${item.status === 'Queued' ? 'start' : 'ready'}`)}
                  aria-label={item.status === 'Queued' ? `Lancer ${item.name}` : `${item.name} prêt`}
                  className={cx('grid size-8 shrink-0 place-items-center rounded-full border transition', item.status === 'Queued' ? 'border-slate-300 text-ink-500 hover:border-brand-500 hover:text-brand-600' : 'border-amber-300 bg-amber-50 text-amber-700 hover:border-emerald-500 hover:text-emerald-600')}>
                  {item.status === 'Queued' ? <Play className="size-3.5" /> : <Check className="size-4" />}
                </button>
              )}
              {done && <Check className="size-5 shrink-0 text-emerald-600" aria-label="Prêt" />}
            </li>
          );
        })}
        {items.length === 0 && <li className="text-sm text-ink-500">Rien pour ce poste.</li>}
      </ul>

      {ticket.status !== 'Queued' && (
        <div className="mt-3 flex items-center gap-3">
          <div className="h-2 flex-1 overflow-hidden rounded-full bg-slate-100" role="progressbar" aria-valuemin={0} aria-valuemax={100} aria-valuenow={Math.round(progress * 100)} aria-label="Avancement">
            <div className={cx('h-full rounded-full transition-all', ticket.status === 'Ready' ? 'bg-emerald-500' : 'bg-amber-500')} style={{width: `${ticket.status === 'Ready' ? 100 : Math.max(8, progress * 100)}%`}} />
          </div>
          <span className={cx('flex items-center gap-1 rounded-full px-2 py-0.5 text-xs font-semibold', ticket.status === 'Ready' ? 'bg-emerald-50 text-emerald-700' : 'bg-amber-50 text-amber-700')}>
            {ticket.status === 'Ready' ? <><Check className="size-3" aria-hidden="true" />Prête</> : <><ChefHat className="size-3" aria-hidden="true" />{ready}/{ticket.items.length} prêts</>}
          </span>
        </div>
      )}

      {operate && (
        <div className="mt-4 flex gap-2">
          {ticket.status === 'Queued' && (
            <Button variant="danger" className="flex-1" busy={busy === 'start'} onClick={() => act('start', 'start', `Préparation de #${number ?? ''} lancée.`)}>
              <Play className="size-4" aria-hidden="true" />Démarrer la préparation
            </Button>
          )}
          {(ticket.heldCourses ?? []).slice(0, 1).map(course => (
            <Button key={course} variant="secondary" className="flex-1" busy={busy === `fire-${course}`} onClick={() => run(`fire-${course}`, async () => {
              await fireCourse(ticket.id, course);
              await onChanged();
            }, `${courseLabel[course]} envoyé en préparation.`)}>
              <Send className="size-4" aria-hidden="true" />{fireLabel[course]}
            </Button>
          ))}
          {ticket.status === 'InProgress' && !(ticket.heldCourses ?? []).length && (
            <Button variant="secondary" className="flex-1" busy={busy === 'ready'} onClick={() => run('ready', async () => {
              await finishTicket(ticket);
              await onChanged();
            }, `#${number ?? ''} est prête.`)}>
              <Check className="size-4" aria-hidden="true" />Marquer comme prêt
            </Button>
          )}
          {ticket.status === 'Ready' && (
            <Button variant="success" className="flex-1" busy={busy === 'deliver'} onClick={() => void deliver()}>
              {ticket.order?.type === 'Delivery' ? <Bike className="size-4" aria-hidden="true" /> : ticket.order?.type === 'Takeaway' ? <ShoppingBag className="size-4" aria-hidden="true" /> : <UtensilsCrossed className="size-4" aria-hidden="true" />}
              {handLabel}
            </Button>
          )}
          {ticket.status !== 'Ready' && <PriorityMenu ticket={ticket} onChanged={onChanged} />}
        </div>
      )}
    </article>
  );
}
