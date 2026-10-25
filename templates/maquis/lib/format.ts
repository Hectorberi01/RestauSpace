import type {DeliveryStatus, GuestOrderStatus, HoursRange, ServiceMode, StorefrontLocation, Weekday} from './storefront';

export const money = (value: number, currency: string) => {
  try {
    return new Intl.NumberFormat('fr-FR', {style: 'currency', currency}).format(value);
  } catch {
    return `${value} ${currency}`;
  }
};

/** Le montant et la devise séparés, pour styler « 4 500 » et « F CFA ». */
export function priceParts(value: number, currency: string) {
  try {
    const parts = new Intl.NumberFormat('fr-FR', {style: 'currency', currency}).formatToParts(value);
    const unit = parts.filter(part => part.type === 'currency').map(part => part.value).join('');
    const amount = parts.filter(part => part.type !== 'currency').map(part => part.value).join('').trim();
    return {amount, unit};
  } catch {
    return {amount: String(value), unit: currency};
  }
}

/** Heure « 12:30 » dans le fuseau donné. */
export const clock = (utc: string, timeZone?: string) => {
  try {
    return new Intl.DateTimeFormat('fr-FR', {hour: '2-digit', minute: '2-digit', timeZone}).format(new Date(utc));
  } catch {
    return new Intl.DateTimeFormat('fr-FR', {hour: '2-digit', minute: '2-digit'}).format(new Date(utc));
  }
};

export const localDay = (utc: string, timeZone: string) =>
  upperFirst(new Intl.DateTimeFormat('fr-FR', {weekday: 'long', day: 'numeric', month: 'long', timeZone}).format(new Date(utc)));

export const upperFirst = (text: string) => text.charAt(0).toUpperCase() + text.slice(1);

/** « 23:00 » → « 23 h », « 23:30 » → « 23 h 30 ». */
export const hourLabel = (time: string) => {
  const [h, m] = time.split(':');
  return `${Number(h)} h${m && m !== '00' ? ` ${m}` : ''}`;
};

export const modeLabel: Record<ServiceMode, string> = {DineIn: 'Sur place', Takeaway: 'À emporter', Delivery: 'Livraison'};

export const statusLabel: Record<GuestOrderStatus, string> = {
  Received: 'Reçue',
  Accepted: 'Acceptée',
  Preparing: 'En préparation',
  Ready: 'Prête',
  Completed: 'Terminée',
  Cancelled: 'Annulée',
};

export const deliveryStatusLabel: Record<DeliveryStatus, string> = {
  Pending: 'En préparation',
  Assigned: 'Livreur désigné',
  OutForDelivery: 'En route',
  Completed: 'Livrée',
  Failed: 'Livraison manquée : le restaurant va vous appeler',
  Cancelled: 'Livraison annulée',
};

export const methodLabel: Record<string, string> = {
  Card: 'Carte bancaire',
  MobileMoney: 'Mobile Money',
  ApplePay: 'Apple Pay',
  GooglePay: 'Google Pay',
  PayPal: 'PayPal',
  BankTransfer: 'Virement',
};

export const allergenLabel: Record<string, string> = {
  gluten: 'Gluten',
  crustaceans: 'Crustacés',
  eggs: 'Œufs',
  fish: 'Poissons',
  peanuts: 'Arachides',
  soybeans: 'Soja',
  milk: 'Lait',
  nuts: 'Fruits à coque',
  celery: 'Céleri',
  mustard: 'Moutarde',
  sesame: 'Sésame',
  sulphites: 'Sulfites',
  lupin: 'Lupin',
  molluscs: 'Mollusques',
};

const days: {day: Weekday; short: string}[] = [
  {day: 'Monday', short: 'Lun'},
  {day: 'Tuesday', short: 'Mar'},
  {day: 'Wednesday', short: 'Mer'},
  {day: 'Thursday', short: 'Jeu'},
  {day: 'Friday', short: 'Ven'},
  {day: 'Saturday', short: 'Sam'},
  {day: 'Sunday', short: 'Dim'},
];

const rangeText = (ranges: HoursRange[]) =>
  ranges.length === 0 ? 'Fermé' : ranges.map(range => `${hourLabel(range.start)} – ${hourLabel(range.end)}`).join(', ');

/** Horaires de la semaine, jours consécutifs identiques regroupés : « Lun – Jeu · 11 h – 22 h ». */
export function weeklyRows(location: StorefrontLocation) {
  const hours = location.weeklyHours ?? [];
  const perDay = days.map(item => ({...item, text: rangeText(hours.filter(range => range.day === item.day))}));
  const rows: {label: string; text: string}[] = [];
  let start = 0;
  for (let index = 1; index <= perDay.length; index++) {
    if (index < perDay.length && perDay[index].text === perDay[start].text) continue;
    const first = perDay[start];
    const last = perDay[index - 1];
    rows.push({label: first === last ? first.short : `${first.short} – ${last.short}`, text: first.text});
    start = index;
  }
  return rows;
}

/** Date locale « AAAA-MM-JJ », jour et heure « HH:MM » dans le fuseau de l'établissement. */
function localParts(date: Date, timeZone: string) {
  const parts = new Intl.DateTimeFormat('en-GB', {timeZone, weekday: 'long', year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', hourCycle: 'h23'})
    .formatToParts(date);
  const get = (type: string) => parts.find(part => part.type === type)?.value ?? '';
  return {day: get('weekday') as Weekday, date: `${get('year')}-${get('month')}-${get('day')}`, time: `${get('hour')}:${get('minute')}`};
}

/** L'heure de fermeture de la plage en cours (« 23 h »), ou null. */
export function closingTime(location: StorefrontLocation, now = new Date()): string | null {
  try {
    const today = localParts(now, location.timeZoneId);
    const yesterday = localParts(new Date(now.getTime() - 86_400_000), location.timeZoneId);
    const hours = location.weeklyHours ?? [];
    const special = location.specialDays?.find(item => item.date === today.date);
    const ranges = special ? special.ranges : hours.filter(range => range.day === today.day);
    const current = ranges.find(range => range.start <= today.time && (range.endsNextDay || today.time < range.end))
      ?? hours.filter(range => range.day === yesterday.day && range.endsNextDay).find(range => today.time < range.end);
    return current ? hourLabel(current.end) : null;
  } catch {
    return null;
  }
}

/** « Ouvert · jusqu'à 23 h », « Fermé · ouvre à 11:30 »… */
export function openingText(location: StorefrontLocation) {
  if (location.closureReason) return {open: false, text: `Fermé : ${location.closureReason}`};
  if (location.isOpenNow) {
    const until = closingTime(location);
    return {open: true, text: `Ouvert maintenant${until ? ` · jusqu’à ${until}` : ''}`};
  }
  if (location.nextOpeningUtc) {
    const sameDay = localParts(new Date(location.nextOpeningUtc), location.timeZoneId).date === localParts(new Date(), location.timeZoneId).date;
    return {open: false, text: `Fermé · ouvre ${sameDay ? 'à' : localDay(location.nextOpeningUtc, location.timeZoneId).toLowerCase() + ' à'} ${clock(location.nextOpeningUtc, location.timeZoneId)}`};
  }
  return {open: false, text: 'Fermé pour le moment'};
}

/** Jours exceptionnels des 7 prochains jours. */
export function upcomingSpecialDays(location: StorefrontLocation) {
  const today = localParts(new Date(), location.timeZoneId).date;
  const limit = localParts(new Date(Date.now() + 7 * 86_400_000), location.timeZoneId).date;
  return (location.specialDays ?? []).filter(item => item.date >= today && item.date <= limit);
}

export const specialDayText = (item: {date: string; isClosed: boolean; ranges: HoursRange[]; reason: string | null}) => {
  const label = upperFirst(new Intl.DateTimeFormat('fr-FR', {weekday: 'long', day: 'numeric', month: 'long', timeZone: 'UTC'}).format(new Date(`${item.date}T12:00:00Z`)));
  return `${label} : ${item.isClosed ? 'fermé' : rangeText(item.ranges)}${item.reason ? ` (${item.reason})` : ''}`;
};

export const directionsUrl = (location: StorefrontLocation) => {
  const coordinates = location.place?.coordinates;
  const destination = coordinates ? `${coordinates.latitude},${coordinates.longitude}` : `${location.street}, ${location.city}`;
  return `https://www.google.com/maps/dir/?api=1&destination=${encodeURIComponent(destination)}`;
};

export const whatsAppUrl = (number: string) => `https://wa.me/${number.replace(/[^\d]/g, '')}`;
