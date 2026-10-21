'use client';
import {Download} from 'lucide-react';
import {downloadCsv} from '@/lib/csv';
import {isoDate, time} from '@/lib/format';
import {useResource} from '@/lib/hooks';
import {dayLabel, getVariance, hm, hours, varianceLabel, varianceTone} from '@/lib/schedule';
import type {LocationSummary} from '@/lib/types';
import {Alert, Badge, Button, Empty, Spinner, cx} from '@/components/ui';
import {WeekNav} from './week';

/** Prévu (planning publié) contre pointé : retards, départs anticipés, absents et heures faites hors planning. */
export function Variance({location, weekStart, onWeekChange}: {location: LocationSummary; weekStart: string; onWeekChange: (weekStart: string) => void}) {
  const data = useResource(signal => getVariance(location.id, weekStart, signal), [location.id, weekStart], 60_000);
  const variance = data.data;

  const exportCsv = () => {
    if (!variance) return;
    downloadCsv(`prevu-pointe-${weekStart}.csv`, [
      ['Membre', 'Jour', 'Prévu', 'Heures prévues', 'Arrivée', 'Départ', 'Heures pointées', 'Retard (min)', 'Départ anticipé (min)', 'Statut'],
      ...variance.shifts.map(shift => [
        shift.name, shift.date, `${hm(shift.start)}-${hm(shift.end)}`, (shift.plannedMinutes / 60).toFixed(2),
        shift.clockedInAtUtc ? time(shift.clockedInAtUtc) : '', shift.clockedOutAtUtc ? time(shift.clockedOutAtUtc) : '',
        (shift.workedMinutes / 60).toFixed(2), String(shift.lateMinutes), String(shift.leftEarlyMinutes), varianceLabel[shift.status],
      ]),
    ]);
  };

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <WeekNav weekStart={weekStart} onChange={onWeekChange} />
        <Button variant="secondary" disabled={!variance?.shifts.length} onClick={exportCsv}><Download className="size-4" aria-hidden="true" />Exporter</Button>
      </div>
      {data.error ? <Alert>{data.error}</Alert> : !variance ? <Spinner /> : !variance.isPublished ? (
        <Empty title="Planning non publié">L’écart se calcule sur la version publiée de la semaine.</Empty>
      ) : (
        <>
          <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
            {variance.members.map(member => {
              const gap = member.workedMinutes - member.dueMinutes;
              return (
                <div key={member.staffMemberId} className="rounded-xl border border-slate-200 p-3">
                  <p className="truncate font-semibold">{member.name}</p>
                  <p className="text-sm text-ink-700">{hours(member.workedMinutes)} pointées / {hours(member.dueMinutes)} prévues à ce jour</p>
                  <p className="text-xs text-ink-500">
                    {hours(member.plannedMinutes)} sur la semaine
                    {Math.abs(gap) >= 15 && <span className={cx('ml-1 font-semibold', gap > 0 ? 'text-amber-700' : 'text-red-700')}>· écart {gap > 0 ? '+' : ''}{hours(gap)}</span>}
                  </p>
                </div>
              );
            })}
          </div>

          <div className="overflow-x-auto rounded-xl border border-slate-200">
            <table className="w-full min-w-[44rem] text-sm">
              <thead className="bg-slate-50 text-left text-xs font-semibold text-ink-500">
                <tr>
                  <th className="px-3 py-2.5">Membre</th>
                  <th className="px-3 py-2.5">Créneau</th>
                  <th className="px-3 py-2.5">Pointé</th>
                  <th className="px-3 py-2.5 text-right">Heures</th>
                  <th className="px-3 py-2.5">Écart</th>
                  <th className="px-3 py-2.5">Statut</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {variance.shifts.map(shift => (
                  <tr key={shift.shiftId}>
                    <td className="px-3 py-2.5 font-semibold whitespace-nowrap">{shift.name}</td>
                    <td className="px-3 py-2.5 whitespace-nowrap first-letter:uppercase">{dayLabel(shift.date)} · {hm(shift.start)} – {hm(shift.end)}</td>
                    <td className="px-3 py-2.5 whitespace-nowrap tabular-nums">
                      {shift.clockedInAtUtc ? `${time(shift.clockedInAtUtc)} – ${shift.clockedOutAtUtc ? time(shift.clockedOutAtUtc) : '…'}` : '—'}
                    </td>
                    <td className="px-3 py-2.5 text-right tabular-nums">{shift.clockedInAtUtc ? hours(shift.workedMinutes) : '—'} / {hours(shift.plannedMinutes)}</td>
                    <td className="px-3 py-2.5 text-xs text-ink-600">
                      {[shift.lateMinutes ? `retard ${shift.lateMinutes} min` : null, shift.leftEarlyMinutes ? `parti ${shift.leftEarlyMinutes} min avant` : null].filter(Boolean).join(' · ') || '—'}
                    </td>
                    <td className="px-3 py-2.5"><Badge tone={varianceTone[shift.status]}>{varianceLabel[shift.status]}</Badge></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          {variance.unplanned.length > 0 && (
            <section>
              <h3 className="mb-2 text-sm font-semibold">Pointages hors planning</h3>
              <ul className="divide-y divide-slate-100 rounded-xl border border-slate-200 text-sm">
                {variance.unplanned.map(entry => (
                  <li key={`${entry.staffMemberId}-${entry.clockedInAtUtc}`} className="flex flex-wrap justify-between gap-2 px-3 py-2">
                    <span><span className="font-semibold">{entry.name}</span> · <span className="inline-block first-letter:uppercase">{dayLabel(isoDate(new Date(entry.clockedInAtUtc)))}</span> · {time(entry.clockedInAtUtc)} – {entry.clockedOutAtUtc ? time(entry.clockedOutAtUtc) : '…'}</span>
                    <span className="tabular-nums">{hours(entry.workedMinutes)}</span>
                  </li>
                ))}
              </ul>
            </section>
          )}
        </>
      )}
    </div>
  );
}
