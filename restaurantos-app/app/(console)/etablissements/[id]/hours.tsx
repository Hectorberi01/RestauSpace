'use client';
import {useEffect, useMemo, useState} from 'react';
import {Copy, Moon, Plus, X} from 'lucide-react';
import {days, overlaps, range, setBusinessHours, type Day, type LocationDetail, type TimeRange} from '@/lib/locations';
import {Button, cx, useAction} from '@/components/ui';

type Week = Record<Day, TimeRange[]>;

const fromLocation = (location: LocationDetail): Week => {
  const week = Object.fromEntries(days.map(({day}) => [day, [] as TimeRange[]])) as Week;
  for (const slot of location.weeklyHours) week[slot.day].push({start: slot.start, end: slot.end, endsNextDay: slot.endsNextDay});
  for (const {day} of days) week[day].sort((a, b) => a.start.localeCompare(b.start));
  return week;
};

const same = (a: Week, b: Week) => JSON.stringify(a) === JSON.stringify(b);

export function TimeInput({value, label, disabled, onChange}: {value: string; label: string; disabled: boolean; onChange: (value: string) => void}) {
  return (
    <input type="time" aria-label={label} value={value} disabled={disabled} required onChange={event => onChange(event.target.value)}
      className="h-9 w-[7.75rem] rounded-lg border border-slate-200 bg-white px-2 text-sm focus:border-brand-500 focus:ring-2 focus:ring-brand-200 focus:outline-none disabled:bg-slate-50" />
  );
}

export function HoursEditor({location, editable, onSaved}: {location: LocationDetail; editable: boolean; onSaved: () => Promise<void>}) {
  const {busy, run} = useAction();
  const initial = useMemo(() => fromLocation(location), [location]);
  const [week, setWeek] = useState<Week>(initial);
  useEffect(() => setWeek(initial), [initial]);

  const update = (day: Day, ranges: TimeRange[]) => setWeek(current => ({...current, [day]: ranges}));
  const invalidDays = days.filter(({day}) => overlaps(week[day]) || week[day].some(item => item.start === item.end && !item.endsNextDay));
  const dirty = !same(week, initial);

  return (
    <div>
      <ul className="divide-y divide-slate-100">
        {days.map(({day, label}) => {
          const ranges = week[day];
          const open = ranges.length > 0;
          const invalid = invalidDays.some(item => item.day === day);
          return (
            <li key={day} className="flex flex-col gap-2 py-3 sm:flex-row sm:items-start">
              <div className="flex w-40 shrink-0 items-center gap-3 pt-1.5">
                <button type="button" role="switch" aria-checked={open} aria-label={`${label} : ${open ? 'fermer' : 'ouvrir'}`} disabled={!editable}
                  onClick={() => update(day, open ? [] : [range('11:00', '23:00')])}
                  className={cx('relative h-5 w-9 shrink-0 rounded-full transition disabled:opacity-50', open ? 'bg-brand-600' : 'bg-slate-300')}>
                  <span className={cx('absolute top-0.5 size-4 rounded-full bg-white shadow transition-all', open ? 'left-[1.125rem]' : 'left-0.5')} />
                </button>
                <span className={cx('text-sm font-medium', !open && 'text-ink-500')}>{label}</span>
              </div>
              <div className="min-w-0 flex-1">
                {!open ? (
                  <p className="pt-1.5 text-sm text-ink-500">Fermé</p>
                ) : (
                  <div className="space-y-2">
                    {ranges.map((item, index) => (
                      <div key={index} className="flex flex-wrap items-center gap-2">
                        <TimeInput label={`${label}, ouverture ${index + 1}`} value={item.start} disabled={!editable}
                          onChange={value => update(day, ranges.map((current, i) => (i === index ? range(value, current.end) : current)))} />
                        <span className="text-ink-500" aria-hidden="true">–</span>
                        <TimeInput label={`${label}, fermeture ${index + 1}`} value={item.end} disabled={!editable}
                          onChange={value => update(day, ranges.map((current, i) => (i === index ? range(current.start, value) : current)))} />
                        {item.endsNextDay && <span className="flex items-center gap-1 text-xs text-violet-700"><Moon className="size-3.5" aria-hidden="true" />lendemain</span>}
                        {editable && (
                          <button type="button" aria-label={`Retirer la plage ${index + 1} du ${label.toLowerCase()}`} onClick={() => update(day, ranges.filter((_, i) => i !== index))}
                            className="grid size-8 place-items-center rounded-lg text-ink-500 hover:bg-slate-100"><X className="size-4" /></button>
                        )}
                      </div>
                    ))}
                    {invalid && <p className="text-xs text-red-600">Les plages se chevauchent ou sont vides.</p>}
                  </div>
                )}
              </div>
              {editable && open && (
                <div className="flex shrink-0 gap-1 sm:pt-0.5">
                  {ranges.length < 3 && (
                    <Button size="sm" variant="ghost" onClick={() => {
                      const last = ranges.at(-1)!;
                      update(day, [...ranges, range(last.endsNextDay ? '12:00' : last.end, last.endsNextDay ? '14:00' : '23:30')]);
                    }}><Plus className="size-4" aria-hidden="true" />Plage</Button>
                  )}
                  <Button size="sm" variant="ghost" aria-label={`Copier les horaires du ${label.toLowerCase()} sur toute la semaine`} title="Appliquer à tous les jours"
                    onClick={() => setWeek(Object.fromEntries(days.map(({day: target}) => [target, ranges.map(item => ({...item}))])) as Week)}>
                    <Copy className="size-4" aria-hidden="true" />
                  </Button>
                </div>
              )}
            </li>
          );
        })}
      </ul>
      {editable && (
        <div className="mt-4 flex flex-wrap items-center gap-2">
          <Button disabled={!dirty || invalidDays.length > 0} busy={busy === 'hours'} onClick={() => run('hours', async () => {
            await setBusinessHours(location.id, days.flatMap(({day}) => week[day].map(item => ({day, ...item}))));
            await onSaved();
          }, 'Horaires enregistrés.')}>Enregistrer les horaires</Button>
          {dirty && <Button variant="ghost" onClick={() => setWeek(initial)}>Annuler les changements</Button>}
          <p className="text-xs text-ink-500">Une fermeture avant l’ouverture se termine le lendemain (service de nuit).</p>
        </div>
      )}
    </div>
  );
}
