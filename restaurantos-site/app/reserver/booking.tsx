'use client';
import {useEffect, useMemo, useState} from 'react';
import {useRouter} from 'next/navigation';
import {CalendarDays, Minus, Plus, Users} from 'lucide-react';
import {
  accentOf, bookTable, depositFor, errorMessage, joinWaitlist, loadBookingTimes, localTime, money, rememberBooking, storefront, storefrontKey, StorefrontError,
  type BookingTimes, type Storefront,
} from '@/lib/storefront';
import {AccentButton, cx, Notice, Spinner} from '@/components/order-ui';
import {intlLocale, tr, useLang} from '@/lib/i18n';

const DAYS = 14;

/** Date du jour J+n dans le fuseau de l'établissement (AAAA-MM-JJ) et son libellé. */
function days(timeZone: string): {value: string; label: string}[] {
  const parts = (date: Date) => new Intl.DateTimeFormat('en-CA', {timeZone, year: 'numeric', month: '2-digit', day: '2-digit'}).format(date);
  return Array.from({length: DAYS}, (_, index) => {
    const date = new Date(Date.now() + index * 86_400_000);
    const label = index === 0 ? tr('Aujourd’hui') : index === 1 ? tr('Demain')
      : new Intl.DateTimeFormat(intlLocale(), {timeZone, weekday: 'short', day: 'numeric', month: 'short'}).format(date).replace(/^./, first => first.toUpperCase());
    return {value: parts(date), label};
  });
}

/** Réservation d'une table : établissement, nombre de couverts, jour, heure, puis coordonnées. */
export function Booking() {
  useLang();
  const router = useRouter();
  const [front, setFront] = useState<Storefront | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [locationId, setLocationId] = useState('');
  const [party, setParty] = useState(2);
  const [day, setDay] = useState('');
  const [times, setTimes] = useState<BookingTimes | null>(null);
  const [timesError, setTimesError] = useState<string | null>(null);
  const [time, setTime] = useState<string | null>(null);
  const [form, setForm] = useState({name: '', phone: '+229 ', email: '', notes: ''});
  const [flexibility, setFlexibility] = useState(30);
  const [busy, setBusy] = useState(false);
  const [submitError, setSubmitError] = useState<string | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    storefront<Storefront>('/', {signal: controller.signal})
      .then(result => {
        if (!result) return;
        setFront(result);
        if (result.locations.length > 0) setLocationId(result.locations[0].id);
      })
      .catch(reason => !controller.signal.aborted && setError(errorMessage(reason)));
    return () => controller.abort();
  }, []);

  const location = front?.locations.find(item => item.id === locationId);
  const calendar = useMemo(() => (location ? days(location.timeZoneId) : []), [location]);
  useEffect(() => {
    if (calendar.length && !calendar.some(item => item.value === day)) setDay(calendar[0].value);
  }, [calendar, day]);

  useEffect(() => {
    if (!locationId || !day) return;
    const controller = new AbortController();
    setTimes(null);
    setTimesError(null);
    setTime(null);
    loadBookingTimes(locationId, day, party, controller.signal)
      .then(result => result && setTimes(result))
      .catch(reason => !controller.signal.aborted && setTimesError(reason instanceof StorefrontError && reason.code === 'storefront.reservation.disabled'
        ? tr('Cet établissement ne prend pas de réservations en ligne. Appelez-le pour réserver.')
        : errorMessage(reason)));
    return () => controller.abort();
  }, [locationId, day, party]);

  const waiting = !!time && !!times?.times.some(item => item.startAtUtc === time && !item.isAvailable);

  const submit = async () => {
    if (!time) return;
    setBusy(true);
    setSubmitError(null);
    try {
      if (waiting) {
        const joined = await joinWaitlist({
          locationId, desiredStart: time, partySize: party, flexibilityMinutes: flexibility, name: form.name.trim(),
          phone: form.phone.replace(/[\s.\-()]/g, ''), email: form.email.trim() || null,
        });
        if (joined) router.push(`/reservation/attente?jeton=${encodeURIComponent(joined.token)}&nouvelle=1`);
        return;
      }
      const booked = await bookTable({
        locationId, startAt: time, partySize: party, name: form.name.trim(), phone: form.phone.replace(/[\s.\-()]/g, ''),
        email: form.email.trim() || null, notes: form.notes.trim() || null,
      });
      if (booked) {
        rememberBooking(booked.token);
        router.push(`/reservation?jeton=${encodeURIComponent(booked.token)}&nouvelle=1`);
      }
    } catch (reason) {
      setSubmitError(errorMessage(reason));
    } finally {
      setBusy(false);
    }
  };

  if (!storefrontKey) return <Shell><Notice tone="warning">{tr('La réservation en ligne n’est pas configurée sur ce site.')}</Notice></Shell>;
  if (error) return <Shell><Notice tone="error">{error}</Notice></Shell>;
  if (!front || !location) return <Shell><Spinner /></Shell>;
  const zone = location.timeZoneId;
  const available = times?.times.filter(item => item.isAvailable) ?? [];
  const max = times?.maxPartySize ?? 20;
  const deposit = depositFor(times?.deposit, party);
  const waitlist = !!times?.acceptsWaitlist;

  return (
    <div style={{'--accent': accentOf(front.restaurant.primaryColor)} as React.CSSProperties}>
      <Shell>
        <p className="text-sm font-semibold text-[var(--accent)]">{front.restaurant.name}</p>
        <h1 className="mt-1 text-2xl font-bold tracking-tight">{tr('Réserver une table')}</h1>

        <div className="mt-6 space-y-5">
          {front.locations.length > 1 && (
            <label className="block">
              <span className="mb-1.5 block text-sm font-semibold">{tr('Établissement')}</span>
              <select value={locationId} onChange={event => setLocationId(event.target.value)} className="w-full rounded-xl border border-slate-200 bg-white px-3 py-2.5">
                {front.locations.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}
              </select>
            </label>
          )}

          <div className="flex items-center justify-between rounded-2xl bg-white p-4 shadow-sm">
            <span className="flex items-center gap-2 font-semibold"><Users className="size-5 text-[var(--accent)]" aria-hidden="true" />{tr('Couverts')}</span>
            <span className="flex items-center gap-3">
              <button type="button" aria-label={tr('Un couvert de moins')} disabled={party <= 1} onClick={() => setParty(party - 1)} className="grid size-10 place-items-center rounded-full border border-slate-200 disabled:opacity-40"><Minus className="size-4" /></button>
              <span className="w-6 text-center text-lg font-bold tabular-nums" aria-live="polite">{party}</span>
              <button type="button" aria-label={tr('Un couvert de plus')} disabled={party >= max} onClick={() => setParty(party + 1)} className="grid size-10 place-items-center rounded-full border border-slate-200 disabled:opacity-40"><Plus className="size-4" /></button>
            </span>
          </div>

          <div>
            <p className="mb-2 flex items-center gap-2 text-sm font-semibold"><CalendarDays className="size-4 text-[var(--accent)]" aria-hidden="true" />{tr('Jour')}</p>
            <div className="-mx-4 flex gap-2 overflow-x-auto px-4 pb-1" role="radiogroup" aria-label={tr('Jour')}>
              {calendar.map(item => (
                <button key={item.value} type="button" role="radio" aria-checked={day === item.value} onClick={() => setDay(item.value)}
                  className={cx('shrink-0 rounded-xl border px-3 py-2 text-sm font-medium', day === item.value ? 'border-[var(--accent)] bg-[var(--accent)] text-white' : 'border-slate-200 bg-white')}>
                  {item.label}
                </button>
              ))}
            </div>
          </div>

          <div>
            <p className="mb-2 text-sm font-semibold">{tr('Heure')}</p>
            {timesError ? <Notice tone="warning">{timesError}</Notice> : !times ? <Spinner /> : available.length === 0 && (!waitlist || times.times.length === 0) ? (
              <Notice>{tr('Aucune table libre ce jour-là pour')} {party} {tr('personne')}{party > 1 ? 's' : ''}{tr('. Essayez un autre jour.')}</Notice>
            ) : (
              <>
                {waitlist && available.length < times.times.length && (
                  <p className="mb-2 text-sm text-ink-500">{tr('Heure complète ? Choisissez-la : nous vous prévenons si une table se libère.')}</p>
                )}
                <div className="grid grid-cols-4 gap-2 sm:grid-cols-5" role="radiogroup" aria-label={tr('Heure')}>
                  {times.times.map(item => (
                    <button key={item.startAtUtc} type="button" role="radio" aria-checked={time === item.startAtUtc} disabled={!item.isAvailable && !waitlist}
                      onClick={() => setTime(item.startAtUtc)}
                      aria-label={item.isAvailable ? undefined : `${localTime(item.startAtUtc, zone)}, ${tr('complet')}`}
                      className={cx('rounded-xl border py-2 text-sm font-semibold tabular-nums disabled:cursor-not-allowed disabled:border-slate-100 disabled:text-slate-300 disabled:line-through',
                        time === item.startAtUtc ? 'border-[var(--accent)] bg-[var(--accent)] text-white'
                          : item.isAvailable ? 'border-slate-200 bg-white' : 'border-dashed border-slate-300 bg-slate-50 text-slate-400')}>
                      {localTime(item.startAtUtc, zone)}
                    </button>
                  ))}
                </div>
              </>
            )}
          </div>

          {time && (
            <form className="space-y-3 rounded-2xl bg-white p-4 shadow-sm" onSubmit={event => {
              event.preventDefault();
              void submit();
            }}>
              {waiting ? (
                <div className="space-y-2">
                  <Notice>{tr('Complet à')} {localTime(time, zone)}. {tr('Inscrivez-vous : si une table se libère, nous vous l’envoyons par WhatsApp ou SMS, à confirmer en quelques minutes.')}</Notice>
                  <label className="block">
                    <span className="mb-1 block text-sm font-semibold">{tr('Je peux venir')}</span>
                    <select value={flexibility} onChange={event => setFlexibility(Number(event.target.value))} className="w-full rounded-xl border border-slate-200 bg-white px-3 py-2.5">
                      <option value={0}>{tr('seulement à cette heure')}</option>
                      <option value={30}>{tr('30 minutes avant ou après')}</option>
                      <option value={60}>{tr('1 heure avant ou après')}</option>
                      <option value={120}>{tr('2 heures avant ou après')}</option>
                    </select>
                  </label>
                </div>
              ) : deposit !== null && times?.deposit && (
                <Notice>
                  {tr('Un acompte de')} <strong>{money(deposit, times.deposit.currencyCode)}</strong> {tr('confirme la réservation : à payer en ligne dans les')} {times.deposit.paymentWindowMinutes} {tr('minutes. Il est déduit de l’addition et remboursé si vous annulez à temps.')}
                </Notice>
              )}
              <p className="font-semibold">{tr('Vos coordonnées')}</p>
              <input required maxLength={100} autoComplete="name" value={form.name} onChange={event => setForm({...form, name: event.target.value})} placeholder={tr('Nom')}
                className="w-full rounded-xl border border-slate-200 px-3 py-2.5" />
              <input required type="tel" autoComplete="tel" value={form.phone} onChange={event => setForm({...form, phone: event.target.value})} placeholder={tr('Téléphone')}
                className="w-full rounded-xl border border-slate-200 px-3 py-2.5" />
              <input type="email" autoComplete="email" value={form.email} onChange={event => setForm({...form, email: event.target.value})} placeholder={tr('E-mail (facultatif)')}
                className="w-full rounded-xl border border-slate-200 px-3 py-2.5" />
              <textarea maxLength={500} rows={2} value={form.notes} onChange={event => setForm({...form, notes: event.target.value})} placeholder={tr('Une précision ? (anniversaire, chaise bébé…)')}
                className="w-full rounded-xl border border-slate-200 px-3 py-2.5" />
              {submitError && <Notice tone="error">{submitError}</Notice>}
              <AccentButton type="submit" className="w-full" busy={busy} disabled={!form.name.trim() || form.phone.replace(/\D/g, '').length < 8}>
                {waiting ? tr('Être prévenu si une table se libère') : <>{tr('Réserver pour')} {party} {tr('à')} {localTime(time, zone)}</>}
              </AccentButton>
              {!waiting && <p className="text-center text-xs text-ink-500">{tr('Confirmation par SMS ou WhatsApp. Table gardée')} {times?.durationMinutes ?? 120} {tr('minutes.')}</p>}
            </form>
          )}
        </div>
      </Shell>
    </div>
  );
}

function Shell({children}: {children: React.ReactNode}) {
  return <div className="mx-auto min-h-screen max-w-xl bg-slate-50 px-4 py-10">{children}</div>;
}
