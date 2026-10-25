'use client';
import {useEffect, useMemo, useRef, useState, useSyncExternalStore} from 'react';
import Link from 'next/link';
import {useRouter} from 'next/navigation';
import {ArrowLeft, Bike, Check, CircleUser, Gift, Minus, Plus, Tag, Trash2, X} from 'lucide-react';
import {absoluteUrl, config} from '@/lib/config';
import {goToCheckout, safeCheckoutUrl} from '@/lib/checkout';
import {clock, methodLabel, modeLabel, money} from '@/lib/format';
import {rememberOrder, useCart, useModeChoice, writeModeChoice} from '@/lib/local';
import {
  componentLabel, couponRefusal, errorMessage, loadAccount, newIdempotencyKey, onCustomerChange, readCustomerToken, startOrderPayment, storefront,
  StorefrontError, toPayload, type CustomerAccount, type PaymentMode, type PlacedOrder, type Quote, type Slots,
} from '@/lib/storefront';
import {useSite} from '@/components/site-provider';
import {useCatalog} from '@/components/use-catalog';
import {Button, ButtonLink, Notice, PageShell, Spinner, choiceClass, cx, inputClass} from '@/components/ui';
import {lineDetails, modesOf} from '../carte/menu';

interface Choice {
  payment: PaymentMode;
  provider: string | null;
  method: string | null;
}

/** Un créneau lisible dans le fuseau de l'établissement : « lun. 19 janv. » et « 12:30 ». */
function slotParts(utc: string, timeZone: string) {
  const at = new Date(utc);
  return {
    day: new Intl.DateTimeFormat('fr-FR', {weekday: 'short', day: 'numeric', month: 'short', timeZone}).format(at),
    time: new Intl.DateTimeFormat('fr-FR', {hour: '2-digit', minute: '2-digit', timeZone}).format(at),
  };
}

/** Validation de la commande : devis du serveur, mode, livraison, créneau, coordonnées, code promo, paiement. */
export function Checkout() {
  const router = useRouter();
  const {front, location, error: siteError} = useSite();
  const {catalog} = useCatalog(location?.id ?? null);
  const [cart, setCart] = useCart(location?.id ?? null);
  const choiceStored = useModeChoice();
  const modes = location ? modesOf(location) : [];
  const mode = modes.find(item => item === choiceStored.mode) ?? modes[0] ?? 'Takeaway';
  const zones = location?.delivery?.zones ?? [];
  const zoneId = zones.find(zone => zone.id === choiceStored.zoneId)?.id ?? zones[0]?.id ?? '';
  const zone = zones.find(item => item.id === zoneId) ?? null;
  const delivering = mode === 'Delivery';

  const [address, setAddress] = useState({line: '', landmark: '', instructions: ''});
  const [couponInput, setCouponInput] = useState('');
  const [couponCode, setCouponCode] = useState<string | null>(null);
  const [quote, setQuote] = useState<Quote | null>(null);
  const [quoteError, setQuoteError] = useState<string | null>(null);
  const [when, setWhen] = useState<'now' | 'later' | null>(null);
  const [slots, setSlots] = useState<Slots | null>(null);
  const [slot, setSlot] = useState('');
  const [customer, setCustomer] = useState({name: '', phone: '', email: '', consent: false});
  const [choice, setChoice] = useState<Choice>({payment: 'Counter', provider: null, method: null});
  const signedIn = useSyncExternalStore(onCustomerChange, () => readCustomerToken() !== null, () => false);
  const [account, setAccount] = useState<CustomerAccount | null>(null);
  const [rewardId, setRewardId] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const idempotency = useRef(newIdempotencyKey());
  const payload = useMemo(() => cart.map(toPayload), [cart]);
  const moment = when ?? (location?.acceptsOrdersNow === false ? 'later' : 'now');

  // Client connecté : sa fiche remplit les coordonnées, ses points ouvrent les récompenses.
  useEffect(() => {
    if (!signedIn) {
      setAccount(null);
      setRewardId(null);
      return;
    }
    const controller = new AbortController();
    loadAccount(controller.signal)
      .then(loaded => {
        setAccount(loaded);
        if (loaded) setCustomer(current => ({...current, name: loaded.name, phone: loaded.phone ?? current.phone, email: loaded.email ?? ''}));
      })
      .catch(() => {});
    return () => controller.abort();
  }, [signedIn]);

  // Devis à chaque changement du panier, du mode, du quartier, du code promo ou de la récompense.
  useEffect(() => {
    idempotency.current = newIdempotencyKey();
    if (!location || cart.length === 0) {
      setQuote(null);
      return;
    }
    const controller = new AbortController();
    const timer = window.setTimeout(() => {
      storefront<Quote>('/cart/quote', {
        method: 'POST',
        body: {locationId: location.id, mode, lines: payload, couponCode, deliveryZoneId: delivering && zoneId ? zoneId : null, rewardId: account ? rewardId : null},
        signal: controller.signal,
      })
        .then(result => {
          setQuote(result);
          setQuoteError(null);
          setChoice(current => {
            if (!result) return current;
            if (current.payment === 'Counter' && !result.canPayAtCounter && result.canPayOnline && result.onlinePaymentOptions[0]) {
              const option = result.onlinePaymentOptions[0];
              return {payment: 'Online', provider: option.provider, method: option.methods[0] ?? null};
            }
            if (current.payment === 'Online' && !result.onlinePaymentOptions.some(option => option.provider === current.provider)) return {payment: 'Counter', provider: null, method: null};
            return current;
          });
        })
        .catch(reason => !controller.signal.aborted && setQuoteError(errorMessage(reason)));
    }, 250);
    return () => {
      controller.abort();
      window.clearTimeout(timer);
    };
  }, [payload, mode, location, cart.length, couponCode, delivering, zoneId, rewardId, account]);

  // Créneaux quand le client choisit une heure.
  useEffect(() => {
    if (moment !== 'later' || !location) return;
    const controller = new AbortController();
    const query = new URLSearchParams({mode});
    if (delivering && zoneId) query.set('zoneId', zoneId);
    setSlots(null);
    storefront<Slots>(`/locations/${location.id}/slots?${query}`, {signal: controller.signal})
      .then(result => {
        setSlots(result);
        setSlot(current => (result?.slots.some(item => item.startUtc === current && item.isAvailable) ? current : ''));
      })
      .catch(reason => !controller.signal.aborted && setQuoteError(errorMessage(reason)));
    return () => controller.abort();
  }, [moment, mode, delivering, zoneId, location]);

  const days = useMemo(() => {
    if (!slots) return [];
    const grouped = new Map<string, {startUtc: string; time: string; isAvailable: boolean}[]>();
    for (const item of slots.slots) {
      const {day, time} = slotParts(item.startUtc, slots.timeZoneId);
      grouped.set(day, [...(grouped.get(day) ?? []), {startUtc: item.startUtc, time, isAvailable: item.isAvailable}]);
    }
    return [...grouped.entries()];
  }, [slots]);

  if (siteError) return <PageShell title="Votre commande"><Notice tone="error">{siteError}</Notice></PageShell>;
  if (!front || !location || !catalog) return <PageShell title="Votre commande"><Spinner /></PageShell>;
  if (front.orderingEnabled === false) return <PageShell title="Votre commande"><Notice tone="warning">La commande en ligne n’est pas disponible pour le moment.</Notice></PageShell>;
  if (cart.length === 0) {
    return (
      <PageShell title="Votre commande">
        <Notice>Votre panier est vide.</Notice>
        <ButtonLink href={`/carte/?etablissement=${location.id}`} className="mt-4">Voir la carte</ButtonLink>
      </PageShell>
    );
  }

  const setQuantity = (key: string, quantity: number) =>
    setCart(quantity <= 0 ? cart.filter(line => line.key !== key) : cart.map(line => (line.key === key ? {...line, quantity: Math.min(99, quantity)} : line)));

  const currency = quote?.currencyCode ?? catalog.currencyCode;
  const phoneNeeded = delivering;
  const couponOk = !couponCode || quote?.coupon?.isValid === true;
  const deliveryOk = !delivering || (!!zone && customer.phone.trim() !== '' && address.line.trim() !== '');
  const contactOk = customer.name.trim() !== '' && (customer.phone.trim() !== '' || customer.email.trim() !== '');
  const canSubmit = !!quote && quote.meetsMinimum && contactOk && deliveryOk && couponOk
    && (moment === 'now' ? location.acceptsOrdersNow : slot !== '')
    && (choice.payment === 'Counter' ? quote.canPayAtCounter : choice.provider !== null);
  const signInHref = `/compte/?retour=${encodeURIComponent('/commander/')}`;

  const submit = async () => {
    if (!quote || !canSubmit) return;
    setBusy(true);
    setError(null);
    try {
      const placed = await storefront<PlacedOrder>('/orders', {
        method: 'POST',
        idempotencyKey: idempotency.current,
        humanCheck: true,
        body: {
          locationId: location.id,
          mode,
          lines: payload,
          payment: choice.payment,
          customer: {name: customer.name.trim(), phone: customer.phone.replace(/[\s.\-()]/g, '') || null, email: customer.email.trim() || null, marketingConsent: customer.consent, language: 'fr'},
          scheduledForUtc: moment === 'later' ? slot : null,
          expectedTotal: quote.total,
          couponCode: quote.coupon?.isValid ? quote.coupon.code : null,
          delivery: delivering ? {zoneId, addressLine: address.line.trim(), landmark: address.landmark.trim() || null, instructions: address.instructions.trim() || null} : null,
          rewardId: account && quote.reward ? quote.reward.rewardId : null,
        },
      });
      if (!placed) return;
      rememberOrder(placed.trackingToken, placed.number);
      setCart([]);
      const tracking = `/commande/?jeton=${encodeURIComponent(placed.trackingToken)}`;
      if (choice.payment === 'Online' && choice.provider) {
        try {
          const started = await startOrderPayment(placed.trackingToken, {
            provider: choice.provider, method: choice.method, payerPhone: customer.phone.replace(/[\s.\-()]/g, '') || null, returnUrl: absoluteUrl(tracking),
          });
          const checkout = safeCheckoutUrl(started?.checkoutUrl);
          if (checkout) {
            goToCheckout(checkout, absoluteUrl(tracking));
            return;
          }
          router.push(`${tracking}&telephone=1`);
        } catch {
          router.push(tracking);
        }
        return;
      }
      router.push(tracking);
    } catch (reason) {
      setError(errorMessage(reason));
      // Un refus (4xx) clôt cette tentative : la suivante, corrigée, est une nouvelle demande.
      if (reason instanceof StorefrontError && reason.status >= 400 && reason.status < 500) idempotency.current = newIdempotencyKey();
      if (reason instanceof StorefrontError && reason.code === 'storefront.checkout.price_changed') setQuote(null);
      if (reason instanceof StorefrontError && reason.code === 'storefront.checkout.slot_full') setSlot('');
    } finally {
      setBusy(false);
    }
  };

  return (
    <PageShell title="Votre commande" eyebrow={front.restaurant.name} wide>
      <Link href={`/carte/?etablissement=${location.id}`} className="-mt-3 mb-6 inline-flex items-center gap-2 text-sm font-medium text-muted hover:text-ink">
        <ArrowLeft className="size-4" aria-hidden="true" />Continuer mes achats
      </Link>
      <div className="grid gap-8 lg:grid-cols-[1fr_400px] lg:items-start">
        <form id="commande" className="space-y-7" onSubmit={event => {
          event.preventDefault();
          void submit();
        }}>
          {front.locations.length > 1 && <p className="text-sm text-muted">Établissement : <b className="text-ink">{location.name}</b></p>}

          {modes.length > 1 && (
            <fieldset>
              <legend className="mb-2 font-serif text-xl font-bold">Mode</legend>
              <div className={cx('grid gap-2', modes.length === 3 ? 'grid-cols-3' : 'grid-cols-2')}>
                {modes.map(item => (
                  <button key={item} type="button" aria-pressed={mode === item} onClick={() => writeModeChoice(item)} className={choiceClass(mode === item, 'px-3 py-3 font-semibold')}>
                    {modeLabel[item]}
                  </button>
                ))}
              </div>
            </fieldset>
          )}

          {delivering && (
            <fieldset className="space-y-3">
              <legend className="mb-2 flex items-center gap-2 font-serif text-xl font-bold"><Bike className="size-5 text-primary" aria-hidden="true" />Livraison</legend>
              <label className="block text-sm">
                <span className="mb-1 block font-medium">Votre quartier</span>
                <select value={zoneId} onChange={event => writeModeChoice('Delivery', event.target.value)} className={inputClass}>
                  {zones.map(item => (
                    <option key={item.id} value={item.id}>{item.name} · {item.fee > 0 ? money(item.fee, currency) : 'livraison offerte'} · ~{item.estimatedMinutes} min</option>
                  ))}
                </select>
              </label>
              {zone && zone.minimumOrder > 0 && <p className="text-xs text-muted">Minimum de commande pour {zone.name} : {money(zone.minimumOrder, currency)}.</p>}
              <label className="block text-sm">
                <span className="mb-1 block font-medium">Adresse</span>
                <input autoComplete="street-address" required placeholder="Rue, maison, étage" value={address.line} maxLength={200}
                  onChange={event => setAddress(current => ({...current, line: event.target.value}))} className={inputClass} />
              </label>
              <label className="block text-sm">
                <span className="mb-1 block font-medium">Repère <span className="font-normal text-muted">(facultatif)</span></span>
                <input placeholder="Face à la pharmacie…" value={address.landmark} maxLength={200} onChange={event => setAddress(current => ({...current, landmark: event.target.value}))} className={inputClass} />
              </label>
              <label className="block text-sm">
                <span className="mb-1 block font-medium">Consignes au livreur <span className="font-normal text-muted">(facultatif)</span></span>
                <textarea value={address.instructions} maxLength={500} rows={2} onChange={event => setAddress(current => ({...current, instructions: event.target.value}))} className={inputClass} />
              </label>
            </fieldset>
          )}

          <fieldset>
            <legend className="mb-2 font-serif text-xl font-bold">{delivering ? 'Livrée quand ?' : 'Prête quand ?'}</legend>
            <div className="grid grid-cols-2 gap-2">
              <button type="button" aria-pressed={moment === 'now'} disabled={!location.acceptsOrdersNow} onClick={() => setWhen('now')} className={choiceClass(moment === 'now', 'px-4 py-3 font-semibold disabled:opacity-50')}>
                Dès que possible
              </button>
              <button type="button" aria-pressed={moment === 'later'} onClick={() => setWhen('later')} className={choiceClass(moment === 'later', 'px-4 py-3 font-semibold')}>
                Choisir l’heure
              </button>
            </div>
            {moment === 'now' && location.acceptsOrdersNow && (
              <p className="mt-2 text-sm text-muted">
                {delivering && zone && location.delivery
                  ? `Livraison estimée vers ${clock(new Date(Date.now() + (location.delivery.leadMinutes + zone.estimatedMinutes) * 60000).toISOString(), location.timeZoneId)}.`
                  : `Prête vers ${clock(new Date(Date.now() + location.preparationMinutes * 60000).toISOString(), location.timeZoneId)}.`}
              </p>
            )}
            {moment === 'later' && (
              <label className="mt-3 block text-sm">
                <span className="mb-1 block font-medium">Créneau</span>
                {!slots ? <span className="block text-muted">Chargement des créneaux…</span> : days.length === 0 ? (
                  <span className="block text-muted">Aucun créneau dans les 7 prochains jours.</span>
                ) : (
                  <select value={slot} onChange={event => setSlot(event.target.value)} className={inputClass} required>
                    <option value="">Choisissez un créneau</option>
                    {days.map(([day, items]) => (
                      <optgroup key={day} label={day}>
                        {items.map(item => (
                          <option key={item.startUtc} value={item.startUtc} disabled={!item.isAvailable}>{day} · {item.time}{item.isAvailable ? '' : ' (complet)'}</option>
                        ))}
                      </optgroup>
                    ))}
                  </select>
                )}
              </label>
            )}
          </fieldset>

          {account?.loyalty.enabled && account.loyalty.rewards.length > 0 && (
            <fieldset>
              <legend className="mb-2 flex items-center gap-2 font-serif text-xl font-bold"><Gift className="size-5 text-primary" aria-hidden="true" />Mes points · {account.loyalty.balance.toLocaleString('fr-FR')}</legend>
              <div className="space-y-2">
                <label className={choiceClass(rewardId === null, 'flex items-center gap-3 px-4 py-3')}>
                  <input type="radio" name="reward" className="size-4 accent-[var(--primary)]" checked={rewardId === null} onChange={() => setRewardId(null)} />
                  Garder mes points
                </label>
                {account.loyalty.rewards.map(reward => (
                  <label key={reward.id} className={choiceClass(rewardId === reward.id, cx('flex items-center gap-3 px-4 py-3', !reward.affordable && 'opacity-50'))}>
                    <input type="radio" name="reward" className="size-4 accent-[var(--primary)]" disabled={!reward.affordable} checked={rewardId === reward.id} onChange={() => setRewardId(reward.id)} />
                    <span className="flex-1">{reward.name} <span className="text-muted">· {reward.pointCost.toLocaleString('fr-FR')} points</span></span>
                    <span className="font-medium text-leaf">−{money(reward.discount, currency)}</span>
                  </label>
                ))}
              </div>
            </fieldset>
          )}

          <fieldset className="space-y-3">
            <legend className="mb-2 font-serif text-xl font-bold">Vos coordonnées</legend>
            {account ? (
              <>
                <p className="flex items-center gap-2 rounded-xl border border-line bg-white px-4 py-3 text-sm">
                  <CircleUser className="size-5 text-muted" aria-hidden="true" />
                  <span className="min-w-0 flex-1 truncate">{account.name}{account.phone ? ` · ${account.phone}` : account.email ? ` · ${account.email}` : ''}</span>
                  <Link href={signInHref} className="text-xs font-semibold text-primary hover:underline">Mon compte</Link>
                </p>
                {phoneNeeded && !account.phone && (
                  <input autoComplete="tel" type="tel" required placeholder="Téléphone (ex. +229 01 97 00 00 00)" value={customer.phone} onChange={event => setCustomer(c => ({...c, phone: event.target.value}))} className={inputClass} aria-label="Téléphone" />
                )}
              </>
            ) : (
              <>
                <p className="text-sm text-muted">Déjà client ? <Link href={signInHref} className="font-semibold text-primary hover:underline">Connectez-vous</Link> pour retrouver vos commandes et vos points.</p>
                <label className="block text-sm"><span className="mb-1 block font-medium">Nom</span>
                  <input autoComplete="name" required maxLength={100} value={customer.name} onChange={event => setCustomer(c => ({...c, name: event.target.value}))} className={inputClass} />
                </label>
                <label className="block text-sm"><span className="mb-1 block font-medium">Téléphone {phoneNeeded ? '' : <span className="font-normal text-muted">(ou e-mail)</span>}</span>
                  <input autoComplete="tel" type="tel" required={phoneNeeded} placeholder="+229 01 97 00 00 00" value={customer.phone} onChange={event => setCustomer(c => ({...c, phone: event.target.value}))} className={inputClass} />
                </label>
                <label className="block text-sm"><span className="mb-1 block font-medium">E-mail <span className="font-normal text-muted">(facultatif avec un téléphone)</span></span>
                  <input autoComplete="email" type="email" value={customer.email} onChange={event => setCustomer(c => ({...c, email: event.target.value}))} className={inputClass} />
                </label>
                <label className="flex items-start gap-3 text-sm text-muted">
                  <input type="checkbox" className="mt-0.5 size-4 accent-[var(--primary)]" checked={customer.consent} onChange={event => setCustomer(c => ({...c, consent: event.target.checked}))} />
                  J’accepte de recevoir les offres de {front.restaurant.name} par SMS, WhatsApp ou e-mail.
                </label>
              </>
            )}
          </fieldset>

          {quote && (
            <fieldset>
              <legend className="mb-2 font-serif text-xl font-bold">Paiement</legend>
              <div className="space-y-2">
                {location.payment.payAtCounter && (
                  <label className={choiceClass(choice.payment === 'Counter', cx('flex items-center gap-3 px-4 py-3', !quote.canPayAtCounter && 'opacity-50'))}>
                    <input type="radio" name="payment" className="size-4 accent-[var(--primary)]" disabled={!quote.canPayAtCounter} checked={choice.payment === 'Counter'}
                      onChange={() => setChoice({payment: 'Counter', provider: null, method: null})} />
                    <span>{delivering ? 'À la livraison' : mode === 'DineIn' ? 'Sur place' : 'Au comptoir, au retrait'}{!quote.canPayAtCounter && location.payment.counterLimit !== null ? ` (jusqu’à ${money(location.payment.counterLimit, currency)})` : ''}</span>
                  </label>
                )}
                {quote.onlinePaymentOptions.flatMap(option => option.methods.map(method => {
                  const on = choice.payment === 'Online' && choice.provider === option.provider && choice.method === method;
                  return (
                    <label key={`${option.provider}-${method}`} className={choiceClass(on, 'flex items-center gap-3 px-4 py-3')}>
                      <input type="radio" name="payment" className="size-4 accent-[var(--primary)]" checked={on} onChange={() => setChoice({payment: 'Online', provider: option.provider, method})} />
                      <span>En ligne · {methodLabel[method] ?? method} <span className="text-muted">({option.displayName})</span></span>
                    </label>
                  );
                }))}
                {!location.payment.payAtCounter && quote.onlinePaymentOptions.length === 0 && <Notice tone="warning">Aucun moyen de paiement n’est disponible pour ce montant.</Notice>}
              </div>
            </fieldset>
          )}
        </form>

        <aside aria-label="Récapitulatif" className="rounded-[22px] border border-line bg-white p-5 shadow-[0_6px_18px_rgba(90,44,16,.06)] lg:sticky lg:top-[calc(var(--top,0px)+16px)]">
          <h2 className="font-serif text-xl font-bold">Récapitulatif</h2>
          <ul className="mt-2 divide-y divide-line">
            {cart.map((line, index) => {
              const quoted = quote?.lines[index];
              const local = lineDetails(line, catalog);
              return (
                <li key={line.key} className="flex items-start gap-3 py-3 text-sm">
                  <div className="min-w-0 flex-1">
                    <p className="font-semibold">{quoted?.name ?? local.name}{quoted?.variantName ? ` · ${quoted.variantName}` : ''}</p>
                    {quoted ? (
                      <>
                        {quoted.components?.map((component, position) => <p key={position} className="text-muted">{component.stepName} : {componentLabel(component)}</p>)}
                        {quoted.modifiers.length > 0 && <p className="text-muted">{quoted.modifiers.map(modifier => modifier.name).join(', ')}</p>}
                      </>
                    ) : local.details && <p className="text-muted">{local.details}</p>}
                    {line.note.trim() && <p className="italic text-muted">« {line.note.trim()} »</p>}
                    <div className="mt-2 flex items-center gap-2">
                      <button type="button" aria-label={line.quantity === 1 ? `Retirer : ${local.name}` : `Une unité de moins : ${local.name}`} onClick={() => setQuantity(line.key, line.quantity - 1)} className="grid size-8 place-items-center rounded-lg border border-line">
                        {line.quantity === 1 ? <Trash2 className="size-4" /> : <Minus className="size-4" />}
                      </button>
                      <span className="w-6 text-center font-semibold">{line.quantity}</span>
                      <button type="button" aria-label={`Une unité de plus : ${local.name}`} disabled={line.quantity >= 99} onClick={() => setQuantity(line.key, line.quantity + 1)} className="grid size-8 place-items-center rounded-lg border border-line"><Plus className="size-4" /></button>
                    </div>
                  </div>
                  <span className="font-semibold">{quoted ? money(quoted.lineTotal, currency) : '…'}</span>
                </li>
              );
            })}
          </ul>

          <div className="mt-2 border-t border-line pt-3">
            {couponCode && quote?.coupon ? (
              quote.coupon.isValid ? (
                <p className="flex items-center justify-between gap-3 rounded-xl bg-emerald-50 px-3 py-2 text-sm text-emerald-900">
                  <span className="flex items-center gap-2"><Check className="size-4" aria-hidden="true" />{quote.coupon.code}{quote.coupon.label ? ` · ${quote.coupon.label}` : ''}</span>
                  <button type="button" aria-label="Retirer le code promo" onClick={() => setCouponCode(null)} className="grid size-7 place-items-center rounded-full hover:bg-emerald-100"><X className="size-4" /></button>
                </p>
              ) : (
                <div className="flex items-center justify-between gap-3 rounded-xl bg-red-50 px-3 py-2 text-sm text-red-800" role="alert">
                  <span>{quote.coupon.code} : {couponRefusal(quote.coupon.refusalCode)}</span>
                  <button type="button" aria-label="Retirer le code promo" onClick={() => setCouponCode(null)} className="grid size-7 place-items-center rounded-full hover:bg-red-100"><X className="size-4" /></button>
                </div>
              )
            ) : (
              <div className="flex gap-2">
                <label className="relative flex-1">
                  <span className="sr-only">Code promo</span>
                  <Tag className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-muted" aria-hidden="true" />
                  <input value={couponInput} maxLength={32} placeholder="Code promo" autoCapitalize="characters" onChange={event => setCouponInput(event.target.value)}
                    onKeyDown={event => {
                      if (event.key === 'Enter' && couponInput.trim()) {
                        event.preventDefault();
                        setCouponCode(couponInput.trim().toUpperCase());
                      }
                    }}
                    className="w-full rounded-xl border border-line py-2.5 pl-9 pr-3 text-sm uppercase placeholder:normal-case focus:border-primary focus:outline-none" />
                </label>
                <button type="button" disabled={!couponInput.trim()} onClick={() => setCouponCode(couponInput.trim().toUpperCase())} className="rounded-xl border border-line px-4 text-sm font-semibold disabled:opacity-50">Appliquer</button>
              </div>
            )}
          </div>

          {quoteError && <div className="mt-3"><Notice tone="error">{quoteError}</Notice></div>}
          {!quote && !quoteError && <p className="mt-3 text-sm text-muted">Calcul du total…</p>}
          {quote && (
            <dl className="mt-3 space-y-1 border-t border-line pt-3 text-sm">
              {(quote.discountTotal > 0 || quote.deliveryFee !== null) && <div className="flex justify-between text-muted"><dt>Sous-total</dt><dd>{money(quote.subTotal, currency)}</dd></div>}
              {quote.discountTotal > 0 && (
                <div className="flex justify-between text-leaf">
                  <dt>Remise{[quote.coupon?.isValid ? quote.coupon.code : null, quote.reward?.name].filter(Boolean).length ? ` (${[quote.coupon?.isValid ? quote.coupon.code : null, quote.reward?.name].filter(Boolean).join(', ')})` : ''}</dt>
                  <dd>−{money(quote.discountTotal, currency)}</dd>
                </div>
              )}
              {quote.deliveryFee !== null && <div className="flex justify-between text-muted"><dt>Livraison{zone ? ` · ${zone.name}` : ''}</dt><dd>{quote.deliveryFee > 0 ? money(quote.deliveryFee, currency) : 'Offerte'}</dd></div>}
              <div className="flex justify-between pt-1 text-lg font-bold"><dt>{quote.pricesIncludeTax ? 'Total TTC' : 'Total'}</dt><dd>{money(quote.total, currency)}</dd></div>
              {quote.taxTotal > 0 && <div className="flex justify-between text-muted"><dt>Dont taxes</dt><dd>{money(quote.taxTotal, currency)}</dd></div>}
              {!quote.meetsMinimum && quote.minimumOrderAmount !== null && (
                <div className="pt-2"><Notice tone="warning">Minimum de commande{delivering && zone ? ` pour ${zone.name}` : ''} : {money(quote.minimumOrderAmount, currency)} (hors livraison).</Notice></div>
              )}
            </dl>
          )}

          {error && <div className="mt-3"><Notice tone="error">{error}</Notice></div>}
          <Button type="submit" form="commande" className="mt-4 w-full" busy={busy} disabled={!canSubmit}>
            {choice.payment === 'Online' ? 'Commander et payer' : 'Commander'}{quote ? ` · ${money(quote.total, currency)}` : ''}
          </Button>
          {!canSubmit && quote && !busy && (
            <p className="mt-2 text-center text-xs text-muted">
              {!contactOk ? 'Indiquez votre nom et un téléphone ou un e-mail.' : !deliveryOk ? 'Indiquez l’adresse et le téléphone pour la livraison.' : moment === 'later' && !slot ? 'Choisissez un créneau.' : ''}
            </p>
          )}
        </aside>
      </div>
    </PageShell>
  );
}
