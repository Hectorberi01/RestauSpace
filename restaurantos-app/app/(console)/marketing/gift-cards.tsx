'use client';
import {useState} from 'react';
import {CreditCard, Gift, Plus, Search, XCircle} from 'lucide-react';
import {money} from '@/lib/format';
import {useResource} from '@/lib/hooks';
import {
  cancelStoredValueCard, issueStoredValueCard, listStoredValueCards, loadStoredValueCard, lookupStoredValueCard, movementLabel, storedValueKindLabel,
  type StoredValueCard, type StoredValueKind,
} from '@/lib/marketing';
import {useConsole} from '@/components/console';
import {UpgradePanel, usePlanFeatures} from '@/components/plan';
import {Feature} from '@/lib/plan';
import {IssuedCode} from '@/components/issued-code';
import {Alert, Badge, Button, Card, Empty, Field, Input, Modal, Select, Spinner, Textarea, cx, useAction} from '@/components/ui';

const day = (iso: string) => new Intl.DateTimeFormat('fr-FR', {day: 'numeric', month: 'short', year: 'numeric'}).format(new Date(iso));

function amountOf(value: string): number | null {
  const parsed = Number(value.replace(',', '.').replace(/\s/g, ''));
  return value.trim() && Number.isFinite(parsed) && parsed > 0 ? parsed : null;
}

function StateBadge({card}: {card: StoredValueCard}) {
  if (card.status === 'Cancelled') return <Badge tone="red">Annulée</Badge>;
  if (card.isExpired) return <Badge tone="amber">Expirée</Badge>;
  if (card.balance <= 0) return <Badge tone="neutral">Épuisée</Badge>;
  return <Badge tone="green">Utilisable</Badge>;
}

const masked = (hint: string) => `•••• ${hint}`;

function IssueDialog({open, onClose, onIssued}: {open: boolean; onClose: () => void; onIssued: () => Promise<void>}) {
  const {tenant} = useConsole();
  const {busy, run} = useAction();
  const [form, setForm] = useState({kind: 'GiftCard' as StoredValueKind, amount: '', expires: '', note: ''});
  const [issued, setIssued] = useState<{code: string; amount: number; kind: StoredValueKind} | null>(null);
  const amount = amountOf(form.amount);
  const currency = tenant.defaultCurrencyCode;

  const close = () => {
    setIssued(null);
    setForm({kind: 'GiftCard', amount: '', expires: '', note: ''});
    onClose();
  };

  return (
    <Modal open={open} onClose={close} title={issued ? 'Carte émise' : 'Émettre une carte'} footer={issued ? (
      <Button onClick={close}>Terminé</Button>
    ) : (
      <>
        <Button variant="ghost" onClick={close}>Annuler</Button>
        <Button busy={busy === 'issue'} disabled={amount === null} onClick={() => void run('issue', async () => {
          const result = await issueStoredValueCard({
            kind: form.kind, amount: amount!, currencyCode: currency,
            expiresAtUtc: form.expires ? new Date(`${form.expires}T23:59:59`).toISOString() : null,
            note: form.note.trim() || null,
          });
          setIssued({code: result.code, amount: amount!, kind: form.kind});
          await onIssued();
        })}>Émettre</Button>
      </>
    )}>
      {issued ? <IssuedCode code={issued.code} amount={issued.amount} currency={currency} kind={issued.kind} /> : (
        <div className="space-y-3">
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
            <Field label="Type">
              <Select value={form.kind} onChange={event => setForm(current => ({...current, kind: event.target.value as StoredValueKind}))}>
                <option value="GiftCard">Carte cadeau</option>
                <option value="StoreCredit">Avoir</option>
              </Select>
            </Field>
            <Field label={`Montant (${currency})`}>
              <Input inputMode="decimal" required value={form.amount} onChange={event => setForm(current => ({...current, amount: event.target.value}))} />
            </Field>
          </div>
          <Field label="Valable jusqu’au" hint="Vide : sans date limite.">
            <Input type="date" value={form.expires} min={new Date().toISOString().slice(0, 10)} onChange={event => setForm(current => ({...current, expires: event.target.value}))} />
          </Field>
          <Field label="Note interne" hint="Facultatif : occasion, numéro de vente…">
            <Input maxLength={250} value={form.note} onChange={event => setForm(current => ({...current, note: event.target.value}))} />
          </Field>
          <p className="text-xs text-ink-500">Une carte vendue doit être encaissée à part, comme une vente. Le code est affiché une seule fois après l’émission.</p>
        </div>
      )}
    </Modal>
  );
}

function CardDialog({cardId, onClose, onChanged}: {cardId: string | null; onClose: () => void; onChanged: () => Promise<void>}) {
  const {can} = useConsole();
  const {busy, run} = useAction();
  const {data: card, error, reload} = useResource(signal => (cardId ? loadStoredValueCard(cardId, signal) : Promise.resolve(null)), [cardId]);
  const [cancelling, setCancelling] = useState(false);
  const [reason, setReason] = useState('');

  if (!cardId) return null;
  return (
    <Modal wide open onClose={onClose} title={card ? `${storedValueKindLabel[card.kind]} ${masked(card.codeHint)}` : 'Carte'} footer={
      card && can('loyalty.manage') && card.status === 'Active' && !cancelling
        ? <Button variant="danger" onClick={() => setCancelling(true)}><XCircle className="size-4" aria-hidden="true" />Annuler la carte</Button>
        : undefined
    }>
      {error ? <Alert>{error}</Alert> : !card ? <Spinner /> : (
        <div className="space-y-5">
          <div className="flex flex-wrap items-center gap-4">
            <div>
              <p className="text-sm text-ink-500">Solde</p>
              <p className="text-[1.75rem] leading-tight font-bold tracking-tight text-ink-900">{money(card.balance, card.currencyCode)}</p>
              <p className="text-sm text-ink-500">sur {money(card.initialAmount, card.currencyCode)}</p>
            </div>
            <StateBadge card={card} />
            <div className="ml-auto text-right text-sm text-ink-500">
              <p>Émise le {day(card.issuedAtUtc)}</p>
              {card.expiresAtUtc && <p>Valable jusqu’au {day(card.expiresAtUtc)}</p>}
            </div>
          </div>
          {card.note && <p className="rounded-xl bg-slate-50 px-3 py-2 text-sm">{card.note}</p>}
          {card.status === 'Cancelled' && <Alert tone="amber">Annulée le {day(card.cancelledAtUtc!)}{card.cancellationReason ? ` : ${card.cancellationReason}` : ''}.</Alert>}

          {cancelling && (
            <Card className="space-y-3 border-red-200 p-4">
              <p className="text-sm text-ink-700">Le solde restant ({money(card.balance, card.currencyCode)}) est perdu et la carte ne pourra plus servir.</p>
              <Field label="Motif"><Textarea rows={2} maxLength={250} value={reason} onChange={event => setReason(event.target.value)} placeholder="Perdue, émise par erreur…" /></Field>
              <div className="flex gap-2">
                <Button variant="danger" busy={busy === 'cancel'} disabled={!reason.trim()} onClick={() => void run('cancel', async () => {
                  await cancelStoredValueCard(card.id, reason.trim());
                  setCancelling(false);
                  await Promise.all([reload(), onChanged()]);
                }, 'Carte annulée.')}>Annuler définitivement</Button>
                <Button variant="ghost" onClick={() => setCancelling(false)}>Retour</Button>
              </div>
            </Card>
          )}

          <section>
            <h3 className="mb-2 font-semibold">Mouvements</h3>
            <ul className="divide-y divide-slate-100">
              {card.movements.map(movement => (
                <li key={movement.id} className="flex items-center gap-3 py-2.5 text-sm">
                  <div className="min-w-0 flex-1">
                    <p className="font-medium">{movementLabel[movement.type]}</p>
                    <p className="text-ink-500">{day(movement.occurredAtUtc)} · solde {money(movement.balanceAfter, card.currencyCode)}</p>
                  </div>
                  <span className={cx('font-semibold tabular-nums', movement.amount >= 0 ? 'text-emerald-700' : 'text-red-700')}>
                    {movement.amount >= 0 ? '+' : '−'}{money(Math.abs(movement.amount), card.currencyCode)}
                  </span>
                </li>
              ))}
            </ul>
          </section>
        </div>
      )}
    </Modal>
  );
}

export function GiftCards() {
  const {can} = useConsole();
  const {busy, run} = useAction();
  const [kind, setKind] = useState<StoredValueKind | ''>('');
  const [page, setPage] = useState(1);
  const [openId, setOpenId] = useState<string | null>(null);
  const [issuing, setIssuing] = useState(false);
  const [code, setCode] = useState('');
  const [notFound, setNotFound] = useState(false);
  const {data, error, reload} = useResource(signal => listStoredValueCards(kind, page, signal), [kind, page]);
  const locked = usePlanFeatures().blocked(Feature.MarketingGiftCards);

  const lookup = () => run('lookup', async () => {
    setNotFound(false);
    try {
      const card = await lookupStoredValueCard(code);
      setCode('');
      setOpenId(card.id);
    } catch (failure) {
      if ((failure as {status?: number}).status === 404) setNotFound(true);
      else throw failure;
    }
  });

  return (
    <div className="space-y-4">
      {locked && <UpgradePanel feature={Feature.MarketingGiftCards} title="Cartes cadeaux">Vendez des cartes cadeaux. Les cartes déjà vendues restent utilisables en caisse.</UpgradePanel>}
      <Card className="p-5">
        <div className="mb-4 flex flex-wrap items-start gap-3">
          <span className="grid size-10 shrink-0 place-items-center rounded-xl bg-rose-50 text-rose-600" aria-hidden="true"><Gift className="size-5" /></span>
          <div className="flex-1">
            <h2 className="font-semibold">Cartes cadeaux et avoirs</h2>
            <p className="text-sm text-ink-500">Acceptées en caisse comme moyen de paiement (« Carte cadeau ou avoir »). Un paiement remboursé revient sur la carte.</p>
          </div>
          {can('loyalty.manage') && !locked && <Button onClick={() => setIssuing(true)}><Plus className="size-4" aria-hidden="true" />Émettre</Button>}
        </div>
        <form className="flex flex-col gap-2 sm:flex-row sm:items-end" onSubmit={event => {
          event.preventDefault();
          if (code.trim()) void lookup();
        }}>
          <Field label="Vérifier un solde" className="flex-1">
            <Input value={code} autoComplete="off" spellCheck={false} placeholder="XXXX-XXXX-XXXX-XXXX" className="font-mono uppercase" onChange={event => {
              setCode(event.target.value);
              setNotFound(false);
            }} />
          </Field>
          <Button type="submit" variant="secondary" busy={busy === 'lookup'} disabled={!code.trim()}><Search className="size-4" aria-hidden="true" />Rechercher</Button>
        </form>
        {notFound && <p className="mt-2 text-sm text-red-700">Aucune carte avec ce code.</p>}
      </Card>

      <Card className="p-5">
        <div className="mb-4 flex flex-wrap items-center gap-3">
          <h2 className="flex-1 font-semibold">Cartes émises</h2>
          <Select aria-label="Filtrer les cartes" className="w-full! sm:w-auto!" value={kind} onChange={event => {
            setKind(event.target.value as StoredValueKind | '');
            setPage(1);
          }}>
            <option value="">Toutes</option>
            <option value="GiftCard">Cartes cadeaux</option>
            <option value="StoreCredit">Avoirs</option>
          </Select>
        </div>
        {error ? <Alert>{error}</Alert> : !data ? <Spinner /> : data.items.length === 0 ? (
          <Empty icon={<CreditCard className="size-8" />} title="Aucune carte">Émettez une carte cadeau, ou remboursez un paiement en avoir depuis une commande.</Empty>
        ) : (
          <>
            <ul className="divide-y divide-slate-100">
              {data.items.map(card => (
                <li key={card.id}>
                  <button type="button" onClick={() => setOpenId(card.id)} className="flex w-full items-center gap-3 py-3 text-left text-sm hover:bg-slate-50">
                    <span className="min-w-0 flex-1">
                      <span className="block font-medium">{storedValueKindLabel[card.kind]} <span className="font-mono text-ink-500">{masked(card.codeHint)}</span></span>
                      <span className="block truncate text-ink-500">Émise le {day(card.issuedAtUtc)}{card.note ? ` · ${card.note}` : ''}</span>
                    </span>
                    <StateBadge card={card} />
                    <span className="w-28 text-right font-semibold tabular-nums">{money(card.balance, card.currencyCode)}</span>
                  </button>
                </li>
              ))}
            </ul>
            {(page > 1 || data.hasMore) && (
              <div className="mt-2 flex justify-end gap-2">
                <Button size="sm" variant="secondary" disabled={page === 1} onClick={() => setPage(page - 1)}>Précédentes</Button>
                <Button size="sm" variant="secondary" disabled={!data.hasMore} onClick={() => setPage(page + 1)}>Suivantes</Button>
              </div>
            )}
          </>
        )}
      </Card>
      <IssueDialog open={issuing} onClose={() => setIssuing(false)} onIssued={reload} />
      <CardDialog cardId={openId} onClose={() => setOpenId(null)} onChanged={reload} />
    </div>
  );
}
