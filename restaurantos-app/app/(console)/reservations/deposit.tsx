'use client';
import {useState} from 'react';
import {Banknote, MessageCircle, RotateCcw, Wallet} from 'lucide-react';
import {describe} from '@/lib/api';
import {depositStatusLabel, depositStatusTone, recordDeposit, refundDeposit, reservationLink, waiveDeposit} from '@/lib/deposits';
import {money, time} from '@/lib/format';
import {useResource} from '@/lib/hooks';
import {loadConfigurations, methodLabel, tendersOf} from '@/lib/payments';
import type {Reservation} from '@/lib/reservations';
import {useConsole} from '@/components/console';
import {Alert, Badge, Button, Field, Input, Modal, Select, useAction, useToast} from '@/components/ui';

/** Message envoyé depuis le téléphone de l'équipier quand le restaurant n'envoie pas lui-même le lien. */
function depositText(reservation: Reservation, url: string | null) {
  const deposit = reservation.deposit!;
  const start = new Date(reservation.startAtUtc);
  const day = new Intl.DateTimeFormat('fr-FR', {weekday: 'long', day: 'numeric', month: 'long'}).format(start);
  return [
    `Bonjour ${reservation.guestName ?? ''}`.trim() + ',',
    `pour confirmer votre réservation du ${day} à ${time(reservation.startAtUtc)} (${reservation.partySize} pers.), un acompte de ${money(deposit.amount, deposit.currencyCode)} est demandé`
      + (deposit.dueBeforeUtc ? ` avant ${time(deposit.dueBeforeUtc)}.` : '.'),
    url ? `Payer et suivre votre réservation : ${url}` : 'Vous pouvez le régler au restaurant ou par mobile money.',
    'L’acompte est déduit de l’addition le jour du repas.',
  ].join('\n');
}

/** Acompte d'une réservation : état, encaissement par l'équipe, lien à envoyer, dispense et remboursement. */
export function DepositSection({reservation, onChanged}: {reservation: Reservation; onChanged: () => Promise<void>}) {
  const {can} = useConsole();
  const toast = useToast();
  const {busy, run} = useAction();
  const [collecting, setCollecting] = useState(false);
  const deposit = reservation.deposit;
  if (!deposit) return null;
  const manage = can('reservations.manage');
  const open = reservation.status === 'Pending' || reservation.status === 'Confirmed' || reservation.status === 'Seated';
  const awaiting = deposit.status === 'AwaitingPayment' || deposit.status === 'Waived';
  const refundable = (deposit.status === 'Forfeited' || (deposit.status === 'Paid' && reservation.status === 'Completed')) && can('payments.refund');

  const shareLink = () => run('link', async () => {
    const link = await reservationLink(reservation.id).catch(() => ({url: null}));
    const text = depositText(reservation, link.url);
    const phone = reservation.guestPhone?.replace(/[^0-9]/g, '') ?? '';
    window.open(`https://wa.me/${phone}?text=${encodeURIComponent(text)}`, '_blank', 'noopener');
  });

  return (
    <div className="space-y-2 border-b border-slate-100 py-4 text-sm">
      <div className="flex items-center gap-3">
        <Wallet className="size-5 shrink-0 text-ink-500" aria-hidden="true" />
        <span className="flex-1">
          <span className="font-semibold">{money(deposit.amount, deposit.currencyCode)}</span>
          {deposit.status === 'AwaitingPayment' && deposit.dueBeforeUtc && reservation.status === 'Pending' && (
            <span className="block text-xs text-ink-500">À payer avant {time(deposit.dueBeforeUtc)}, sinon la réservation est annulée.</span>
          )}
          {deposit.status === 'Paid' && <span className="block text-xs text-ink-500">Sera déduit de l’addition depuis la commande.</span>}
        </span>
        <Badge tone={depositStatusTone[deposit.status]}>{depositStatusLabel[deposit.status]}</Badge>
      </div>
      {manage && open && awaiting && (
        <div className="flex flex-wrap gap-2 pl-8">
          {can('payments.collect') && <Button size="sm" onClick={() => setCollecting(true)}><Banknote className="size-4" aria-hidden="true" />Encaisser</Button>}
          {reservation.guestPhone && <Button size="sm" variant="secondary" busy={busy === 'link'} onClick={() => void shareLink()}><MessageCircle className="size-4" aria-hidden="true" />Envoyer le lien</Button>}
          {deposit.status === 'AwaitingPayment' && (
            <Button size="sm" variant="ghost" busy={busy === 'waive'} onClick={() => void run('waive', async () => {
              await waiveDeposit(reservation.id);
              await onChanged();
            }, 'Acompte dispensé : la réservation ne sera plus annulée faute de paiement.')}>Dispenser</Button>
          )}
        </div>
      )}
      {manage && refundable && (
        <div className="pl-8">
          <Button size="sm" variant="secondary" busy={busy === 'refund'} onClick={() => void run('refund', async () => {
            await refundDeposit(reservation.id);
            await onChanged();
          }, 'Remboursement de l’acompte demandé.')}><RotateCcw className="size-4" aria-hidden="true" />Rembourser l’acompte</Button>
        </div>
      )}
      {collecting && (
        <CollectDepositDialog reservation={reservation} onClose={() => setCollecting(false)} onDone={async () => {
          setCollecting(false);
          toast('success', 'Acompte encaissé : la réservation est confirmée.');
          await onChanged();
        }} />
      )}
    </div>
  );
}

/** L'équipe a reçu l'acompte : moyen sur place (espèces, mobile money vérifié, terminal) et référence. */
function CollectDepositDialog({reservation, onClose, onDone}: {reservation: Reservation; onClose: () => void; onDone: () => Promise<void>}) {
  const deposit = reservation.deposit!;
  const {data: configurations, error} = useResource(signal => loadConfigurations(signal), []);
  const tenders = (configurations ? tendersOf(configurations, deposit.currencyCode) : []).filter(tender => tender.type === 'OnSite' && tender.method !== 'GiftCard');
  const [key, setKey] = useState('');
  const [reference, setReference] = useState('');
  const {busy, run} = useAction();
  const [failure, setFailure] = useState<string | null>(null);
  const tender = tenders.find(item => item.key === key) ?? tenders[0];

  return (
    <Modal open onClose={onClose} title={`Encaisser l’acompte · ${money(deposit.amount, deposit.currencyCode)}`} footer={
      <>
        <Button variant="secondary" onClick={onClose}>Annuler</Button>
        <Button busy={busy === 'save'} disabled={!tender || !reference.trim()} onClick={() => void run('save', async () => {
          try {
            await recordDeposit(reservation.id, tender!.provider, reference.trim(), tender!.method);
            await onDone();
          } catch (err) {
            setFailure(describe(err));
          }
        })}>Encaisser</Button>
      </>
    }>
      <div className="space-y-4">
        {(error || failure) && <Alert>{failure ?? error}</Alert>}
        <Field label="Moyen de paiement">
          <Select value={tender?.key ?? ''} onChange={event => setKey(event.target.value)}>
            {tenders.map(item => <option key={item.key} value={item.key}>{item.label || methodLabel[item.method]}</option>)}
          </Select>
        </Field>
        <Field label="Référence" hint="N° de ticket, de transaction mobile money ou de caisse.">
          <Input value={reference} maxLength={100} onChange={event => setReference(event.target.value)} autoFocus />
        </Field>
        {configurations && tenders.length === 0 && <Alert tone="amber">Aucun moyen de paiement sur place n’est activé (Paramètres &gt; Paiements).</Alert>}
        <p className="text-xs text-ink-500">L’acompte est déduit de l’addition le jour du repas.</p>
      </div>
    </Modal>
  );
}
