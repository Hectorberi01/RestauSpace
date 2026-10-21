'use client';
import {useRef, useState, type PointerEvent} from 'react';
import {Clock, Coins, Minus, Move, Plus, RotateCw, Scan} from 'lucide-react';
import {put} from '@/lib/api';
import {compact, elapsed} from '@/lib/format';
import type {DiningArea, DiningTable, TableLayout, TableSession} from '@/lib/types';
import {cx, useAction} from '@/components/ui';

const GRID = 100;
const MIN_ROWS = 36;
const DRAG_THRESHOLD = 4;
const LONG_STAY_MINUTES = 90;
export const ROTATION_STEP = 15;

/** L'angle tourné de <paramref name="delta"/> degrés, ramené entre 0 et 345. */
export const turned = (angle: number, delta: number) => (((angle + delta) % 360) + 360) % 360;

/** Taille et forme proposées pour une table qui n'a pas encore de place sur le plan. */
export function defaultLayout(table: DiningTable): Omit<TableLayout, 'x' | 'y'> {
  if (table.capacity <= 2) return {width: 9, height: 9, shape: 'Round'};
  if (table.capacity <= 4) return {width: 10, height: 10, shape: 'Square'};
  if (table.capacity <= 6) return {width: 16, height: 10, shape: 'Rectangle'};
  return {width: 22, height: 10, shape: 'Rectangle'};
}

const overlaps = (a: TableLayout, b: TableLayout) => a.x < b.x + b.width && b.x < a.x + a.width && a.y < b.y + b.height && b.y < a.y + a.height;

/** Première place libre de la grille, ligne par ligne, avec un espace de 4 cases autour des autres tables (place des chaises). */
export function freeSpot(placed: TableLayout[], size: Omit<TableLayout, 'x' | 'y'>): TableLayout {
  for (let y = 4; y + size.height <= GRID; y += 2) {
    for (let x = 4; x + size.width <= GRID - 2; x += 2) {
      const candidate = {...size, x, y};
      const padded = {...candidate, x: x - 4, y: y - 4, width: size.width + 8, height: size.height + 8};
      if (!placed.some(other => overlaps(padded, other))) return candidate;
    }
  }
  return {...size, x: 0, y: 0};
}

export const saveLayout = (areaId: string, tableId: string, layout: TableLayout) =>
  put(`/floor/dining-areas/${areaId}/tables/${tableId}/layout`, layout);

/** La place de chaque table : la sienne, ou une place libre proposée (gardée seulement si on déplace la table). */
export function arrange(tables: DiningTable[]): Map<string, TableLayout> {
  const result = new Map<string, TableLayout>();
  const used: TableLayout[] = [];
  tables.filter(table => table.layout).forEach(table => {
    result.set(table.id, table.layout!);
    used.push(table.layout!);
  });
  tables.filter(table => !table.layout).forEach(table => {
    const spot = freeSpot(used, defaultLayout(table));
    result.set(table.id, spot);
    used.push(spot);
  });
  return result;
}

type Side = 'top' | 'bottom' | 'left' | 'right';

/** Les chaises autour d'une table : d'abord sur les grands côtés, au plus 5 par côté. */
function chairs(capacity: number, layout: TableLayout): Record<Side, number> {
  const count: Record<Side, number> = {top: 0, bottom: 0, left: 0, right: 0};
  const wide = layout.width >= layout.height * 1.4;
  const tall = layout.height >= layout.width * 1.4;
  const order: Side[] = wide ? ['top', 'bottom', 'top', 'bottom', 'left', 'right'] : tall ? ['left', 'right', 'left', 'right', 'top', 'bottom'] : ['top', 'bottom', 'left', 'right'];
  for (let seat = 0, step = 0; seat < Math.min(capacity, 20) && step < 200; step++) {
    const side = order[step % order.length];
    if (count[side] < 5) {
      count[side]++;
      seat++;
    }
  }
  return count;
}

function Chairs({capacity, layout, tone}: {capacity: number; layout: TableLayout; tone: string}) {
  const count = chairs(capacity, layout);
  const bar = cx('absolute rounded-full transition-colors', tone);
  const spread = (n: number) => Array.from({length: n}, (_, i) => `${((i + 1) * 100) / (n + 1)}%`);
  // Longueur d'une chaise : une part du côté, bornée pour rester lisible à tous les zooms.
  const along = (n: number) => `clamp(10px, ${Math.min(34, 72 / Math.max(n, 1))}%, 30px)`;
  return (
    <>
      {spread(count.top).map(left => <span key={`t${left}`} className={bar} style={{left, top: -12, width: along(count.top), height: 7, translate: '-50% 0'}} />)}
      {spread(count.bottom).map(left => <span key={`b${left}`} className={bar} style={{left, bottom: -12, width: along(count.bottom), height: 7, translate: '-50% 0'}} />)}
      {spread(count.left).map(top => <span key={`l${top}`} className={bar} style={{top, left: -12, width: 7, height: along(count.left), translate: '0 -50%'}} />)}
      {spread(count.right).map(top => <span key={`r${top}`} className={bar} style={{top, right: -12, width: 7, height: along(count.right), translate: '0 -50%'}} />)}
    </>
  );
}

interface Drag {
  mode: 'move' | 'turn';
  id: string;
  pointer: number;
  startX: number;
  startY: number;
  origin: TableLayout;
  current: TableLayout;
  moved: boolean;
}

/**
 * Le plan dessiné d'une salle : chaque table à sa place, avec sa forme et ses chaises. Les tables sans place reçoivent
 * une place libre proposée. Avec <paramref name="canArrange"/>, une table se déplace en la faisant glisser (la place est
 * enregistrée au lâcher) ; un simple clic l'ouvre. La poignée au-dessus de la table la fait tourner par pas de 15°
 * (un clic la tourne de 45°).
 */
export function DrawnPlan({area, sessions, totals, currency, now, canArrange, onSelect, onChanged}: {
  area: DiningArea;
  sessions: Map<string, TableSession>;
  totals: Map<string, number>;
  currency: string;
  now: number;
  canArrange: boolean;
  onSelect: (tableId: string) => void;
  onChanged: () => Promise<void>;
}) {
  const {busy, run} = useAction();
  const board = useRef<HTMLDivElement>(null);
  const [drag, setDrag] = useState<Drag>();
  const [zoom, setZoom] = useState(1);
  const [active, setActive] = useState<string>();
  const layouts = arrange(area.tables);
  const layoutOf = (table: DiningTable) => (drag?.id === table.id ? drag.current : layouts.get(table.id)!);
  const bottom = Math.max(MIN_ROWS, ...area.tables.map(table => { const layout = layoutOf(table); return layout.y + layout.height + 6; }));
  const rows = Math.min(GRID, bottom);

  const units = (pixels: number) => (pixels / (board.current?.clientWidth || 1)) * GRID;

  const begin = (event: PointerEvent<HTMLButtonElement>, table: DiningTable) => {
    if (!canArrange || event.button !== 0) return;
    const origin = layouts.get(table.id)!;
    event.currentTarget.setPointerCapture(event.pointerId);
    setActive(table.id);
    setDrag({mode: 'move', id: table.id, pointer: event.pointerId, startX: event.clientX, startY: event.clientY, origin, current: origin, moved: false});
  };

  const beginTurn = (event: PointerEvent<HTMLButtonElement>, table: DiningTable) => {
    if (event.button !== 0) return;
    event.stopPropagation();
    const origin = layouts.get(table.id)!;
    event.currentTarget.setPointerCapture(event.pointerId);
    setActive(table.id);
    setDrag({mode: 'turn', id: table.id, pointer: event.pointerId, startX: event.clientX, startY: event.clientY, origin, current: origin, moved: false});
  };

  const turn = (event: PointerEvent<HTMLButtonElement>) => {
    if (!drag || drag.mode !== 'turn' || event.pointerId !== drag.pointer) return;
    if (!drag.moved && Math.hypot(event.clientX - drag.startX, event.clientY - drag.startY) < DRAG_THRESHOLD) return;
    const frame = event.currentTarget.parentElement!.getBoundingClientRect();
    const degrees = (Math.atan2(event.clientX - (frame.left + frame.width / 2), (frame.top + frame.height / 2) - event.clientY) * 180) / Math.PI;
    const rotation = turned(Math.round(degrees / ROTATION_STEP) * ROTATION_STEP, 0);
    setDrag({...drag, current: {...drag.origin, rotation}, moved: true});
  };

  const endTurn = (table: DiningTable) => {
    if (!drag || drag.mode !== 'turn') return;
    const current = drag.moved ? drag.current : {...drag.origin, rotation: turned(drag.origin.rotation ?? 0, 45)};
    if ((current.rotation ?? 0) === (drag.origin.rotation ?? 0)) {
      setDrag(undefined);
      return;
    }
    setDrag({...drag, current, moved: true});
    void run(table.id, async () => {
      try {
        await saveLayout(area.id, table.id, current);
        await onChanged();
      } finally {
        setDrag(undefined);
      }
    });
  };

  const move = (event: PointerEvent<HTMLButtonElement>) => {
    if (!drag || drag.mode !== 'move' || event.pointerId !== drag.pointer) return;
    const distance = Math.hypot(event.clientX - drag.startX, event.clientY - drag.startY);
    if (!drag.moved && distance < DRAG_THRESHOLD) return;
    const x = Math.round(Math.min(GRID - drag.origin.width, Math.max(0, drag.origin.x + units(event.clientX - drag.startX))));
    const y = Math.round(Math.min(GRID - drag.origin.height, Math.max(0, drag.origin.y + units(event.clientY - drag.startY))));
    setDrag({...drag, current: {...drag.origin, x, y}, moved: true});
  };

  const end = (table: DiningTable) => {
    if (!drag || drag.mode !== 'move') return;
    const {current, moved} = drag;
    if (!moved) {
      setDrag(undefined);
      onSelect(table.id);
      return;
    }
    void run(table.id, async () => {
      try {
        await saveLayout(area.id, table.id, current);
        await onChanged();
      } finally {
        setDrag(undefined);
      }
    });
  };

  const placeAll = () =>
    run('place', async () => {
      for (const table of area.tables.filter(item => !item.layout)) await saveLayout(area.id, table.id, layouts.get(table.id)!);
      await onChanged();
    }, 'Plan enregistré.');

  const unplaced = area.tables.filter(table => !table.layout).length;

  return (
    <div className="relative overflow-hidden rounded-2xl border border-slate-200 bg-slate-50">
      <div className="max-h-[72vh] overflow-auto">
        <div
          ref={board}
          aria-label={`Plan de ${area.name}`}
          style={{width: `${zoom * 100}%`, aspectRatio: `${GRID} / ${rows}`, minWidth: 560}}
          className={cx('relative touch-none select-none', canArrange && 'bg-[radial-gradient(circle,#d5dde6_1px,transparent_1.5px)] bg-[size:22px_22px]')}>
          {area.tables.map(table => {
            const layout = layoutOf(table);
            const session = table.openSessionId ? sessions.get(table.openSessionId) : undefined;
            const minutes = session ? (now - new Date(session.openedAtUtc).getTime()) / 60_000 : 0;
            const long = session && minutes >= LONG_STAY_MINUTES;
            const total = session ? totals.get(session.id) : undefined;
            const dragging = drag?.id === table.id && drag.moved && drag.mode === 'move';
            const turning = drag?.id === table.id && drag.mode === 'turn';
            const rotation = layout.shape === 'Round' ? 0 : layout.rotation ?? 0;
            const rotatable = canArrange && layout.shape !== 'Round';
            const tone = !table.isInService
              ? {table: 'bg-slate-100 text-ink-400 ring-1 ring-slate-200 [background-image:repeating-linear-gradient(135deg,transparent_0_6px,rgb(226_232_240)_6px_7px)]', chair: 'bg-slate-200/70'}
              : long
                ? {table: 'bg-amber-500 text-white ring-1 ring-amber-600 shadow-lg shadow-amber-500/25', chair: 'bg-amber-400'}
                : session
                  ? {table: 'bg-brand-100 text-brand-900 ring-1 ring-brand-300 shadow-md shadow-brand-500/10', chair: 'bg-brand-300'}
                  : {table: 'bg-white text-ink-800 ring-1 ring-slate-200 shadow-sm hover:ring-brand-400', chair: 'bg-slate-300/80'};
            return (
              <div
                key={table.id}
                onPointerEnter={() => canArrange && !drag && setActive(table.id)}
                style={{left: `${layout.x}%`, top: `${(layout.y / rows) * 100}%`, width: `${layout.width}%`, height: `${(layout.height / rows) * 100}%`, rotate: `${rotation}deg`}}
                className={cx('group absolute', dragging || turning ? 'z-20' : active === table.id ? 'z-[15]' : 'z-10', !turning && 'transition-[rotate]')}>
              <button
                type="button"
                aria-label={`Table ${table.label}${session ? `, occupée par ${session.guestCount}` : ''}`}
                onPointerDown={event => begin(event, table)}
                onPointerMove={move}
                onPointerUp={() => end(table)}
                onPointerCancel={() => setDrag(undefined)}
                onClick={event => (!canArrange || event.detail === 0) && onSelect(table.id)}
                disabled={busy === table.id}
                className={cx('absolute inset-0 outline-none transition-transform', canArrange ? 'cursor-grab active:cursor-grabbing' : 'cursor-pointer', dragging && 'scale-[1.04]')}>
                <Chairs capacity={table.capacity} layout={layout} tone={tone.chair} />
                <span className={cx(
                  'absolute inset-0 flex flex-col items-center justify-center gap-1 overflow-hidden p-1.5 text-center leading-none transition',
                  layout.shape === 'Round' ? 'rounded-full' : 'rounded-2xl',
                  tone.table,
                  (dragging || turning) && 'shadow-xl ring-2 ring-brand-500',
                  !table.layout && canArrange && 'border border-dashed border-slate-300',
                )}>
                  <span className="flex flex-col items-center justify-center gap-1" style={{rotate: `${-rotation}deg`}}>
                  <span className="text-sm font-bold tracking-tight sm:text-base">{table.label}</span>
                  {session ? (
                    <span className="flex flex-col items-center gap-0.5">
                      {total !== undefined && total > 0 && (
                        <span className={cx('flex items-center gap-1 rounded-full px-1.5 py-0.5 text-[0.65rem] font-semibold', long ? 'bg-white/20' : 'bg-white/70')}>
                          <Coins className="size-2.5" aria-hidden="true" />{compact(total)} {currency === 'XOF' ? 'F' : currency}
                        </span>
                      )}
                      <span className={cx('flex items-center gap-1 rounded-full px-1.5 py-0.5 text-[0.65rem] font-semibold', long ? 'bg-white/20' : 'bg-white/70')}>
                        <Clock className="size-2.5" aria-hidden="true" />{elapsed(session.openedAtUtc, now)}
                      </span>
                    </span>
                  ) : (
                    <span className="text-[0.65rem] text-ink-400">{table.isInService ? `${table.capacity} pl.` : 'Hors service'}</span>
                  )}
                  </span>
                </span>
                {session && (
                  <span className={cx('absolute -top-2 -right-2 grid size-5 place-items-center rounded-full text-[0.6rem] font-bold shadow ring-2 ring-white', long ? 'bg-white text-amber-700' : 'bg-brand-600 text-white')}>
                    {session.guestCount}
                  </span>
                )}
              </button>
              {rotatable && (
                <button
                  type="button"
                  aria-label={`Tourner la table ${table.label}`}
                  title={turning && drag?.moved ? `${drag.current.rotation ?? 0}°` : 'Faites glisser pour tourner, cliquez pour tourner de 45°'}
                  onPointerDown={event => beginTurn(event, table)}
                  onPointerMove={turn}
                  onPointerUp={() => endTurn(table)}
                  onPointerCancel={() => setDrag(undefined)}
                  onKeyDown={event => {
                    if (event.key !== 'ArrowLeft' && event.key !== 'ArrowRight') return;
                    event.preventDefault();
                    const next = {...layout, rotation: turned(rotation, event.key === 'ArrowRight' ? ROTATION_STEP : -ROTATION_STEP)};
                    void run(table.id, async () => {
                      await saveLayout(area.id, table.id, next);
                      await onChanged();
                    });
                  }}
                  disabled={busy === table.id}
                  className={cx(
                    'absolute left-1/2 -top-9 grid size-6 -translate-x-1/2 cursor-grab place-items-center rounded-full bg-white text-brand-700 shadow ring-1 ring-brand-300 transition-opacity hover:bg-brand-50 focus-visible:opacity-100 active:cursor-grabbing',
                    "after:absolute after:top-full after:left-1/2 after:h-3 after:w-px after:-translate-x-1/2 after:bg-brand-300 after:content-['']",
                    active === table.id || turning ? 'opacity-100' : 'opacity-0 group-hover:opacity-100',
                  )}>
                  <RotateCw className="size-3.5" aria-hidden="true" />
                </button>
              )}
              {turning && drag.moved && (
                <span className="pointer-events-none absolute left-1/2 top-1/2 rounded-full bg-ink-900/85 px-2 py-0.5 text-xs font-semibold text-white" style={{translate: '-50% -50%', rotate: `${-rotation}deg`}}>
                  {rotation}°
                </span>
              )}
              </div>
            );
          })}
          {area.tables.length === 0 && (
            <p className="absolute inset-0 grid place-items-center p-6 text-center text-sm text-ink-500">Aucune table dans cette salle.</p>
          )}
        </div>
      </div>

      <div className="pointer-events-none absolute inset-x-3 bottom-3 flex items-end justify-between gap-2">
        <div className="pointer-events-auto flex flex-col overflow-hidden rounded-xl bg-white shadow-md ring-1 ring-slate-200">
          <button type="button" aria-label="Agrandir" className="grid size-9 place-items-center hover:bg-slate-50 disabled:opacity-40" disabled={zoom >= 1.8} onClick={() => setZoom(value => Math.min(1.8, value + 0.2))}><Plus className="size-4" /></button>
          <button type="button" aria-label="Réduire" className="grid size-9 place-items-center border-t border-slate-100 hover:bg-slate-50 disabled:opacity-40" disabled={zoom <= 1} onClick={() => setZoom(value => Math.max(1, value - 0.2))}><Minus className="size-4" /></button>
          <button type="button" aria-label="Ajuster à l’écran" className="grid size-9 place-items-center border-t border-slate-100 hover:bg-slate-50" onClick={() => setZoom(1)}><Scan className="size-4" /></button>
        </div>
        {canArrange && (
          <div className="pointer-events-auto flex items-center gap-2 rounded-xl bg-white/95 px-3 py-2 text-xs text-ink-600 shadow-md ring-1 ring-slate-200">
            <Move className="size-3.5" aria-hidden="true" />
            <span>Glissez une table pour la déplacer, sa poignée <RotateCw className="inline size-3" aria-hidden="true" /> pour la tourner.</span>
            {unplaced > 0 && (
              <button type="button" disabled={busy === 'place'} onClick={() => void placeAll()} className="font-semibold text-brand-700 hover:underline disabled:opacity-50">
                Garder cette disposition
              </button>
            )}
          </div>
        )}
      </div>
    </div>
  );
}
