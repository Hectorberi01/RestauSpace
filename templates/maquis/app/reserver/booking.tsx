'use client';
import {useEffect, useMemo, useState} from 'react';
import {useRouter} from 'next/navigation';
import {CalendarDays, Minus, Plus, Users} from 'lucide-react';
import {clock, money} from '@/lib/format';
import {rememberBooking} from '@/lib/local';
import {bookTable, depositFor, errorMessage, loadBookingTimes, StorefrontError, type BookingTimes} from '@/lib/storefront';
import {useSite} from '@/components/site-provider';
import {Button, Notice, PageShell, Spinner, cx, inputClass} from '@/components/ui';

const DAYS = 14;

/** Les 14 prochains jours dans le fuseau de l'établissement (AAAA-MM-JJ) et leur libellé. */
function calendarOf(timeZone: string) {
  const iso = (date: Date) => new Intl.DateTimeFormat('en-CA', {timeZone, year: 'numeric', month: '2-digit', day: '2-digit'}).format(date);
  return Array.from({length: DAYS}, (_, index) => {
    const date = new Date(Date.now() + index * 86_400_000);
    const label = index === 0 ? 'Aujourd’hui' : index === 1 ? 'Demain'
      : new Intl.DateTimeFormat('fr-FR', {timeZone, weekday: 'short', day: 'numeric', month: 'short'}).format(date).replace(/^./, first => first.toUpperCase());
    return {value: iso(date), label};
  });
}

/** Réservation d'une table : établissement, couverts, jour, heure, puis coordonnées. */
export function Booking() {
  const router = useRouter();
  const {front, location, error, chooseLocation} = useSite();
  const [party, setParty] = useState(2);
  const [day, setDay] = useState('');
  const [times, setTimes] = useState<BookingTimes | null>(null);
  const [timesError, setTimesError] = useState<string | null>(null);
  const [time, setTime] = useState<string | null>(null);
  const [form, setForm] = useState({name: '', phone: '+229 ', email: '', notes: ''});
  const [busy, setBusy] = useState(false);
  const [submitError, setSubmitError] = useState<string | null>(null);

  const calendar = useMemo(() => (location ? calendarOf(location.timeZoneId) : []), [location]);
  const selectedDay = calendar.some(item => item.value === day) ? day : calendar[0]?.value ?? '';

  useEffect(() => {
    if (!location || !selectedDay) return;
    const controller = new AbortController();
    setTimes(null);
    setTimesError(null);
    setTime(null);
    loadBookingTimes(location.id, selectedDay, party, controller.signal)
      .then(result => result && setTimes(result))
      .catch(reason => !controller.signal.aborted && setTimesError(reason instanceof StorefrontError && reason.code === 'storefront.reservation.disabled'
        ? 'Cet établissement ne prend pas de réservations en ligne. Appelez-le pour réserver.'
        : errorMessage(reason)));
    return () => controller.abort();
  }, [location, selectedDay, party]);

  const submit = async () => {
    if (!time || !location) return;
    setBusy(true);
    setSubmitError(null);
    try {
      const booked = await bookTable({
        locationId: location.id, startAt: time, partySize: party, name: form.name.trim(), phone: form.phone.replace(/[\s.\-()]/g, ''),
        email: form.email.trim() || null, notes: form.notes.trim() || null,
      });
      if (booked) {
        rememberBooking(booked.token);
        router.push(`/reservation/?jeton=${encodeURIComponent(booked.token)}&nouvelle=1`);
      }
    } catch (reason) {
      setSubmitError(errorMessage(reason));
    } finally {
      setBusy(false);
    }
  };

  if (error) return <PageShell title="Réserver une table"><Notice tone="error">{error}</Notice></PageShell>;
  if (!front || !location) return <PageShell title="Réserver une table"><Spinner /></PageShell>;
  const zone = location.timeZoneId;
  const available = times?.times.filter(item => item.isAvailable) ?? [];
  const max = times?.maxPartySize ?? 20;
  const deposit = depositFor(times?.deposit, party);

  return (
    <PageShell title="Réserver une table" eyebrow={front.restaurant.name}>
      <div className="space-y-6">
        {front.locations.length > 1 && (
          <label className="block">
            <span className="mb-1.5 block text-sm font-semibold">Établissement</span>
            <select value={location.id} onChange={event => chooseLocation(event.target.value)} className={inputClass}>
              {front.locations.map(item => <option key={item.id} value={item.id}>{item.name} · {item.city}</option>)}
            </select>
          </label>
        )}

        <div className="flex items-center justify-between rounded-2xl border border-line bg-white p-4">
          <span className="flex items-center gap-2 font-semibold"><Users className="size-5 text-primary" aria-hidden="true" />Nombre de personnes</span>
          <span className="flex items-center gap-3">
            <button type="button" aria-label="Une personne de moins" disabled={party <= 1} onClick={() => setParty(party - 1)} className="grid size-11 place-items-center rounded-full border border-line disabled:opacity-40"><Minus className="size-4" /></button>
            <span className="w-6 text-center text-lg font-bold tabular-nums" aria-live="polite">{party}</span>
            <button type="button" aria-label="Une personne de plus" disabled={party >= max} onClick={() => setParty(party + 1)} className="grid size-11 place-items-center rounded-full border border-line disabled:opacity-40"><Plus className="size-4" /></button>
          </span>
        </div>

        <div>
          <p className="mb-2 flex items-center gap-2 text-sm font-semibold"><CalendarDays className="size-4 text-primary" aria-hidden="true" />Jour</p>
          <div className="no-scrollbar -mx-4 flex gap-2 overflow-x-auto px-4 pb-1" role="radiogroup" aria-label="Jour">
            {calendar.map(item => (
              <button key={item.value} type="button" role="radio" aria-checked={selectedDay === item.value} onClick={() => setDay(item.value)}
                className={cx('shrink-0 rounded-xl border px-3.5 py-2.5 text-sm font-medium', selectedDay === item.value ? 'border-ink bg-ink text-white' : 'border-line bg-white')}>
                {item.label}
              </button>
            ))}
          </div>
        </div>

        <div>
          <p className="mb-2 text-sm font-semibold">Heure</p>
          {timesError ? <Notice tone="warning">{timesError}</Notice> : !times ? <Spinner label="Recherche des tables libres…" /> : available.length === 0 ? (
            <Notice>Aucune table libre ce jour-là pour {party} personne{party > 1 ? 's' : ''}. Essayez un autre jour.</Notice>
          ) : (
            <div className="grid grid-cols-4 gap-2 sm:grid-cols-6" role="radiogroup" aria-label="Heure">
              {times.times.map(item => (
                <button key={item.startAtUtc} type="button" role="radio" aria-checked={time === item.startAtUtc} disabled={!item.isAvailable} onClick={() => setTime(item.startAtUtc)}
                  aria-label={item.isAvailable ? undefined : `${clock(item.startAtUtc, zone)}, complet`}
                  className={cx('rounded-xl border py-2.5 text-sm font-semibold tabular-nums disabled:cursor-not-allowed disabled:border-line/60 disabled:text-muted/50 disabled:line-through',
                    time === item.startAtUtc ? 'border-primary bg-primary text-white' : 'border-line bg-white')}>
                  {clock(item.startAtUtc, zone)}
                </button>
              ))}
            </div>
          )}
        </div>

        {time && (
          <form className="space-y-3 rounded-[22px] border border-line bg-white p-5" onSubmit={event => {
            event.preventDefault();
            void submit();
          }}>
            {deposit !== null && times?.deposit && (
              <Notice>
                Un acompte de <strong>{money(deposit, times.deposit.currencyCode)}</strong> confirme la réservation : à payer en ligne dans les {times.deposit.paymentWindowMinutes} minutes. Il est déduit de l’addition et remboursé si vous annulez à temps.
              </Notice>
            )}
            <p className="font-serif text-xl font-bold">Vos coordonnées</p>
            <label className="block text-sm"><span className="mb-1 block font-medium">Nom</span>
              <input required maxLength={100} autoComplete="name" value={form.name} onChange={event => setForm({...form, name: event.target.value})} className={inputClass} />
            </label>
            <label className="block text-sm"><span className="mb-1 block font-medium">Téléphone</span>
              <input required type="tel" autoComplete="tel" value={form.phone} onChange={event => setForm({...form, phone: event.target.value})} className={inputClass} />
            </label>
            <label className="block text-sm"><span className="mb-1 block font-medium">E-mail <span className="font-normal text-muted">(facultatif)</span></span>
              <input type="email" autoComplete="email" value={form.email} onChange={event => setForm({...form, email: event.target.value})} className={inputClass} />
            </label>
            <label className="block text-sm"><span className="mb-1 block font-medium">Une précision ? <span className="font-normal text-muted">(anniversaire, chaise bébé…)</span></span>
              <textarea maxLength={500} rows={2} value={form.notes} onChange={event => setForm({...form, notes: event.target.value})} className={inputClass} />
            </label>
            {submitError && <Notice tone="error">{submitError}</Notice>}
            <Button type="submit" className="w-full" busy={busy} disabled={!form.name.trim() || form.phone.replace(/\D/g, '').length < 8}>
              Réserver pour {party} à {clock(time, zone)}
            </Button>
            <p className="text-center text-xs text-muted">Table gardée {times?.durationMinutes ?? 90} minutes.</p>
          </form>
        )}
      </div>
    </PageShell>
  );
}
