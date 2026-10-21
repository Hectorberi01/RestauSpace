'use client';
import {useEffect, useState} from 'react';
import {Gift, Tag, Ticket} from 'lucide-react';
import {money} from '@/lib/format';
import {useResource} from '@/lib/hooks';
import {applyPromotion, findAccountOfCustomer, ineligibleReason, listRewards, previewDiscounts, redeemReward} from '@/lib/marketing';
import type {Order} from '@/lib/types';
import {Alert, Badge, Button, Field, Input, Modal, Spinner, cx, useAction} from '@/components/ui';

/** La remise est posée sur la commande par un événement : on recharge quelques fois le temps qu'elle arrive. */
async function settle(reload: () => Promise<void>) {
  for (const wait of [800, 1500, 3000]) {
    await new Promise(resolve => window.setTimeout(resolve, wait));
    await reload();
  }
}

export function PromotionDialog({order, open, onClose, onChanged}: {order: Order; open: boolean; onClose: () => void; onChanged: () => Promise<void>}) {
  const {busy, run} = useAction();
  const [code, setCode] = useState('');
  const [presented, setPresented] = useState<string | null>(null);
  useEffect(() => {
    if (!open) return;
    setCode('');
    setPresented(null);
  }, [open]);
  const preview = useResource(signal => (open ? previewDiscounts(order.id, presented, signal) : Promise.resolve(null)), [open, order.id, order.total, presented]);
  const automatic = useResource(signal => (open ? previewDiscounts(order.id, null, signal) : Promise.resolve(null)), [open, order.id]);
  // La promotion du code est celle qui n'apparaît pas sans le code.
  const fromCoupon = (id: string) => presented !== null && !(automatic.data ?? []).some(item => item.promotionId === id);

  const apply = (key: string, target: {promotionId: string} | {couponCode: string}) => run(key, async () => {
    await applyPromotion(order.id, target);
    onClose();
    await settle(onChanged);
  }, 'Promotion appliquée : la remise apparaît sur la commande.');

  return (
    <Modal open={open} onClose={onClose} title="Appliquer une promotion">
      <div className="space-y-4">
        <form className="flex items-end gap-2" onSubmit={event => {
          event.preventDefault();
          setPresented(code.trim().toUpperCase() || null);
        }}>
          <Field label="Code promo du client" className="flex-1">
            <Input value={code} maxLength={32} onChange={event => setCode(event.target.value.toUpperCase())} placeholder="BIENVENUE10" autoComplete="off" />
          </Field>
          <Button type="submit" variant="secondary" disabled={!code.trim()}><Ticket className="size-4" aria-hidden="true" />Vérifier</Button>
        </form>
        {preview.error ? <Alert>{preview.error}</Alert> : !preview.data ? <Spinner /> : preview.data.length === 0 ? (
          <p className="py-4 text-center text-sm text-ink-500">Aucune promotion en cours.</p>
        ) : (
          <ul className="divide-y divide-slate-100 rounded-xl border border-slate-200">
            {preview.data.map(item => (
              <li key={item.promotionId} className="flex items-center gap-3 px-3 py-2.5 text-sm">
                <span className={cx('grid size-9 shrink-0 place-items-center rounded-xl', item.isEligible ? 'bg-emerald-50 text-emerald-600' : 'bg-slate-100 text-ink-400')} aria-hidden="true"><Tag className="size-4" /></span>
                <div className="min-w-0 flex-1">
                  <p className="flex items-center gap-2 truncate font-medium">{item.name}{fromCoupon(item.promotionId) && <Badge tone="blue">Code {presented}</Badge>}</p>
                  <p className="text-xs text-ink-500">{item.isEligible ? `Remise : −${money(item.discount, item.currencyCode)}` : ineligibleReason[item.reason ?? ''] ?? 'Non applicable.'}</p>
                </div>
                {item.isEligible && (
                  <Button size="sm" busy={busy === item.promotionId} onClick={() => apply(item.promotionId,
                    fromCoupon(item.promotionId) && presented ? {couponCode: presented} : {promotionId: item.promotionId})}>Appliquer</Button>
                )}
              </li>
            ))}
          </ul>
        )}
        <p className="text-xs text-ink-500">Seule une commande en brouillon accepte une remise. Chaque promotion ne compte qu’une fois par commande.</p>
      </div>
    </Modal>
  );
}

export function RewardDialog({order, open, onClose, onChanged}: {order: Order; open: boolean; onClose: () => void; onChanged: () => Promise<void>}) {
  const {busy, run} = useAction();
  const account = useResource(signal => (open && order.customerId ? findAccountOfCustomer(order.customerId, signal) : Promise.resolve(null)), [open, order.customerId]);
  const rewards = useResource(signal => (open ? listRewards(true, signal) : Promise.resolve(null)), [open]);
  const balance = account.data?.balance ?? 0;

  return (
    <Modal open={open} onClose={onClose} title="Utiliser une récompense">
      {account.error ? <Alert>{account.error}</Alert> : account.loading || rewards.loading ? <Spinner /> : !account.data ? (
        <Alert tone="blue">Ce client n’a pas encore de compte fidélité. Il en aura un à sa première commande terminée si l’inscription automatique est active.</Alert>
      ) : (
        <div className="space-y-4">
          <p className="text-sm">Solde du client : <strong>{new Intl.NumberFormat('fr-FR').format(balance)} points</strong>
            {account.data.status !== 'Active' && <Badge tone="amber" className="ml-2">Compte {account.data.status === 'Suspended' ? 'suspendu' : 'clos'}</Badge>}</p>
          {!rewards.data || rewards.data.length === 0 ? <p className="text-sm text-ink-500">Aucune récompense active.</p> : (
            <ul className="divide-y divide-slate-100 rounded-xl border border-slate-200">
              {rewards.data.map(reward => {
                const affordable = balance >= reward.pointCost && account.data!.status === 'Active' && reward.currencyCode === order.currencyCode;
                return (
                  <li key={reward.id} className="flex items-center gap-3 px-3 py-2.5 text-sm">
                    <span className={cx('grid size-9 shrink-0 place-items-center rounded-xl', affordable ? 'bg-rose-50 text-rose-600' : 'bg-slate-100 text-ink-400')} aria-hidden="true"><Gift className="size-4" /></span>
                    <div className="min-w-0 flex-1">
                      <p className="truncate font-medium">{reward.name}</p>
                      <p className="text-xs text-ink-500">{reward.pointCost} points → −{money(reward.discountAmount, reward.currencyCode)}</p>
                    </div>
                    <Button size="sm" disabled={!affordable} busy={busy === reward.id} onClick={() => run(reward.id, async () => {
                      await redeemReward(account.data!.id, reward.id, order.id);
                      onClose();
                      await settle(onChanged);
                    }, 'Récompense utilisée : la remise apparaît sur la commande.')}>Utiliser</Button>
                  </li>
                );
              })}
            </ul>
          )}
        </div>
      )}
    </Modal>
  );
}
