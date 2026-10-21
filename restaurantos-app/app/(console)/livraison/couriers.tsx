'use client';
import {useEffect, useMemo, useState} from 'react';
import {Banknote, Bike, HandCoins, Pencil, Plus, Power, Wallet} from 'lucide-react';
import {
  courierBalances, courierKindLabel, createCourier, recordRemittance, remittancesSince, setCourierActive, updateCourier, collectionLabel,
  type Courier, type CourierBalance, type CourierKind,
} from '@/lib/delivery';
import {openSession} from '@/lib/cash';
import {startOfDay} from '@/lib/dashboard';
import {money, time} from '@/lib/format';
import {useResource} from '@/lib/hooks';
import {Alert, Badge, Button, Card, Empty, Field, Input, Modal, Select, Spinner, Textarea, cx, useAction} from '@/components/ui';
import {phoneOrThrow} from './dialogs';

const parseAmount = (value: string) => Number(value.replace(/\s/g, '').replace(',', '.'));

function CourierFormDialog({courier, open, currency, onClose, onDone}: {
  courier: Courier | null;
  open: boolean;
  currency: string;
  onClose: () => void;
  onDone: () => Promise<void>;
}) {
  const {busy, run} = useAction();
  const [name, setName] = useState('');
  const [phone, setPhone] = useState('');
  const [kind, setKind] = useState<CourierKind>('Independent');
  const [fee, setFee] = useState('');
  useEffect(() => {
    if (!open) return;
    setName(courier?.name ?? '');
    setPhone(courier?.phone ?? '');
    setKind(courier?.kind ?? 'Independent');
    setFee(courier?.feePerDelivery ? String(courier.feePerDelivery) : '');
  }, [open, courier]);

  return (
    <Modal open={open} onClose={onClose} title={courier ? 'Modifier le livreur' : 'Nouveau livreur'} footer={
      <>
        <Button variant="secondary" onClick={onClose}>Annuler</Button>
        <Button type="submit" form="courier-profile-form" busy={busy === 'save'}>Enregistrer</Button>
      </>
    }>
      <form id="courier-profile-form" className="space-y-4" onSubmit={event => {
        event.preventDefault();
        void run('save', async () => {
          const amount = fee.trim() ? parseAmount(fee) : null;
          if (amount !== null && !(amount >= 0)) throw new Error('Tarif par course invalide.');
          const input = {name: name.trim(), phone: phoneOrThrow(phone, false), kind, feePerDelivery: amount || null};
          if (courier) await updateCourier(courier.id, input);
          else await createCourier(input);
          await onDone();
          onClose();
        }, courier ? 'Livreur mis à jour.' : 'Livreur enregistré.');
      }}>
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
          <Field label="Nom"><Input required maxLength={80} autoComplete="off" value={name} onChange={event => setName(event.target.value)} /></Field>
          <Field label="Téléphone (WhatsApp)" hint="Facultatif, format international.">
            <Input type="tel" autoComplete="off" placeholder="+229 01 90 00 00 00" value={phone} onChange={event => setPhone(event.target.value)} />
          </Field>
        </div>
        <Field label="Statut">
          <Select value={kind} onChange={event => setKind(event.target.value as CourierKind)}>
            <option value="Independent">Indépendant (zémidjan, coursier payé à la course)</option>
            <option value="Staff">Salarié du restaurant</option>
          </Select>
        </Field>
        <Field label={`Tarif par course (${currency})`} hint="Facultatif. Figé sur chaque course confiée, réglé à sa remise. Le modifier ne change pas les courses passées.">
          <Input inputMode="decimal" value={fee} onChange={event => setFee(event.target.value)} placeholder="0" />
        </Field>
      </form>
    </Modal>
  );
}

function RemittanceDialog({balance, cashOpen, onClose, onDone}: {
  balance: CourierBalance | null;
  cashOpen: boolean | null;
  onClose: () => void;
  onDone: () => Promise<void>;
}) {
  const {busy, run} = useAction();
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [counted, setCounted] = useState('');
  const [note, setNote] = useState('');
  const items = useMemo(() => balance?.items ?? [], [balance]);
  const chosen = items.filter(item => selected.has(item.fulfillmentId));
  const cash = chosen.reduce((sum, item) => sum + item.cashHeld, 0);
  const fees = chosen.reduce((sum, item) => sum + item.courierFee, 0);
  const expected = cash - fees;
  useEffect(() => {
    setSelected(new Set(items.map(item => item.fulfillmentId)));
    setNote('');
  }, [items]);
  useEffect(() => setCounted(String(Math.abs(expected))), [expected]);

  const currency = balance?.currencyCode ?? 'XOF';
  const pays = expected < 0;
  const value = counted.trim() === '' ? NaN : parseAmount(counted);
  const signed = pays ? -value : value;
  const gap = Number.isFinite(signed) ? signed - expected : 0;
  const toggle = (id: string) => setSelected(current => {
    const next = new Set(current);
    if (next.has(id)) next.delete(id);
    else next.add(id);
    return next;
  });

  return (
    <Modal wide open={!!balance} onClose={onClose} title={`Remise de ${balance?.courierName ?? ''}`} footer={
      <>
        <Button variant="secondary" onClick={onClose}>Annuler</Button>
        <Button type="submit" form="remittance-form" busy={busy === 'remit'} disabled={chosen.length === 0}>Enregistrer la remise</Button>
      </>
    }>
      <form id="remittance-form" className="space-y-4" onSubmit={event => {
        event.preventDefault();
        void run('remit', async () => {
          if (!Number.isFinite(value) || value < 0) throw new Error('Montant invalide.');
          if (gap !== 0 && !note.trim()) throw new Error('Expliquez l’écart dans la note.');
          await recordRemittance(chosen.map(item => item.fulfillmentId), signed, note.trim() || null);
          await onDone();
          onClose();
        }, 'Remise enregistrée.');
      }}>
        {cashOpen === false && (
          <Alert tone="amber">Aucune caisse ouverte dans cet établissement : la remise sera enregistrée, mais l’argent ne passera pas dans la caisse. Ouvrez d’abord la caisse.</Alert>
        )}
        <div className="relative overflow-x-auto">
          <table className="w-full min-w-[520px] text-left text-sm">
            <thead className="border-b border-slate-200 text-xs text-ink-500">
              <tr>
                <th scope="col" className="w-8 px-2 py-2"><span className="sr-only">Inclure</span></th>
                <th scope="col" className="px-2 py-2 font-medium">Course</th>
                <th scope="col" className="px-2 py-2 font-medium">Encaissé</th>
                <th scope="col" className="px-2 py-2 text-right font-medium">Espèces</th>
                <th scope="col" className="px-2 py-2 text-right font-medium">Tarif</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {items.map(item => (
                <tr key={item.fulfillmentId} className={cx(!selected.has(item.fulfillmentId) && 'opacity-50')}>
                  <td className="px-2 py-2">
                    <input type="checkbox" aria-label={`Inclure la course de ${item.customerName}`} checked={selected.has(item.fulfillmentId)} onChange={() => toggle(item.fulfillmentId)} className="size-4 accent-brand-600" />
                  </td>
                  <td className="px-2 py-2">
                    <span className="block font-medium">{item.customerName}</span>
                    <span className="text-xs text-ink-500">{[item.zoneName, item.completedAtUtc ? `livrée à ${time(item.completedAtUtc)}` : null].filter(Boolean).join(' · ')}</span>
                  </td>
                  <td className="px-2 py-2 text-ink-600">{item.collectionMethod ? `${collectionLabel[item.collectionMethod]} ${money(item.amountCollected ?? 0, currency)}` : 'Déjà réglée'}</td>
                  <td className="px-2 py-2 text-right tabular-nums">{money(item.cashHeld, currency)}</td>
                  <td className="px-2 py-2 text-right tabular-nums">{item.courierFee ? `- ${money(item.courierFee, currency)}` : '—'}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        <dl className="grid grid-cols-1 gap-2 rounded-xl bg-slate-50 p-3 text-sm sm:grid-cols-3">
          <div><dt className="text-xs text-ink-500">Espèces encaissées</dt><dd className="font-semibold tabular-nums">{money(cash, currency)}</dd></div>
          <div><dt className="text-xs text-ink-500">Tarifs des courses</dt><dd className="font-semibold tabular-nums">{money(fees, currency)}</dd></div>
          <div>
            <dt className="text-xs text-ink-500">{pays ? 'À payer au livreur' : 'À remettre par le livreur'}</dt>
            <dd className="text-lg font-bold tabular-nums">{money(Math.abs(expected), currency)}</dd>
          </div>
        </dl>
        <Field label={pays ? 'Montant payé au livreur' : 'Montant remis par le livreur'} hint={pays ? 'Sortie d’espèces de la caisse.' : 'Entrée d’espèces dans la caisse.'}>
          <Input inputMode="decimal" required value={counted} onChange={event => setCounted(event.target.value)} />
        </Field>
        {gap !== 0 && Number.isFinite(gap) && (
          <p className={cx('rounded-xl px-3 py-2 text-sm font-semibold', gap < 0 ? 'bg-red-50 text-red-700' : 'bg-amber-50 text-amber-800')}>
            Écart : {gap > 0 ? '+' : '-'} {money(Math.abs(gap), currency)} {gap < 0 ? '(il manque de l’argent)' : '(excédent)'}
          </p>
        )}
        <Field label={gap !== 0 ? 'Note (obligatoire en cas d’écart)' : 'Note (facultative)'}>
          <Textarea rows={2} maxLength={300} required={gap !== 0} value={note} onChange={event => setNote(event.target.value)} />
        </Field>
      </form>
    </Modal>
  );
}

export function CouriersPanel({locationId, currency, couriers, canRemit, canManage, onChanged}: {
  locationId: string;
  currency: string;
  couriers: Courier[];
  canRemit: boolean;
  canManage: boolean;
  onChanged: () => Promise<void>;
}) {
  const {busy, run} = useAction();
  const [editing, setEditing] = useState<{courier: Courier | null} | null>(null);
  const [remitting, setRemitting] = useState<CourierBalance | null>(null);
  const money$ = useResource(
    async signal => {
      if (!canRemit) return null;
      const [balances, remittances, session] = await Promise.all([
        courierBalances(locationId, signal),
        remittancesSince(locationId, startOfDay(new Date()), signal),
        openSession(locationId, signal).then(found => !!found).catch(() => null),
      ]);
      return {balances, remittances, cashOpen: session};
    },
    [locationId, canRemit],
    canRemit ? 30_000 : 0,
  );
  const reload = async () => {
    await Promise.all([money$.reload(), onChanged()]);
  };
  const balances = money$.data?.balances ?? [];
  const remittances = money$.data?.remittances ?? [];
  const toCollect = balances.reduce((sum, balance) => sum + Math.max(balance.expected, 0), 0);
  const toPay = balances.reduce((sum, balance) => sum + Math.max(-balance.expected, 0), 0);
  const gaps = remittances.reduce((sum, remittance) => sum + remittance.difference, 0);

  return (
    <div className="space-y-6">
      {canRemit && (
        <section aria-labelledby="couriers-money" className="space-y-3">
          <div className="flex flex-wrap items-end justify-between gap-2">
            <div>
              <h2 id="couriers-money" className="text-base font-semibold">Argent des livreurs</h2>
              <p className="text-sm text-ink-500">Courses terminées pas encore remises : espèces encaissées moins le tarif de chaque course.</p>
            </div>
            {money$.data?.cashOpen === false && <Badge tone="amber">Aucune caisse ouverte</Badge>}
          </div>
          {money$.error && <Alert>{money$.error}</Alert>}
          {!money$.data && !money$.error && <Spinner label="Chargement de l’argent des livreurs…" />}
          {money$.data && (
            <>
              <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
                <Card className="p-4"><p className="text-xs text-ink-500">À remettre par les livreurs</p><p className="mt-1 text-xl font-bold tabular-nums">{money(toCollect, currency)}</p></Card>
                <Card className="p-4"><p className="text-xs text-ink-500">À payer aux livreurs</p><p className="mt-1 text-xl font-bold tabular-nums">{money(toPay, currency)}</p></Card>
                <Card className="p-4">
                  <p className="text-xs text-ink-500">Écarts des remises du jour</p>
                  <p className={cx('mt-1 text-xl font-bold tabular-nums', gaps < 0 ? 'text-red-600' : gaps > 0 ? 'text-amber-700' : '')}>{gaps > 0 ? '+' : gaps < 0 ? '-' : ''} {money(Math.abs(gaps), currency)}</p>
                </Card>
              </div>
              {balances.length === 0 ? (
                <Empty icon={<HandCoins className="size-8" />} title="Rien à remettre">Les livraisons terminées avec des espèces ou un tarif à régler apparaissent ici.</Empty>
              ) : (
                <div className="grid grid-cols-1 gap-3 md:grid-cols-2 xl:grid-cols-3">
                  {balances.map(balance => (
                    <Card key={`${balance.courierId ?? balance.courierName}`} className="p-4">
                      <div className="flex items-start justify-between gap-2">
                        <div className="min-w-0">
                          <p className="flex items-center gap-2 truncate font-semibold"><Bike className="size-4 shrink-0 text-ink-500" aria-hidden="true" />{balance.courierName}</p>
                          <p className="text-xs text-ink-500">{balance.deliveries} course{balance.deliveries > 1 ? 's' : ''}{balance.courierId ? '' : ' · saisi à la main'}</p>
                        </div>
                        <div className="text-right">
                          <p className="text-xs text-ink-500">{balance.expected < 0 ? 'À lui payer' : 'À remettre'}</p>
                          <p className="text-lg font-bold tabular-nums">{money(Math.abs(balance.expected), balance.currencyCode)}</p>
                        </div>
                      </div>
                      <dl className="mt-3 space-y-1 text-sm">
                        <div className="flex justify-between gap-2"><dt className="flex items-center gap-1.5 text-ink-500"><Banknote className="size-3.5" aria-hidden="true" />Espèces détenues</dt><dd className="tabular-nums">{money(balance.cashHeld, balance.currencyCode)}</dd></div>
                        <div className="flex justify-between gap-2"><dt className="flex items-center gap-1.5 text-ink-500"><Wallet className="size-3.5" aria-hidden="true" />Tarifs des courses</dt><dd className="tabular-nums">{balance.courierFees ? `- ${money(balance.courierFees, balance.currencyCode)}` : '—'}</dd></div>
                        {balance.mobileMoneyCollected > 0 && (
                          <div className="flex justify-between gap-2 text-xs text-ink-500"><dt>Mobile money reçu par le restaurant</dt><dd className="tabular-nums">{money(balance.mobileMoneyCollected, balance.currencyCode)}</dd></div>
                        )}
                      </dl>
                      <Button size="sm" className="mt-3 w-full" onClick={() => setRemitting(balance)}><HandCoins className="size-4" aria-hidden="true" />Faire la remise</Button>
                    </Card>
                  ))}
                </div>
              )}

              {remittances.length > 0 && (
                <div className="relative overflow-x-auto">
                  <h3 className="mb-2 text-sm font-semibold">Remises d’aujourd’hui</h3>
                  <table className="w-full min-w-[640px] text-left text-sm">
                    <thead className="border-b border-slate-200 text-xs text-ink-500">
                      <tr>
                        <th scope="col" className="px-3 py-2 font-medium">Heure</th>
                        <th scope="col" className="px-3 py-2 font-medium">Livreur</th>
                        <th scope="col" className="px-3 py-2 text-right font-medium">Courses</th>
                        <th scope="col" className="px-3 py-2 text-right font-medium">Attendu</th>
                        <th scope="col" className="px-3 py-2 text-right font-medium">Remis / payé</th>
                        <th scope="col" className="px-3 py-2 text-right font-medium">Écart</th>
                        <th scope="col" className="px-3 py-2 font-medium">Note</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-slate-100">
                      {remittances.map(remittance => (
                        <tr key={remittance.id}>
                          <td className="px-3 py-2">{time(remittance.recordedAtUtc)}</td>
                          <td className="px-3 py-2 font-medium">{remittance.courierName}</td>
                          <td className="px-3 py-2 text-right tabular-nums">{remittance.deliveries}</td>
                          <td className="px-3 py-2 text-right tabular-nums">{money(remittance.expected, remittance.currencyCode)}</td>
                          <td className="px-3 py-2 text-right tabular-nums">{money(remittance.counted, remittance.currencyCode)}</td>
                          <td className={cx('px-3 py-2 text-right font-semibold tabular-nums', remittance.difference < 0 ? 'text-red-600' : remittance.difference > 0 ? 'text-amber-700' : 'text-ink-500')}>
                            {remittance.difference === 0 ? '—' : `${remittance.difference > 0 ? '+' : '-'} ${money(Math.abs(remittance.difference), remittance.currencyCode)}`}
                          </td>
                          <td className="max-w-[16rem] truncate px-3 py-2 text-ink-600">{remittance.note ?? ''}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </>
          )}
        </section>
      )}

      <section aria-labelledby="couriers-list" className="space-y-3">
        <div className="flex flex-wrap items-end justify-between gap-2">
          <div>
            <h2 id="couriers-list" className="text-base font-semibold">Fiches livreurs</h2>
            <p className="text-sm text-ink-500">Salariés ou indépendants, avec leur tarif par course. Désactivé, un livreur n’est plus proposé ; ses courses restent.</p>
          </div>
          {canManage && <Button size="sm" onClick={() => setEditing({courier: null})}><Plus className="size-4" aria-hidden="true" />Nouveau livreur</Button>}
        </div>
        {couriers.length === 0 ? (
          <Empty icon={<Bike className="size-8" />} title="Aucun livreur enregistré">
            {canManage ? 'Enregistrez vos livreurs pour les choisir en un clic et suivre leurs courses.' : 'Les livreurs enregistrés apparaîtront ici.'}
          </Empty>
        ) : (
          <div className="relative overflow-x-auto">
            <table className="w-full min-w-[600px] text-left text-sm">
              <thead className="border-b border-slate-200 text-xs text-ink-500">
                <tr>
                  <th scope="col" className="px-3 py-2 font-medium">Nom</th>
                  <th scope="col" className="px-3 py-2 font-medium">Téléphone</th>
                  <th scope="col" className="px-3 py-2 font-medium">Statut</th>
                  <th scope="col" className="px-3 py-2 text-right font-medium">Tarif par course</th>
                  {canManage && <th scope="col" className="px-3 py-2"><span className="sr-only">Actions</span></th>}
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {couriers.map(courier => (
                  <tr key={courier.id} className={cx(!courier.isActive && 'text-ink-500')}>
                    <td className="px-3 py-2 font-medium">{courier.name}{!courier.isActive && <Badge className="ml-2">Désactivé</Badge>}</td>
                    <td className="px-3 py-2">{courier.phone ? <a href={`tel:${courier.phone}`} className="text-brand-700 hover:underline">{courier.phone}</a> : '—'}</td>
                    <td className="px-3 py-2">{courierKindLabel[courier.kind]}</td>
                    <td className="px-3 py-2 text-right tabular-nums">{courier.feePerDelivery ? money(courier.feePerDelivery, courier.feeCurrencyCode ?? currency) : '—'}</td>
                    {canManage && (
                      <td className="px-3 py-2">
                        <div className="flex justify-end gap-1">
                          <Button size="sm" variant="ghost" aria-label={`Modifier ${courier.name}`} onClick={() => setEditing({courier})}><Pencil className="size-4" aria-hidden="true" /></Button>
                          <Button size="sm" variant="ghost" busy={busy === courier.id} aria-label={courier.isActive ? `Désactiver ${courier.name}` : `Réactiver ${courier.name}`}
                            onClick={() => run(courier.id, async () => {
                              await setCourierActive(courier.id, !courier.isActive);
                              await onChanged();
                            }, courier.isActive ? 'Livreur désactivé.' : 'Livreur réactivé.')}>
                            <Power className="size-4" aria-hidden="true" />
                          </Button>
                        </div>
                      </td>
                    )}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      <CourierFormDialog open={editing !== null} courier={editing?.courier ?? null} currency={currency} onClose={() => setEditing(null)} onDone={onChanged} />
      <RemittanceDialog balance={remitting} cashOpen={money$.data?.cashOpen ?? null} onClose={() => setRemitting(null)} onDone={reload} />
    </div>
  );
}
