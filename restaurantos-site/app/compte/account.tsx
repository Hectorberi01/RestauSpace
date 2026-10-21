'use client';
import {useCallback, useEffect, useState, useSyncExternalStore} from 'react';
import Link from 'next/link';
import {useRouter, useSearchParams} from 'next/navigation';
import {ArrowLeft, Gift, LogOut, Mail, MessageCircle, Phone, Receipt, Star} from 'lucide-react';
import {
  accentOf,
  errorMessage,
  loadAccount,
  loadCustomerOrders,
  modeLabel,
  money,
  onCustomerChange,
  readCustomerToken,
  requestSignInCode,
  signOut,
  storefront,
  verifySignInCode,
  type CodeSent,
  type CustomerAccount,
  type CustomerOrder,
  type GuestOrderStatus,
  type SignInChannel,
  type Storefront,
} from '@/lib/storefront';
import {AccentButton, cx, Notice, Spinner} from '@/components/order-ui';
import {intlLocale, tr, translated, useLang} from '@/lib/i18n';

const statusLabel: Record<GuestOrderStatus, string> = translated({
  Received: 'Reçue',
  Accepted: 'Acceptée',
  Preparing: 'En préparation',
  Ready: 'Prête',
  Completed: 'Terminée',
  Cancelled: 'Annulée',
});

const channels: {value: SignInChannel; label: string; icon: typeof Phone}[] = [
  {value: 'Sms', label: 'SMS', icon: Phone},
  {value: 'WhatsApp', label: 'WhatsApp', icon: MessageCircle},
  {value: 'Email', label: 'E-mail', icon: Mail},
];

const useSignedIn = () => useSyncExternalStore(onCustomerChange, () => readCustomerToken() !== null, () => false);

/** Seules les pages de commande sont des retours acceptés (jamais une adresse extérieure). */
function safeReturn(value: string | null) {
  return value && /^\/commander(\/[0-9a-f-]{36})?$/i.test(value) ? value : null;
}

export function Account() {
  useLang();
  const params = useSearchParams();
  const router = useRouter();
  const back = safeReturn(params.get('retour'));
  const signedIn = useSignedIn();
  const [front, setFront] = useState<Storefront | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    storefront<Storefront>('/', {signal: controller.signal}).then(setFront).catch(() => {});
    return () => controller.abort();
  }, []);

  const accent = accentOf(front?.restaurant.primaryColor);
  return (
    <div style={{'--accent': accent} as React.CSSProperties} className="min-h-screen bg-slate-50">
      <div className="mx-auto max-w-xl space-y-5 px-4 py-8">
        <Link href={back ?? '/commander'} className="inline-flex items-center gap-2 text-sm font-medium text-ink-600 hover:text-ink-900">
          <ArrowLeft className="size-4" aria-hidden="true" />{back ? tr('Retour à la commande') : tr('Commander')}
        </Link>
        <h1 className="text-2xl font-bold">{front ? tr('Mon compte · {p0}', {p0: front.restaurant.name}) : tr('Mon compte')}</h1>
        {signedIn ? <Signed /> : <SignIn onDone={() => back && router.push(back)} />}
      </div>
    </div>
  );
}

function SignIn({onDone}: {onDone: () => void}) {
  const [channel, setChannel] = useState<SignInChannel>('Sms');
  const [address, setAddress] = useState('');
  const [sent, setSent] = useState<CodeSent | null>(null);
  const [code, setCode] = useState('');
  const [nameTicket, setNameTicket] = useState<string | null>(null);
  const [name, setName] = useState('');
  const [consent, setConsent] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const isEmail = channel === 'Email';

  const send = async () => {
    setBusy(true);
    setError(null);
    try {
      const result = await requestSignInCode(channel, address.trim());
      if (result) {
        setSent(result);
        setCode('');
      }
    } catch (reason) {
      setError(errorMessage(reason));
    } finally {
      setBusy(false);
    }
  };

  const verify = async () => {
    if (!sent) return;
    setBusy(true);
    setError(null);
    try {
      const result = nameTicket
        ? await verifySignInCode(nameTicket, null, name.trim(), consent)
        : await verifySignInCode(sent.ticket, code.trim(), undefined, consent);
      if (result?.status === 'NameRequired' && result.ticket) setNameTicket(result.ticket);
      else if (result?.status === 'SignedIn') onDone();
    } catch (reason) {
      setError(errorMessage(reason));
    } finally {
      setBusy(false);
    }
  };

  const input = 'w-full rounded-xl border border-slate-300 px-3 py-3';
  return (
    <div className="space-y-4 rounded-3xl bg-white p-6 shadow-sm">
      {!sent ? (
        <form className="space-y-4" onSubmit={event => {
          event.preventDefault();
          void send();
        }}>
          <div>
            <h2 className="text-lg font-semibold">{tr('Se connecter')}</h2>
            <p className="text-sm text-ink-500">{tr('Retrouvez vos commandes et vos points. Pas de mot de passe : nous vous envoyons un code.')}</p>
          </div>
          <div role="radiogroup" aria-label={tr('Recevoir le code par')} className="grid grid-cols-3 gap-2">
            {channels.map(({value, label, icon: Icon}) => (
              <button key={value} type="button" role="radio" aria-checked={channel === value} onClick={() => setChannel(value)}
                className={cx('flex items-center justify-center gap-2 rounded-xl border px-3 py-2.5 text-sm font-medium', channel === value ? 'border-[var(--accent)] bg-slate-50' : 'border-slate-200')}>
                <Icon className="size-4" aria-hidden="true" />{label}
              </button>
            ))}
          </div>
          <input value={address} onChange={event => setAddress(event.target.value)} className={input} required
            type={isEmail ? 'email' : 'tel'} autoComplete={isEmail ? 'email' : 'tel'} aria-label={isEmail ? tr('Adresse e-mail') : tr('Numéro de téléphone')}
            placeholder={isEmail ? 'vous@exemple.com' : '+229 90 00 00 00'} />
          {error && <Notice tone="error">{error}</Notice>}
          <AccentButton type="submit" className="w-full" busy={busy} disabled={!address.trim()}>{tr('Recevoir mon code')}</AccentButton>
        </form>
      ) : (
        <form className="space-y-4" onSubmit={event => {
          event.preventDefault();
          void verify();
        }}>
          {nameTicket ? (
            <>
              <div>
                <h2 className="text-lg font-semibold">{tr('Bienvenue !')}</h2>
                <p className="text-sm text-ink-500">{tr('C’est votre première visite : indiquez votre nom pour créer votre compte.')}</p>
              </div>
              <input value={name} maxLength={100} onChange={event => setName(event.target.value)} className={input} autoComplete="name" aria-label={tr('Nom')} placeholder={tr('Votre nom')} required />
            </>
          ) : (
            <>
              <div>
                <h2 className="text-lg font-semibold">{tr('Saisissez le code')}</h2>
                <p className="text-sm text-ink-500">{tr('Envoyé à')} {sent.destination}{tr('. Il est valable jusqu’à')} {new Date(sent.expiresAtUtc).toLocaleTimeString(intlLocale(), {hour: '2-digit', minute: '2-digit'})}.</p>
              </div>
              <input value={code} onChange={event => setCode(event.target.value.replace(/\D/g, '').slice(0, 8))} inputMode="numeric" autoComplete="one-time-code"
                className={cx(input, 'text-center font-mono text-2xl tracking-[0.4em]')} aria-label={tr('Code reçu')} placeholder="••••••" required />
            </>
          )}
          <label className="flex items-start gap-3 text-sm text-ink-600">
            <input type="checkbox" className="mt-0.5 size-4 accent-[var(--accent)]" checked={consent} onChange={event => setConsent(event.target.checked)} />
            {tr('J’accepte de recevoir les offres du restaurant.')}
          </label>
          {error && <Notice tone="error">{error}</Notice>}
          <AccentButton type="submit" className="w-full" busy={busy} disabled={nameTicket ? !name.trim() : code.length < 4}>
            {nameTicket ? tr('Créer mon compte') : tr('Me connecter')}
          </AccentButton>
          <button type="button" onClick={() => {
            setSent(null);
            setNameTicket(null);
            setError(null);
          }} className="w-full text-sm font-medium text-ink-600 hover:text-ink-900">
            {tr('Changer de numéro ou renvoyer un code')}
          </button>
        </form>
      )}
    </div>
  );
}

function Signed() {
  const [account, setAccount] = useState<CustomerAccount | null>(null);
  const [orders, setOrders] = useState<CustomerOrder[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [leaving, setLeaving] = useState(false);

  const load = useCallback((signal: AbortSignal) => {
    Promise.all([loadAccount(signal), loadCustomerOrders(signal)])
      .then(([loadedAccount, loadedOrders]) => {
        setAccount(loadedAccount);
        setOrders(loadedOrders ?? []);
      })
      .catch(reason => !signal.aborted && setError(errorMessage(reason)));
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    load(controller.signal);
    return () => controller.abort();
  }, [load]);

  if (error) return <Notice tone="error">{error}</Notice>;
  if (!account) return <Spinner />;
  const {loyalty} = account;
  return (
    <div className="space-y-4">
      <div className="rounded-3xl bg-white p-6 shadow-sm">
        <p className="text-lg font-semibold">{account.name}</p>
        <p className="text-sm text-ink-500">{[account.phone, account.email].filter(Boolean).join(' · ')}</p>
        <button type="button" disabled={leaving} onClick={() => {
          setLeaving(true);
          void signOut();
        }} className="mt-4 inline-flex items-center gap-2 text-sm font-medium text-ink-600 hover:text-ink-900 disabled:opacity-50">
          <LogOut className="size-4" aria-hidden="true" />{tr('Se déconnecter')}
        </button>
      </div>

      {loyalty.enabled && (
        <div className="rounded-3xl bg-white p-6 shadow-sm">
          <h2 className="flex items-center gap-2 font-semibold"><Star className="size-5 text-[var(--accent)]" aria-hidden="true" />{tr('Fidélité')}</h2>
          <p className="mt-2 text-4xl font-black text-[var(--accent)]">{loyalty.balance.toLocaleString('fr-FR')} <span className="text-base font-semibold text-ink-600">{tr('points')}</span></p>
          {loyalty.pointsPerCurrencyUnit > 0 && (
            <p className="text-sm text-ink-500">{loyalty.pointsPerCurrencyUnit.toLocaleString('fr-FR')} {tr('point(s) par')} {account.currencyCode === 'XOF' ? tr('franc CFA') : account.currencyCode} {tr('payé, une fois la commande remise.')}</p>
          )}
          {loyalty.rewards.length > 0 && (
            <ul className="mt-4 space-y-2">
              {loyalty.rewards.map(reward => (
                <li key={reward.id} className={cx('flex items-center gap-3 rounded-xl border px-4 py-3 text-sm', reward.affordable ? 'border-emerald-200 bg-emerald-50' : 'border-slate-200')}>
                  <Gift className={cx('size-5 shrink-0', reward.affordable ? 'text-emerald-700' : 'text-ink-400')} aria-hidden="true" />
                  <span className="min-w-0 flex-1">
                    <span className="block font-medium">{reward.name}</span>
                    <span className="text-ink-500">{reward.pointCost.toLocaleString('fr-FR')} {tr('points · −')}{money(reward.discount, account.currencyCode)}</span>
                  </span>
                  {reward.affordable && <span className="text-xs font-semibold text-emerald-800">{tr('Disponible')}</span>}
                </li>
              ))}
            </ul>
          )}
          {loyalty.rewards.some(reward => reward.affordable) && <p className="mt-2 text-xs text-ink-500">{tr('À utiliser au moment de commander.')}</p>}
        </div>
      )}

      <div className="rounded-3xl bg-white p-6 shadow-sm">
        <h2 className="flex items-center gap-2 font-semibold"><Receipt className="size-5" aria-hidden="true" />{tr('Mes commandes')}</h2>
        {orders && orders.length === 0 && <p className="mt-2 text-sm text-ink-500">{tr('Aucune commande pour le moment.')}</p>}
        {orders && orders.length > 0 && (
          <ul className="mt-2 divide-y divide-slate-100">
            {orders.map(order => {
              const body = (
                <>
                  <span className="min-w-0 flex-1">
                    <span className="block font-medium">{tr('N°')} {order.number} · {modeLabel[order.mode]}</span>
                    <span className="text-ink-500">{new Date(order.createdAtUtc).toLocaleDateString(intlLocale(), {day: 'numeric', month: 'short', year: 'numeric'})}{order.locationName ? ` · ${order.locationName}` : ''}</span>
                  </span>
                  <span className="text-right">
                    <span className="block font-semibold">{money(order.total, order.currencyCode)}</span>
                    <span className={cx('text-xs', order.status === 'Cancelled' ? 'text-red-700' : 'text-ink-500')}>{statusLabel[order.status]}</span>
                  </span>
                </>
              );
              return (
                <li key={order.orderId}>
                  {order.trackingToken ? (
                    <Link href={`/commande?jeton=${encodeURIComponent(order.trackingToken)}`} className="flex items-center gap-3 py-3 text-sm hover:bg-slate-50">{body}</Link>
                  ) : (
                    <div className="flex items-center gap-3 py-3 text-sm">{body}</div>
                  )}
                </li>
              );
            })}
          </ul>
        )}
      </div>
    </div>
  );
}
