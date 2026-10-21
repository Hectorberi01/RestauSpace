'use client';
import {useEffect, useState} from 'react';
import Link from 'next/link';
import {Gift, Minus, Pause, Play, Plus, Settings2, Star, UserPlus, XCircle} from 'lucide-react';
import {currencySymbol, money} from '@/lib/format';
import {useResource} from '@/lib/hooks';
import {loadCustomer} from '@/lib/customers';
import {
  accountAction, accountStatusLabel, accountStatusTone, adjustPoints, changeRewardCost, changeRewardValue, closeAccount, createReward,
  listAccounts, listRewards, listTransactions, loadProgram, openAccount, rewardAction, saveProgram, systemReason, transactionLabel,
  type AccountStatus, type LoyaltyAccount, type LoyaltyProgram, type Reward,
} from '@/lib/marketing';
import {useConsole} from '@/components/console';
import {CustomerPickerDialog} from '@/components/customer-picker';
import {Alert, Badge, Button, Card, Empty, Field, Input, Modal, Select, Spinner, Textarea, cx, useAction} from '@/components/ui';

const day = (iso: string) => new Intl.DateTimeFormat('fr-FR', {day: 'numeric', month: 'short', year: 'numeric'}).format(new Date(iso));
const points = (value: number) => `${new Intl.NumberFormat('fr-FR').format(value)} pt${Math.abs(value) > 1 ? 's' : ''}`;

function decimal(value: string): number | null {
  const parsed = Number(value.replace(',', '.').replace(/\s/g, ''));
  return value.trim() && Number.isFinite(parsed) ? parsed : null;
}

function Switch({checked, label, disabled, onChange}: {checked: boolean; label: string; disabled?: boolean; onChange: (value: boolean) => void}) {
  return (
    <button type="button" role="switch" aria-checked={checked} aria-label={label} disabled={disabled} onClick={() => onChange(!checked)}
      className={cx('relative h-6 w-11 shrink-0 rounded-full transition disabled:opacity-50', checked ? 'bg-brand-600' : 'bg-slate-300')}>
      <span className={cx('absolute top-0.5 size-5 rounded-full bg-white shadow transition-all', checked ? 'left-[1.375rem]' : 'left-0.5')} />
    </button>
  );
}

function ProgramCard() {
  const {can, tenant} = useConsole();
  const {busy, run} = useAction();
  const {data, error, reload} = useResource(signal => loadProgram(signal), []);
  const [form, setForm] = useState({enabled: false, rate: '', auto: true, expiry: ''});
  useEffect(() => {
    if (data) setForm({enabled: data.isEnabled, rate: String(data.pointsPerCurrencyUnit), auto: data.autoEnroll, expiry: data.inactivityExpiryDays ? String(data.inactivityExpiryDays) : ''});
  }, [data]);
  const editable = can('loyalty.configure');
  const currency = tenant.defaultCurrencyCode;
  const rate = decimal(form.rate);
  const sample = currency === 'XOF' || currency === 'XAF' ? 10_000 : 100;
  const next: LoyaltyProgram | null = rate === null ? null : {
    isEnabled: form.enabled, pointsPerCurrencyUnit: rate, autoEnroll: form.auto, inactivityExpiryDays: decimal(form.expiry),
  };
  const dirty = data && next && JSON.stringify(next) !== JSON.stringify({...data});

  return (
    <Card className="p-5">
      <div className="mb-4 flex items-start gap-3">
        <span className="grid size-10 shrink-0 place-items-center rounded-xl bg-amber-50 text-amber-600" aria-hidden="true"><Settings2 className="size-5" /></span>
        <div className="flex-1">
          <h2 className="font-semibold">Programme de fidélité</h2>
          <p className="text-sm text-ink-500">Les points sont gagnés automatiquement quand une commande d’un client rattaché est terminée.</p>
        </div>
        {data && <Switch checked={form.enabled} label="Programme actif" disabled={!editable} onChange={value => setForm(current => ({...current, enabled: value}))} />}
      </div>
      {error ? <Alert>{error}</Alert> : !data ? <Spinner /> : (
        <form className="grid grid-cols-1 gap-3 sm:grid-cols-3" onSubmit={event => {
          event.preventDefault();
          if (next) void run('program', async () => {
            await saveProgram(next);
            await reload();
          }, 'Programme enregistré.');
        }}>
          <Field label={`Points gagnés par ${currencySymbol(currency)} dépensé`} hint={rate && rate > 0 ? `${money(sample, currency)} rapportent ${Math.floor(sample * rate)} point${Math.floor(sample * rate) > 1 ? 's' : ''}.` : 'Ex. 0,01 = 1 point pour 100.'}>
            <Input inputMode="decimal" required disabled={!editable} value={form.rate} onChange={event => setForm(current => ({...current, rate: event.target.value}))} />
          </Field>
          <Field label="Expiration après inactivité (jours)" hint="Vide : les points n’expirent jamais. De 30 à 3650.">
            <Input type="number" min={30} max={3650} disabled={!editable} value={form.expiry} onChange={event => setForm(current => ({...current, expiry: event.target.value}))} placeholder="Jamais" />
          </Field>
          <label className="flex items-start gap-3 rounded-xl border border-slate-200 p-3 text-sm sm:mt-6">
            <input type="checkbox" className="mt-0.5 size-4 accent-brand-600" disabled={!editable} checked={form.auto} onChange={event => setForm(current => ({...current, auto: event.target.checked}))} />
            <span><span className="font-medium">Inscription automatique</span><span className="block text-xs text-ink-500">Compte ouvert à la première commande terminée.</span></span>
          </label>
          {editable && <div className="sm:col-span-3"><Button type="submit" variant="secondary" busy={busy === 'program'} disabled={!dirty}>Enregistrer le programme</Button></div>}
        </form>
      )}
    </Card>
  );
}

function RewardDialog({open, reward, onClose, onDone}: {open: boolean; reward: Reward | null; onClose: () => void; onDone: () => Promise<void>}) {
  const {tenant} = useConsole();
  const {busy, run} = useAction();
  const [form, setForm] = useState({name: '', cost: '', amount: ''});
  useEffect(() => {
    if (open) setForm({name: reward?.name ?? '', cost: reward ? String(reward.pointCost) : '', amount: reward ? String(reward.discountAmount) : ''});
  }, [open, reward]);
  const currency = reward?.currencyCode ?? tenant.defaultCurrencyCode;
  const cost = Number(form.cost);
  const amount = decimal(form.amount);
  const invalid = !form.name.trim() || !Number.isInteger(cost) || cost < 1 || amount === null || amount <= 0;

  return (
    <Modal open={open} onClose={onClose} title={reward ? `Modifier « ${reward.name} »` : 'Nouvelle récompense'} footer={
      <>
        <Button variant="secondary" onClick={onClose}>Annuler</Button>
        <Button type="submit" form="reward-form" busy={busy === 'reward'} disabled={invalid}>{reward ? 'Enregistrer' : 'Créer'}</Button>
      </>
    }>
      <form id="reward-form" className="space-y-4" onSubmit={event => {
        event.preventDefault();
        void run('reward', async () => {
          if (!reward) await createReward(form.name.trim(), cost, amount!, currency);
          else {
            if (cost !== reward.pointCost) await changeRewardCost(reward.id, cost);
            if (amount !== reward.discountAmount) await changeRewardValue(reward.id, amount!, currency);
          }
          await onDone();
          onClose();
        }, reward ? 'Récompense enregistrée.' : 'Récompense créée.');
      }}>
        {!reward && <Field label="Nom"><Input required maxLength={100} value={form.name} onChange={event => setForm(current => ({...current, name: event.target.value}))} placeholder="Boisson offerte" /></Field>}
        <div className="grid grid-cols-2 gap-3">
          <Field label="Coût en points"><Input type="number" min={1} required value={form.cost} onChange={event => setForm(current => ({...current, cost: event.target.value}))} placeholder="100" /></Field>
          <Field label={`Remise (${currency})`}><Input inputMode="decimal" required value={form.amount} onChange={event => setForm(current => ({...current, amount: event.target.value}))} placeholder="1500" /></Field>
        </div>
        <p className="text-xs text-ink-500">Au comptoir, la récompense devient une remise sur la commande du client ; ses points sont débités.</p>
      </form>
    </Modal>
  );
}

function Rewards() {
  const {can} = useConsole();
  const {busy, run} = useAction();
  const {data, error, reload} = useResource(signal => listRewards(false, signal), []);
  const [editing, setEditing] = useState<Reward | 'new' | null>(null);
  const configure = can('loyalty.configure');
  return (
    <Card className="p-5">
      <div className="mb-4 flex flex-wrap items-center gap-3">
        <span className="grid size-10 shrink-0 place-items-center rounded-xl bg-rose-50 text-rose-600" aria-hidden="true"><Gift className="size-5" /></span>
        <div className="flex-1">
          <h2 className="font-semibold">Récompenses</h2>
          <p className="text-sm text-ink-500">Ce que les clients obtiennent avec leurs points.</p>
        </div>
        {configure && <Button variant="secondary" onClick={() => setEditing('new')}><Plus className="size-4" aria-hidden="true" />Récompense</Button>}
      </div>
      {error ? <Alert>{error}</Alert> : !data ? <Spinner /> : data.length === 0 ? (
        <p className="text-sm text-ink-500">Aucune récompense : créez-en une pour que les points servent à quelque chose.</p>
      ) : (
        <ul className="divide-y divide-slate-100">
          {data.map(reward => (
            <li key={reward.id} className="flex flex-wrap items-center gap-3 py-3 text-sm">
              <div className={cx('min-w-0 flex-1', reward.status === 'Inactive' && 'opacity-60')}>
                <p className="font-medium">{reward.name}</p>
                <p className="text-ink-500">{points(reward.pointCost)} → −{money(reward.discountAmount, reward.currencyCode)}</p>
              </div>
              {reward.status === 'Inactive' && <Badge>Désactivée</Badge>}
              {configure && (
                <div className="flex gap-1">
                  <Button size="sm" variant="ghost" onClick={() => setEditing(reward)}>Modifier</Button>
                  <Button size="sm" variant="ghost" busy={busy === reward.id} onClick={() => run(reward.id, async () => {
                    await rewardAction(reward.id, reward.status === 'Active' ? 'deactivate' : 'activate');
                    await reload();
                  }, reward.status === 'Active' ? 'Récompense désactivée.' : 'Récompense activée.')}>{reward.status === 'Active' ? 'Désactiver' : 'Activer'}</Button>
                </div>
              )}
            </li>
          ))}
        </ul>
      )}
      <RewardDialog open={editing !== null} reward={editing === 'new' ? null : editing} onClose={() => setEditing(null)} onDone={reload} />
    </Card>
  );
}

export function AccountDialog({account, name, onClose, onChanged}: {account: LoyaltyAccount | null; name: string; onClose: () => void; onChanged: () => Promise<void>}) {
  const {can} = useConsole();
  const {busy, run} = useAction();
  const [page, setPage] = useState(1);
  const [adjust, setAdjust] = useState({direction: 'Credit' as 'Credit' | 'Debit', points: '', reason: ''});
  const [closing, setClosing] = useState(false);
  const [closeReason, setCloseReason] = useState('');
  useEffect(() => {
    setPage(1);
    setAdjust({direction: 'Credit', points: '', reason: ''});
    setClosing(false);
    setCloseReason('');
  }, [account?.id]);
  const history = useResource(signal => (account ? listTransactions(account.id, page, signal) : Promise.resolve(null)), [account?.id, account?.balance, page]);
  if (!account) return null;
  const manage = can('loyalty.manage') && account.status !== 'Closed';
  const amount = Number(adjust.points);

  return (
    <Modal wide open onClose={onClose} title={`Fidélité · ${name}`} footer={manage ? (
      <>
        {!closing && <Button variant="ghost" className="mr-auto text-red-600" onClick={() => setClosing(true)}><XCircle className="size-4" aria-hidden="true" />Clore le compte</Button>}
        {account.status === 'Active' && <Button variant="secondary" busy={busy === 'suspend'} onClick={() => run('suspend', async () => {
          await accountAction(account.id, 'suspend');
          await onChanged();
        }, 'Compte suspendu.')}><Pause className="size-4" aria-hidden="true" />Suspendre</Button>}
        {account.status === 'Suspended' && <Button busy={busy === 'reactivate'} onClick={() => run('reactivate', async () => {
          await accountAction(account.id, 'reactivate');
          await onChanged();
        }, 'Compte réactivé.')}><Play className="size-4" aria-hidden="true" />Réactiver</Button>}
      </>
    ) : undefined}>
      <div className="space-y-5">
        <div className="flex flex-wrap items-center gap-4">
          <div>
            <p className="text-sm text-ink-500">Solde</p>
            <p className="text-[1.75rem] leading-tight font-bold tracking-tight text-ink-900">{points(account.balance)}</p>
          </div>
          <Badge tone={accountStatusTone[account.status]}>{accountStatusLabel[account.status]}</Badge>
          <p className="text-sm text-ink-500">Ouvert le {day(account.openedAtUtc)}</p>
          {can('customers.view') && <Link href={`/clients/${account.customerId}`} className="ml-auto text-sm font-semibold text-brand-700 hover:underline">Fiche client</Link>}
        </div>
        {account.status === 'Suspended' && <Alert tone="amber">Compte suspendu : il ne gagne plus de points et ne peut pas utiliser de récompense.</Alert>}

        {closing && (
          <Card className="space-y-3 border-red-200 p-4">
            <p className="text-sm text-ink-700">Le solde restant ({points(account.balance)}) est perdu et le compte ne peut plus être rouvert.</p>
            <Field label="Motif"><Textarea rows={2} maxLength={250} value={closeReason} onChange={event => setCloseReason(event.target.value)} /></Field>
            <div className="flex gap-2">
              <Button variant="danger" busy={busy === 'close'} disabled={!closeReason.trim()} onClick={() => run('close', async () => {
                await closeAccount(account.id, closeReason.trim());
                setClosing(false);
                await onChanged();
              }, 'Compte clos.')}>Clore définitivement</Button>
              <Button variant="ghost" onClick={() => setClosing(false)}>Annuler</Button>
            </div>
          </Card>
        )}

        {manage && account.status === 'Active' && (
          <form className="grid grid-cols-1 gap-2 rounded-xl bg-slate-50 p-3 sm:grid-cols-[auto_8rem_1fr_auto] sm:items-end" onSubmit={event => {
            event.preventDefault();
            void run('adjust', async () => {
              await adjustPoints(account.id, amount, adjust.direction, adjust.reason.trim());
              setAdjust({direction: 'Credit', points: '', reason: ''});
              await onChanged();
            }, 'Ajustement enregistré.');
          }}>
            <div className="flex gap-1" role="radiogroup" aria-label="Sens de l’ajustement">
              {(['Credit', 'Debit'] as const).map(direction => (
                <button key={direction} type="button" role="radio" aria-checked={adjust.direction === direction} onClick={() => setAdjust(current => ({...current, direction}))}
                  className={cx('grid size-10 place-items-center rounded-xl border', adjust.direction === direction ? 'border-brand-500 bg-brand-50 text-brand-700' : 'border-slate-200 bg-white')}
                  aria-label={direction === 'Credit' ? 'Ajouter des points' : 'Retirer des points'}>
                  {direction === 'Credit' ? <Plus className="size-4" /> : <Minus className="size-4" />}
                </button>
              ))}
            </div>
            <Field label="Points"><Input type="number" min={1} required value={adjust.points} onChange={event => setAdjust(current => ({...current, points: event.target.value}))} /></Field>
            <Field label="Motif"><Input required maxLength={250} value={adjust.reason} onChange={event => setAdjust(current => ({...current, reason: event.target.value}))} placeholder="Geste commercial, erreur de caisse…" /></Field>
            <Button type="submit" variant="secondary" busy={busy === 'adjust'} disabled={!Number.isInteger(amount) || amount < 1 || !adjust.reason.trim()}>Ajuster</Button>
          </form>
        )}

        <section>
          <h3 className="mb-2 font-semibold">Historique</h3>
          {history.error ? <Alert>{history.error}</Alert> : !history.data ? <Spinner /> : history.data.items.length === 0 ? (
            <p className="text-sm text-ink-500">Aucun mouvement.</p>
          ) : (
            <ul className="divide-y divide-slate-100">
              {history.data.items.map(entry => (
                <li key={entry.id} className="flex items-center gap-3 py-2.5 text-sm">
                  <div className="min-w-0 flex-1">
                    <p className="font-medium">{transactionLabel[entry.type]}</p>
                    <p className="truncate text-ink-500">{day(entry.occurredAtUtc)}{entry.reason ? ` · ${systemReason[entry.reason] ?? entry.reason}` : ''}</p>
                  </div>
                  <span className={cx('font-semibold tabular-nums', entry.signedPoints >= 0 ? 'text-emerald-700' : 'text-red-700')}>
                    {entry.signedPoints >= 0 ? '+' : '−'}{points(Math.abs(entry.signedPoints))}
                  </span>
                </li>
              ))}
            </ul>
          )}
          {history.data && (page > 1 || history.data.hasMore) && (
            <div className="mt-2 flex justify-end gap-2">
              <Button size="sm" variant="secondary" disabled={page === 1} onClick={() => setPage(page - 1)}>Plus récents</Button>
              <Button size="sm" variant="secondary" disabled={!history.data.hasMore} onClick={() => setPage(page + 1)}>Plus anciens</Button>
            </div>
          )}
        </section>
      </div>
    </Modal>
  );
}

function Accounts() {
  const {can} = useConsole();
  const {run} = useAction();
  const [status, setStatus] = useState<AccountStatus | ''>('');
  const [page, setPage] = useState(1);
  const [openId, setOpenId] = useState<string | null>(null);
  const [names, setNames] = useState<Record<string, string>>({});
  const [picking, setPicking] = useState(false);
  const {data, error, reload} = useResource(signal => listAccounts(status, page, signal), [status, page]);
  const opened = data?.items.find(account => account.id === openId) ?? null;

  useEffect(() => {
    if (!data || !can('customers.view')) return;
    const missing = data.items.map(account => account.customerId).filter(id => !(id in names));
    if (missing.length === 0) return;
    void Promise.all(missing.map(id => loadCustomer(id).then(customer => [id, customer.name] as const).catch(() => [id, 'Client'] as const)))
      .then(entries => setNames(current => ({...current, ...Object.fromEntries(entries)})));
  }, [data, can, names]);

  return (
    <Card className="p-5">
      <div className="mb-4 flex flex-wrap items-center gap-3">
        <span className="grid size-10 shrink-0 place-items-center rounded-xl bg-brand-50 text-brand-700" aria-hidden="true"><Star className="size-5" /></span>
        <div className="flex-1">
          <h2 className="font-semibold">Comptes clients</h2>
          <p className="text-sm text-ink-500">Soldes de points et historique.</p>
        </div>
        <Select aria-label="Filtrer les comptes" className="w-full! sm:w-auto!" value={status} onChange={event => {
          setStatus(event.target.value as AccountStatus | '');
          setPage(1);
        }}>
          <option value="">Tous</option>
          {(Object.keys(accountStatusLabel) as AccountStatus[]).map(item => <option key={item} value={item}>{accountStatusLabel[item]}</option>)}
        </Select>
        {can('loyalty.operate') && can('customers.view') && <Button variant="secondary" onClick={() => setPicking(true)}><UserPlus className="size-4" aria-hidden="true" />Inscrire un client</Button>}
      </div>
      {error ? <Alert>{error}</Alert> : !data ? <Spinner /> : data.items.length === 0 ? (
        <Empty icon={<Star className="size-8" />} title="Aucun compte">Les comptes s’ouvrent à la première commande terminée (inscription automatique) ou à la main.</Empty>
      ) : (
        <>
          <ul className="divide-y divide-slate-100">
            {data.items.map(account => (
              <li key={account.id}>
                <button type="button" onClick={() => setOpenId(account.id)} className="flex w-full items-center gap-3 py-3 text-left text-sm hover:bg-slate-50">
                  <span className="min-w-0 flex-1">
                    <span className="block truncate font-medium">{names[account.customerId] ?? (can('customers.view') ? '…' : 'Client')}</span>
                    <span className="block text-ink-500">Depuis le {day(account.openedAtUtc)}</span>
                  </span>
                  {account.status !== 'Active' && <Badge tone={accountStatusTone[account.status]}>{accountStatusLabel[account.status]}</Badge>}
                  <span className="w-24 text-right font-semibold tabular-nums">{points(account.balance)}</span>
                </button>
              </li>
            ))}
          </ul>
          {(page > 1 || data.hasMore) && (
            <div className="mt-2 flex justify-end gap-2">
              <Button size="sm" variant="secondary" disabled={page === 1} onClick={() => setPage(page - 1)}>Précédents</Button>
              <Button size="sm" variant="secondary" disabled={!data.hasMore} onClick={() => setPage(page + 1)}>Suivants</Button>
            </div>
          )}
        </>
      )}
      <AccountDialog account={opened} name={opened ? names[opened.customerId] ?? 'Client' : ''} onClose={() => setOpenId(null)} onChanged={reload} />
      <CustomerPickerDialog open={picking} onClose={() => setPicking(false)} onPick={customer => run('open', async () => {
        await openAccount(customer.id);
        setNames(current => ({...current, [customer.id]: customer.name}));
        await reload();
      }, `Compte fidélité ouvert pour ${customer.name}.`).then(() => undefined)} />
    </Card>
  );
}

export function Loyalty() {
  return (
    <div className="space-y-4">
      <ProgramCard />
      <div className="grid grid-cols-1 items-start gap-4 xl:grid-cols-2">
        <Accounts />
        <Rewards />
      </div>
    </div>
  );
}

