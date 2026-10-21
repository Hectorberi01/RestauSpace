'use client';
import {useState} from 'react';
import {Clock, Plus, Trash2} from 'lucide-react';
import {put} from '@/lib/api';
import type {MenuSlot, Weekday} from '@/lib/types';
import {Button, Input, cx, useAction} from '@/components/ui';

const days: {value: Weekday; short: string}[] = [
  {value: 'Monday', short: 'Lun'},
  {value: 'Tuesday', short: 'Mar'},
  {value: 'Wednesday', short: 'Mer'},
  {value: 'Thursday', short: 'Jeu'},
  {value: 'Friday', short: 'Ven'},
  {value: 'Saturday', short: 'Sam'},
  {value: 'Sunday', short: 'Dim'},
];

interface Group {
  days: Weekday[];
  start: string;
  end: string;
}

const presets: {label: string; start: string; end: string}[] = [
  {label: 'Midi', start: '11:30', end: '15:00'},
  {label: 'Soir', start: '18:30', end: '23:00'},
  {label: 'Happy hour', start: '17:00', end: '19:00'},
];

/** Les créneaux regroupés par horaire : un groupe = mêmes heures, plusieurs jours. */
const grouped = (slots: MenuSlot[]): Group[] => {
  const map = new Map<string, Group>();
  for (const slot of slots) {
    const key = `${slot.start}-${slot.end}`;
    const group = map.get(key) ?? {days: [], start: slot.start, end: slot.end};
    group.days.push(slot.day);
    map.set(key, group);
  }
  return [...map.values()];
};

/**
 * Créneaux d'une carte (midi, soir, happy hour), en heure locale de l'établissement. Sans créneau, la carte est
 * proposée à toute heure d'ouverture. Modifiable même publiée : seul le moment où elle est proposée change.
 */
export function MenuSchedule({menuId, slots, editable, onChanged}: {menuId: string; slots: MenuSlot[]; editable: boolean; onChanged: () => Promise<void>}) {
  const {busy, run} = useAction();
  const [groups, setGroups] = useState<Group[]>(() => grouped(slots));
  const dirty = JSON.stringify(groups) !== JSON.stringify(grouped(slots));
  const valid = groups.every(group => group.days.length > 0 && /^\d\d:\d\d$/.test(group.start) && /^\d\d:\d\d$/.test(group.end) && group.start !== group.end);

  const update = (index: number, change: Partial<Group>) => setGroups(current => current.map((group, i) => (i === index ? {...group, ...change} : group)));
  const save = () =>
    run('schedule', async () => {
      await put(`/catalog/menus/${menuId}/schedule`, {slots: groups.flatMap(group => group.days.map(day => ({day, start: group.start, end: group.end})))});
      await onChanged();
    }, groups.length ? 'Créneaux enregistrés.' : 'Carte proposée à toute heure.');

  return (
    <section className="rounded-xl border border-slate-200 p-4">
      <h3 className="flex items-center gap-2 text-sm font-semibold"><Clock className="size-4" aria-hidden="true" />Créneaux</h3>
      <p className="mt-1 mb-3 text-sm text-ink-500">
        {groups.length === 0 ? 'Proposée à toute heure d’ouverture.' : 'Proposée en ligne et en borne seulement pendant ces créneaux (heure de l’établissement).'}
      </p>
      <div className="space-y-3">
        {groups.map((group, index) => (
          <div key={index} className="flex flex-wrap items-center gap-2">
            <div className="flex flex-wrap gap-1" role="group" aria-label={`Jours du créneau ${index + 1}`}>
              {days.map(day => {
                const on = group.days.includes(day.value);
                return (
                  <button key={day.value} type="button" aria-pressed={on} disabled={!editable}
                    onClick={() => update(index, {days: on ? group.days.filter(item => item !== day.value) : [...group.days, day.value]})}
                    className={cx('rounded-lg border px-2 py-1 text-xs font-semibold', on ? 'border-brand-500 bg-brand-50 text-brand-700' : 'border-slate-300 text-ink-500')}>
                    {day.short}
                  </button>
                );
              })}
            </div>
            <Input type="time" aria-label="Début" className="w-32!" value={group.start} disabled={!editable} onChange={event => update(index, {start: event.target.value})} />
            <span className="text-sm text-ink-500">à</span>
            <Input type="time" aria-label="Fin" className="w-32!" value={group.end} disabled={!editable} onChange={event => update(index, {end: event.target.value})} />
            {group.end < group.start && <span className="text-xs text-ink-500">(le lendemain)</span>}
            {editable && (
              <Button size="sm" variant="ghost" aria-label="Retirer ce créneau" onClick={() => setGroups(current => current.filter((_, i) => i !== index))}><Trash2 className="size-4" /></Button>
            )}
          </div>
        ))}
      </div>
      {editable && (
        <div className="mt-3 flex flex-wrap items-center gap-2">
          {presets.map(preset => (
            <Button key={preset.label} size="sm" variant="secondary" onClick={() => setGroups(current => [...current, {days: days.map(day => day.value), start: preset.start, end: preset.end}])}>
              <Plus className="size-3.5" aria-hidden="true" />{preset.label}
            </Button>
          ))}
          <Button size="sm" className="ml-auto" busy={busy === 'schedule'} disabled={!dirty || !valid} onClick={() => void save()}>Enregistrer les créneaux</Button>
        </div>
      )}
    </section>
  );
}
