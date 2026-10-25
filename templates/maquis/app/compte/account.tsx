'use client';
import {useEffect, useState, useSyncExternalStore} from 'react';
import Link from 'next/link';
import {useRouter, useSearchParams} from 'next/navigation';
import {Gift, LogOut, Mail, MessageCircle, Phone, Receipt, Star} from 'lucide-react';
import {modeLabel, money, statusLabel} from '@/lib/format';
import {rememberedBookings, rememberedOrders, type RememberedOrder} from '@/lib/local';
import {
  errorMessage, loadAccount, loadCustomerOrders, onCustomerChange, readCustomerToken, requestSignInCode, signOut, verifySignInCode,
  type CodeSent, type CustomerAccount, type CustomerOrder, type SignInChannel,
} from '@/lib/storefront';
import {useSite} from '@/components/site-provider';
import {Button, Notice, PageShell, Spinner, choiceClass, cx, inputClass} from '@/components/ui';

const channels: {value: SignInChannel; label: string; icon: typeof Phone}[] = [
  {value: 'Email', label: 'E-mail', icon: Mail},
  {value: 'Sms', label: 'SMS', icon: Phone},
  {value: 'WhatsApp', label: 'WhatsApp', icon: MessageCircle},
];

/** Retours acceptés après connexion : pages du site seulement. */
const safeReturn = (value: string | null) => (value === '/commander/' || value === '/carte/' ? value : null);

export function Account() {
  const params = useSearchParams();
  const router = useRouter();
  const back = safeReturn(params.get('retour'));
  const signedIn = useSyncExternalStore(onCustomerChange, () => readCustomerToken() !== null, () => false);
  const {front} = useSite();

  return (
    <PageShell title="Mon compte" eyebrow={front?.restaurant.name}>
      {signedIn ? <Signed /> : (
        <div className="space-y-6">
          <SignIn onDone={() => back && router.push(back)} />
          <DeviceHistory />
        </div>
      )}
    </PageShell>
  );
}

function SignIn({onDone}: {onDone: () => void}) {
  const [channel, setChannel] = useState<SignInChannel>('Email');
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
      const result = await requestSignInCode(channel, isEmail ? address.trim() : address.replace(/[\s.\-()]/g, ''));
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
      const result = nameTicket ? await verifySignInCode(nameTicket, null, name.trim(), consent) : await verifySignInCode(sent.ticket, code.trim(), null, consent);
      if (result?.status === 'NameRequired' && result.ticket) setNameTicket(result.ticket);
      else if (result?.status === 'SignedIn') onDone();
    } catch (reason) {
      setError(errorMessage(reason));
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="rounded-[22px] border border-line bg-white p-6">
      {!sent ? (
        <form className="space-y-4" onSubmit={event => {
          event.preventDefault();
          void send();
        }}>
          <div>
            <h2 className="font-serif text-xl font-bold">Se connecter</h2>
            <p className="text-sm text-muted">Retrouvez vos commandes et vos points. Pas de mot de passe : nous vous envoyons un code.</p>
          </div>
          <div role="radiogroup" aria-label="Recevoir le code par" className="grid grid-cols-3 gap-2">
            {channels.map(({value, label, icon: Icon}) => (
              <button key={value} type="button" role="radio" aria-checked={channel === value} onClick={() => setChannel(value)} className={choiceClass(channel === value, 'flex items-center justify-center gap-2 px-2 py-2.5 font-medium')}>
                <Icon className="size-4" aria-hidden="true" />{label}
              </button>
            ))}
          </div>
          <label className="block text-sm">
            <span className="mb-1 block font-medium">{isEmail ? 'Adresse e-mail' : 'Numéro de téléphone'}</span>
            <input value={address} onChange={event => setAddress(event.target.value)} className={inputClass} required
              type={isEmail ? 'email' : 'tel'} autoComplete={isEmail ? 'email' : 'tel'} placeholder={isEmail ? 'vous@exemple.com' : '+229 01 97 00 00 00'} />
          </label>
          {error && <Notice tone="error">{error}</Notice>}
          <Button type="submit" className="w-full" busy={busy} disabled={!address.trim()}>Recevoir mon code</Button>
        </form>
      ) : (
        <form className="space-y-4" onSubmit={event => {
          event.preventDefault();
          void verify();
        }}>
          {nameTicket ? (
            <>
              <div>
                <h2 className="font-serif text-xl font-bold">Bienvenue !</h2>
                <p className="text-sm text-muted">C’est votre première visite : indiquez votre nom pour créer votre compte.</p>
              </div>
              <label className="block text-sm"><span className="mb-1 block font-medium">Votre nom</span>
                <input value={name} maxLength={100} onChange={event => setName(event.target.value)} className={inputClass} autoComplete="name" required />
              </label>
            </>
          ) : (
            <>
              <div>
                <h2 className="font-serif text-xl font-bold">Saisissez le code</h2>
                <p className="text-sm text-muted">Envoyé à {sent.destination}. Il est valable jusqu’à {new Date(sent.expiresAtUtc).toLocaleTimeString('fr-FR', {hour: '2-digit', minute: '2-digit'})}.</p>
              </div>
              <input value={code} onChange={event => setCode(event.target.value.replace(/\D/g, '').slice(0, 8))} inputMode="numeric" autoComplete="one-time-code"
                className={cx(inputClass, 'text-center font-mono text-2xl tracking-[0.4em]')} aria-label="Code reçu" placeholder="••••••" required />
            </>
          )}
          <label className="flex items-start gap-3 text-sm text-muted">
            <input type="checkbox" className="mt-0.5 size-4 accent-[var(--primary)]" checked={consent} onChange={event => setConsent(event.target.checked)} />
            J’accepte de recevoir les offres du restaurant.
          </label>
          {error && <Notice tone="error">{error}</Notice>}
          <Button type="submit" className="w-full" busy={busy} disabled={nameTicket ? !name.trim() : code.length < 4}>{nameTicket ? 'Créer mon compte' : 'Me connecter'}</Button>
          <button type="button" onClick={() => {
            setSent(null);
            setNameTicket(null);
            setError(null);
          }} className="w-full rounded-lg py-2 text-sm font-medium text-muted hover:text-ink">
            Changer d’adresse ou renvoyer un code
          </button>
        </form>
      )}
    </div>
  );
}

/** Commandes et réservations faites sur ce navigateur, sans compte. */
function DeviceHistory() {
  const [orders, setOrders] = useState<RememberedOrder[]>([]);
  const [bookings, setBookings] = useState<string[]>([]);
  useEffect(() => {
    setOrders(rememberedOrders());
    setBookings(rememberedBookings());
  }, []);
  if (orders.length === 0 && bookings.length === 0) return null;
  return (
    <div className="rounded-[22px] border border-line bg-white p-6">
      <h2 className="font-serif text-xl font-bold">Sur cet appareil</h2>
      <ul className="mt-2 divide-y divide-line text-sm">
        {orders.map(order => (
          <li key={order.token}>
            <Link href={`/commande/?jeton=${encodeURIComponent(order.token)}`} className="flex justify-between gap-3 py-3 hover:text-primary">
              <span className="font-medium">Commande n° {order.number}</span>
              <span className="text-muted">{new Date(order.at).toLocaleDateString('fr-FR', {day: 'numeric', month: 'short'})}</span>
            </Link>
          </li>
        ))}
        {bookings.map((token, index) => (
          <li key={token}>
            <Link href={`/reservation/?jeton=${encodeURIComponent(token)}`} className="block py-3 font-medium hover:text-primary">Réservation {bookings.length > 1 ? index + 1 : ''}</Link>
          </li>
        ))}
      </ul>
    </div>
  );
}

function Signed() {
  const [account, setAccount] = useState<CustomerAccount | null>(null);
  const [orders, setOrders] = useState<CustomerOrder[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [leaving, setLeaving] = useState(false);

  useEffect(() => {
    const controller = new AbortController();
    Promise.all([loadAccount(controller.signal), loadCustomerOrders(controller.signal)])
      .then(([loadedAccount, loadedOrders]) => {
        setAccount(loadedAccount);
        setOrders(loadedOrders ?? []);
      })
      .catch(reason => !controller.signal.aborted && setError(errorMessage(reason)));
    return () => controller.abort();
  }, []);

  if (error) return <Notice tone="error">{error}</Notice>;
  if (!account) return <Spinner />;
  const {loyalty} = account;
  return (
    <div className="space-y-4">
      <div className="rounded-[22px] border border-line bg-white p-6">
        <p className="text-lg font-semibold">{account.name}</p>
        <p className="text-sm text-muted">{[account.phone, account.email].filter(Boolean).join(' · ')}</p>
        <button type="button" disabled={leaving} onClick={() => {
          setLeaving(true);
          void signOut();
        }} className="mt-4 inline-flex items-center gap-2 rounded-lg text-sm font-medium text-muted hover:text-ink disabled:opacity-50">
          <LogOut className="size-4" aria-hidden="true" />Se déconnecter
        </button>
      </div>

      {loyalty.enabled && (
        <div className="rounded-[22px] border border-line bg-white p-6">
          <h2 className="flex items-center gap-2 font-serif text-xl font-bold"><Star className="size-5 text-primary" aria-hidden="true" />Fidélité</h2>
          <p className="mt-2 font-serif text-4xl font-bold text-primary">{loyalty.balance.toLocaleString('fr-FR')} <span className="font-sans text-base font-semibold text-muted">points</span></p>
          {loyalty.rewards.length > 0 && (
            <ul className="mt-4 space-y-2">
              {loyalty.rewards.map(reward => (
                <li key={reward.id} className={cx('flex items-center gap-3 rounded-xl border px-4 py-3 text-sm', reward.affordable ? 'border-emerald-200 bg-emerald-50' : 'border-line')}>
                  <Gift className={cx('size-5 shrink-0', reward.affordable ? 'text-emerald-700' : 'text-muted')} aria-hidden="true" />
                  <span className="min-w-0 flex-1">
                    <span className="block font-medium">{reward.name}</span>
                    <span className="text-muted">{reward.pointCost.toLocaleString('fr-FR')} points · −{money(reward.discount, account.currencyCode)}</span>
                  </span>
                  {reward.affordable && <span className="text-xs font-semibold text-emerald-800">Disponible</span>}
                </li>
              ))}
            </ul>
          )}
          {loyalty.rewards.some(reward => reward.affordable) && <p className="mt-2 text-xs text-muted">À utiliser au moment de commander.</p>}
        </div>
      )}

      <div className="rounded-[22px] border border-line bg-white p-6">
        <h2 className="flex items-center gap-2 font-serif text-xl font-bold"><Receipt className="size-5" aria-hidden="true" />Mes commandes</h2>
        {orders && orders.length === 0 && <p className="mt-2 text-sm text-muted">Aucune commande pour le moment.</p>}
        {orders && orders.length > 0 && (
          <ul className="mt-2 divide-y divide-line">
            {orders.map(order => {
              const body = (
                <>
                  <span className="min-w-0 flex-1">
                    <span className="block font-medium">N° {order.number} · {modeLabel[order.mode]}</span>
                    <span className="text-muted">{new Date(order.createdAtUtc).toLocaleDateString('fr-FR', {day: 'numeric', month: 'short', year: 'numeric'})}{order.locationName ? ` · ${order.locationName}` : ''}</span>
                  </span>
                  <span className="text-right">
                    <span className="block font-semibold">{money(order.total, order.currencyCode)}</span>
                    <span className={cx('text-xs', order.status === 'Cancelled' ? 'text-red-700' : 'text-muted')}>{statusLabel[order.status]}</span>
                  </span>
                </>
              );
              return (
                <li key={order.orderId}>
                  {order.trackingToken
                    ? <Link href={`/commande/?jeton=${encodeURIComponent(order.trackingToken)}`} className="flex items-center gap-3 py-3 text-sm hover:text-primary">{body}</Link>
                    : <div className="flex items-center gap-3 py-3 text-sm">{body}</div>}
                </li>
              );
            })}
          </ul>
        )}
      </div>
    </div>
  );
}
