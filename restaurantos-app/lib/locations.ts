import {api, del, post, put} from './api';

export type Day = 'Monday' | 'Tuesday' | 'Wednesday' | 'Thursday' | 'Friday' | 'Saturday' | 'Sunday';

export interface TimeRange {
  start: string;
  end: string;
  endsNextDay: boolean;
}

export interface WeeklySlot extends TimeRange {
  day: Day;
}

export interface ExceptionalSchedule {
  date: string;
  isClosed: boolean;
  ranges: TimeRange[];
  reason: string | null;
}

export interface LocationContact {
  phone: string | null;
  whatsApp: string | null;
  email: string | null;
}

export interface LocationDetail {
  id: string;
  name: string;
  status: 'Draft' | 'Active' | 'TemporarilyClosed' | 'Archived';
  isOperational: boolean;
  address: {street: string; city: string; postalCode: string | null; countryCode: string; latitude: number | null; longitude: number | null};
  contact: LocationContact;
  seats: number;
  timeZoneId: string;
  weeklyHours: WeeklySlot[];
  exceptionalSchedules: ExceptionalSchedule[];
  temporaryClosureReason: string | null;
  createdAtUtc: string;
  archivedAtUtc: string | null;
}

export const days: {day: Day; label: string; short: string}[] = [
  {day: 'Monday', label: 'Lundi', short: 'Lun'},
  {day: 'Tuesday', label: 'Mardi', short: 'Mar'},
  {day: 'Wednesday', label: 'Mercredi', short: 'Mer'},
  {day: 'Thursday', label: 'Jeudi', short: 'Jeu'},
  {day: 'Friday', label: 'Vendredi', short: 'Ven'},
  {day: 'Saturday', label: 'Samedi', short: 'Sam'},
  {day: 'Sunday', label: 'Dimanche', short: 'Dim'},
];

export const countries = [
  {code: 'BJ', label: 'Bénin', timeZone: 'Africa/Porto-Novo'},
  {code: 'CI', label: 'Côte d’Ivoire', timeZone: 'Africa/Abidjan'},
  {code: 'SN', label: 'Sénégal', timeZone: 'Africa/Dakar'},
  {code: 'TG', label: 'Togo', timeZone: 'Africa/Lome'},
  {code: 'BF', label: 'Burkina Faso', timeZone: 'Africa/Ouagadougou'},
  {code: 'ML', label: 'Mali', timeZone: 'Africa/Bamako'},
  {code: 'NE', label: 'Niger', timeZone: 'Africa/Niamey'},
  {code: 'NG', label: 'Nigeria', timeZone: 'Africa/Lagos'},
  {code: 'CM', label: 'Cameroun', timeZone: 'Africa/Douala'},
  {code: 'MA', label: 'Maroc', timeZone: 'Africa/Casablanca'},
  {code: 'FR', label: 'France', timeZone: 'Europe/Paris'},
  {code: 'BE', label: 'Belgique', timeZone: 'Europe/Brussels'},
  {code: 'CH', label: 'Suisse', timeZone: 'Europe/Zurich'},
  {code: 'CA', label: 'Canada', timeZone: 'America/Montreal'},
];

/** Une plage qui finit à l'heure de début ou avant se termine le lendemain (service de nuit). */
export const range = (start: string, end: string): TimeRange => ({start, end, endsNextDay: end <= start});

export function formatRange(item: TimeRange): string {
  return `${item.start} – ${item.end}${item.endsNextDay ? ' (lendemain)' : ''}`;
}

/** Plages qui se chevauchent dans une même journée (minutes depuis minuit, une plage de nuit va au-delà de 24 h). */
export function overlaps(ranges: TimeRange[]): boolean {
  const minutes = (value: string) => Number(value.slice(0, 2)) * 60 + Number(value.slice(3, 5));
  const spans = ranges
    .map(item => [minutes(item.start), minutes(item.end) + (item.endsNextDay ? 1440 : 0)] as const)
    .sort((a, b) => a[0] - b[0]);
  return spans.some((span, index) => index > 0 && span[0] < spans[index - 1][1]);
}

export const loadLocation = (id: string, signal?: AbortSignal) => api<LocationDetail>(`/locations/${id}`, {signal});

export const renameLocation = (id: string, name: string) => put(`/locations/${id}/name`, {name});

export const changeAddress = (id: string, address: {street: string; city: string; postalCode: string | null; countryCode: string; latitude: number | null; longitude: number | null}) =>
  put(`/locations/${id}/address`, address);

export const setContact = (id: string, contact: LocationContact) => put(`/locations/${id}/contact`, contact);

export const setCapacity = (id: string, seats: number) => put(`/locations/${id}/capacity`, {seats});

export const setTimeZone = (id: string, timeZoneId: string) => put(`/locations/${id}/time-zone`, {timeZoneId});

export const setBusinessHours = (id: string, weeklyHours: WeeklySlot[]) => put(`/locations/${id}/business-hours`, {weeklyHours});

export const addClosure = (id: string, date: string, reason: string | null) => post(`/locations/${id}/exceptional-closures`, {date, reason});

export const addOpening = (id: string, date: string, ranges: TimeRange[], reason: string | null) =>
  post(`/locations/${id}/exceptional-openings`, {date, ranges, reason});

export const removeException = (id: string, date: string) => del(`/locations/${id}/exceptions/${date}`);

export const lifecycle = (id: string, action: 'activate' | 'temporarily-close' | 'reopen' | 'archive', body?: unknown) =>
  post(`/locations/${id}/${action}`, body);
