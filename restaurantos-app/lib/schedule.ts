import {api, del, post, put} from './api';
import {isoDate, type Tone} from './format';

// --- Planning de l'équipe : semaines (brouillon puis publication), absences, remplacements, prévu / pointé.

export type ShiftWarning = 'time_off' | 'pending_time_off' | 'not_assigned';

export interface Shift {
  id: string;
  staffMemberId: string;
  name: string;
  date: string;
  /** « HH:mm:ss » en heure locale de l'établissement. */
  start: string;
  end: string;
  endsNextDay: boolean;
  breakMinutes: number;
  position: string | null;
  note: string | null;
  plannedMinutes: number;
  warnings: ShiftWarning[];
}

export interface ScheduleMember {
  staffMemberId: string;
  name: string;
  plannedMinutes: number;
  overLimit: boolean;
  isAssigned: boolean;
}

export type TimeOffKind = 'Leave' | 'Unavailable';
export type TimeOffStatus = 'Pending' | 'Approved' | 'Rejected' | 'Cancelled';

export interface TimeOff {
  id: string;
  staffMemberId: string;
  name: string;
  firstDay: string;
  lastDay: string;
  kind: TimeOffKind;
  comment: string | null;
  status: TimeOffStatus;
  requestedAtUtc: string;
  decidedAtUtc: string | null;
  decisionNote: string | null;
}

export interface ScheduleWeek {
  id: string | null;
  locationId: string;
  weekStart: string;
  publishedVersion: number;
  publishedAtUtc: string | null;
  hasUnpublishedChanges: boolean;
  changedMembers: string[];
  shifts: Shift[];
  members: ScheduleMember[];
  absences: TimeOff[];
  openSwaps: number;
  weeklyHoursLimit: number;
  positions: string[];
}

export interface ShiftInput {
  staffMemberId: string;
  date: string;
  start: string;
  end: string;
  breakMinutes: number;
  position: string | null;
  note: string | null;
}

export interface ScheduleSettings {
  positions: string[];
  weeklyHoursLimit: number;
}

export type SwapStatus = 'AwaitingColleague' | 'AwaitingManager' | 'Approved' | 'Declined' | 'Rejected' | 'Cancelled' | 'Expired';

export interface ShiftSwap {
  id: string;
  locationId: string;
  locationName: string | null;
  weekId: string;
  shiftId: string;
  date: string | null;
  start: string | null;
  end: string | null;
  position: string | null;
  shiftStartsAtUtc: string;
  fromMemberId: string;
  fromName: string;
  toMemberId: string;
  toName: string;
  note: string | null;
  status: SwapStatus;
  requestedAtUtc: string;
  decidedAtUtc: string | null;
}

export type VarianceStatus = 'upcoming' | 'in_progress' | 'on_time' | 'late' | 'missing';

export interface VarianceShift {
  shiftId: string;
  staffMemberId: string;
  name: string;
  date: string;
  start: string;
  end: string;
  position: string | null;
  plannedMinutes: number;
  startsAtUtc: string;
  endsAtUtc: string;
  clockedInAtUtc: string | null;
  clockedOutAtUtc: string | null;
  workedMinutes: number;
  lateMinutes: number;
  leftEarlyMinutes: number;
  status: VarianceStatus;
}

export interface ScheduleVariance {
  weekStart: string;
  isPublished: boolean;
  shifts: VarianceShift[];
  members: {staffMemberId: string; name: string; plannedMinutes: number; dueMinutes: number; workedMinutes: number}[];
  unplanned: {staffMemberId: string; name: string; clockedInAtUtc: string; clockedOutAtUtc: string | null; workedMinutes: number}[];
}

export interface MyShift {
  weekId: string;
  locationId: string;
  locationName: string;
  shift: Shift;
  startsAtUtc: string;
  colleagues: string[];
}

export interface MySchedule {
  staffMemberId: string;
  from: string;
  to: string;
  shifts: MyShift[];
  timeOff: TimeOff[];
  swaps: ShiftSwap[];
  locations: {locationId: string; name: string; colleagues: {staffMemberId: string; name: string}[]}[];
}

// --- Dates : une semaine commence le lundi ; les dates voyagent en « AAAA-MM-JJ ».

export function mondayOf(date: Date): Date {
  const day = new Date(date.getFullYear(), date.getMonth(), date.getDate());
  day.setDate(day.getDate() - ((day.getDay() + 6) % 7));
  return day;
}

export const addDays = (date: Date, days: number) => new Date(date.getFullYear(), date.getMonth(), date.getDate() + days);

export const parseDay = (value: string) => {
  const [y, m, d] = value.split('-').map(Number);
  return new Date(y, m - 1, d);
};

export const weekDays = (weekStart: string) => Array.from({length: 7}, (_, i) => isoDate(addDays(parseDay(weekStart), i)));

const dayFormat = new Intl.DateTimeFormat('fr-FR', {weekday: 'short', day: 'numeric', month: 'short'});
const longDayFormat = new Intl.DateTimeFormat('fr-FR', {weekday: 'long', day: 'numeric', month: 'long'});
const shortFormat = new Intl.DateTimeFormat('fr-FR', {day: 'numeric', month: 'short'});

export const dayLabel = (iso: string) => dayFormat.format(parseDay(iso));
export const longDayLabel = (iso: string) => longDayFormat.format(parseDay(iso));
export const shortDayLabel = (iso: string) => shortFormat.format(parseDay(iso));

export function weekLabel(weekStart: string): string {
  const start = parseDay(weekStart);
  const end = addDays(start, 6);
  const sameMonth = start.getMonth() === end.getMonth();
  const left = sameMonth ? String(start.getDate()) : shortFormat.format(start);
  return `Semaine du ${left} au ${new Intl.DateTimeFormat('fr-FR', {day: 'numeric', month: 'long', year: 'numeric'}).format(end)}`;
}

export const hm = (value: string | null) => (value ? value.slice(0, 5) : '');

export function hours(minutes: number): string {
  const h = Math.floor(Math.abs(minutes) / 60);
  const m = Math.abs(minutes) % 60;
  const sign = minutes < 0 ? '-' : '';
  if (h === 0) return m ? `${sign}${m} min` : '0 h';
  return `${sign}${h} h${m ? ` ${String(m).padStart(2, '0')}` : ''}`;
}

export const shiftTime = (shift: {start: string; end: string; endsNextDay?: boolean}) => `${hm(shift.start)} – ${hm(shift.end)}${shift.endsNextDay ? ' (+1)' : ''}`;

export function period(absence: {firstDay: string; lastDay: string}): string {
  return absence.firstDay === absence.lastDay ? longDayLabel(absence.firstDay) : `du ${shortDayLabel(absence.firstDay)} au ${shortDayLabel(absence.lastDay)}`;
}

export const timeOffKindLabel: Record<TimeOffKind, string> = {Leave: 'Congé', Unavailable: 'Indisponible'};
export const timeOffStatusLabel: Record<TimeOffStatus, string> = {Pending: 'À valider', Approved: 'Validée', Rejected: 'Refusée', Cancelled: 'Annulée'};
export const timeOffStatusTone: Record<TimeOffStatus, Tone> = {Pending: 'amber', Approved: 'green', Rejected: 'red', Cancelled: 'neutral'};

export const swapStatusLabel: Record<SwapStatus, string> = {
  AwaitingColleague: 'Attend le collègue',
  AwaitingManager: 'À valider',
  Approved: 'Validé',
  Declined: 'Refusé par le collègue',
  Rejected: 'Refusé',
  Cancelled: 'Annulé',
  Expired: 'Expiré',
};
export const swapStatusTone: Record<SwapStatus, Tone> = {
  AwaitingColleague: 'blue', AwaitingManager: 'amber', Approved: 'green', Declined: 'neutral', Rejected: 'red', Cancelled: 'neutral', Expired: 'neutral',
};

export const varianceLabel: Record<VarianceStatus, string> = {
  upcoming: 'À venir', in_progress: 'En poste', on_time: 'À l’heure', late: 'En retard', missing: 'Absent',
};
export const varianceTone: Record<VarianceStatus, Tone> = {upcoming: 'neutral', in_progress: 'blue', on_time: 'green', late: 'amber', missing: 'red'};

export const warningLabel: Record<ShiftWarning, string> = {
  time_off: 'Absence validée ce jour-là',
  pending_time_off: 'Absence demandée ce jour-là',
  not_assigned: 'Ne travaille plus ici',
};

// --- Gérant.

const weekPath = (locationId: string, weekStart: string) => `/staff/schedule/weeks/${locationId}/${weekStart}`;

export const getWeek = (locationId: string, weekStart: string, signal?: AbortSignal) => api<ScheduleWeek>(weekPath(locationId, weekStart), {signal});

export const addShift = (locationId: string, weekStart: string, shift: ShiftInput) => post<{id: string}>(`${weekPath(locationId, weekStart)}/shifts`, shift);

export const updateShift = (locationId: string, weekStart: string, shiftId: string, shift: ShiftInput) =>
  put(`${weekPath(locationId, weekStart)}/shifts/${shiftId}`, shift);

export const removeShift = (locationId: string, weekStart: string, shiftId: string) => del(`${weekPath(locationId, weekStart)}/shifts/${shiftId}`);

export const copyPreviousWeek = (locationId: string, weekStart: string) => post<{copied: number}>(`${weekPath(locationId, weekStart)}/copy-previous`);

export const publishWeek = (locationId: string, weekStart: string) => post<{notified: number}>(`${weekPath(locationId, weekStart)}/publish`);

export const getVariance = (locationId: string, weekStart: string, signal?: AbortSignal) =>
  api<ScheduleVariance>(`${weekPath(locationId, weekStart)}/variance`, {signal});

export const getScheduleSettings = (locationId: string, signal?: AbortSignal) => api<ScheduleSettings>(`/staff/schedule/settings/${locationId}`, {signal});

export const setScheduleSettings = (locationId: string, settings: ScheduleSettings) => put(`/staff/schedule/settings/${locationId}`, settings);

export const listTimeOff = (from: string, to: string, status?: TimeOffStatus, signal?: AbortSignal) =>
  api<TimeOff[]>('/staff/schedule/time-off', {query: {from, to, status}, signal});

export const recordTimeOff = (input: {staffMemberId: string; firstDay: string; lastDay: string; kind: TimeOffKind; comment: string | null}) =>
  post<{id: string}>('/staff/schedule/time-off', input);

export const decideTimeOff = (id: string, decision: 'approve' | 'reject' | 'cancel', note?: string) =>
  post(`/staff/schedule/time-off/${id}/${decision}`, decision === 'reject' ? {note: note?.trim() || null} : undefined);

export const listSwaps = (openOnly: boolean, signal?: AbortSignal) => api<ShiftSwap[]>('/staff/schedule/swaps', {query: {openOnly}, signal});

export const decideSwap = (id: string, approve: boolean) => post(`/staff/schedule/swaps/${id}/${approve ? 'approve' : 'reject'}`);

// --- Membre connecté.

export const getMySchedule = (from: string, weeks: number, signal?: AbortSignal) => api<MySchedule>('/staff/me/schedule', {query: {from, weeks}, signal});

export const requestTimeOff = (input: {firstDay: string; lastDay: string; kind: TimeOffKind; comment: string | null}) => post<{id: string}>('/staff/me/time-off', input);

export const cancelMyTimeOff = (id: string) => post(`/staff/me/time-off/${id}/cancel`);

export const requestSwap = (input: {weekId: string; shiftId: string; toStaffMemberId: string; note: string | null}) => post<{id: string}>('/staff/me/swaps', input);

export const answerSwap = (id: string, accept: boolean) => post(`/staff/me/swaps/${id}/${accept ? 'accept' : 'decline'}`);

export const cancelMySwap = (id: string) => post(`/staff/me/swaps/${id}/cancel`);
