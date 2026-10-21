'use client';
import {useState} from 'react';
import Link from 'next/link';
import {useRouter} from 'next/navigation';
import {ArrowRightLeft, Ban, LayoutGrid, Map as MapIcon, Merge, Pencil, Plus, QrCode, RotateCcw, RotateCw, Send, Store, Trash2, Users} from 'lucide-react';
import {api, del, post, put, type Page} from '@/lib/api';
import {elapsed, money, orderStatusLabel, orderStatusTone} from '@/lib/format';
import {useNow, useResource} from '@/lib/hooks';
import {courseLabel, fireCourse, fireLabel} from '@/lib/kitchen';
import {createOrder} from '@/lib/orders';
import type {Course, DiningArea, DiningTable, KitchenTicket, LocationSummary, OrderSummary, TableLayout, TableShape, TableSession} from '@/lib/types';
import {useConsole, WithLocation} from '@/components/console';
import {Alert, Badge, Button, Card, Empty, Field, Input, Modal, PageHeader, Select, Spinner, cx, useAction} from '@/components/ui';
import {DrawnPlan, ROTATION_STEP, defaultLayout, saveLayout, turned} from './drawn-plan';

interface Selected {
  area: DiningArea;
  table: DiningTable;
  session?: TableSession;
}

function TableDialog({selected, areas, location, onClose, onChanged}: {selected: Selected; areas: DiningArea[]; location: LocationSummary; onClose: () => void; onChanged: () => Promise<void>}) {
  const {can} = useConsole();
  const router = useRouter();
  const {busy, run} = useAction();
  const {table, session} = selected;
  const [guests, setGuests] = useState(String(session?.guestCount ?? Math.min(2, table.capacity)));
  const [capacity, setCapacity] = useState(String(table.capacity));
  const [label, setLabel] = useState(table.label);
  const {data: orders} = useResource(
    signal => (session ? api<Page<OrderSummary>>('/orders', {query: {tableSessionId: session.id, pageSize: 50}, signal}).then(page => page.items) : Promise.resolve([])),
    [session?.id],
  );
  const open = orders?.filter(order => order.status !== 'Completed' && order.status !== 'Cancelled') ?? [];
  const [moveTo, setMoveTo] = useState('');
  const [mergeInto, setMergeInto] = useState('');
  const others = areas.flatMap(area => area.tables.filter(item => item.id !== table.id && item.isInService).map(item => ({area, table: item})));
  const free = others.filter(item => !item.table.openSessionId);
  const busyTables = others.filter(item => item.table.openSessionId);

  const openTable = (thenOrder: boolean) => run(thenOrder ? 'open-order' : 'open', async () => {
    const created = await post<{id: string}>('/floor/table-sessions', {tableId: table.id, guestCount: Number(guests) || 1, reservationId: null});
    if (thenOrder && can('orders.take')) {
      const id = await createOrder(location.id, 'DineIn', created.id);
      router.push(`/commandes/${id}`);
      return;
    }
    await onChanged();
    onClose();
  }, thenOrder ? undefined : 'Table ouverte.');

  return (
    <Modal open onClose={onClose} title={`Table ${table.label} · ${selected.area.name}`}>
      <div className="space-y-5">
        <p className="text-sm text-ink-600">{table.capacity} places · {table.isInService ? (session ? `occupée depuis ${elapsed(session.openedAtUtc)}` : 'libre') : 'hors service'}</p>

        {can('floor.manage') && (
          <form className="flex items-end gap-2" onSubmit={event => {
            event.preventDefault();
            void run('label', async () => {
              await put(`/floor/dining-areas/${selected.area.id}/tables/${table.id}/label`, {label: label.trim()});
              await onChanged();
            }, 'Table renommée.');
          }}>
            <Field label="Nom de la table" className="w-40"><Input required maxLength={30} value={label} onChange={event => setLabel(event.target.value)} /></Field>
            <Button type="submit" variant="secondary" busy={busy === 'label'} disabled={!label.trim() || label.trim() === table.label}>Renommer</Button>
          </form>
        )}

        {can('floor.manage') && <ShapeEditor areaId={selected.area.id} table={table} onChanged={onChanged} />}

        {can('floor.manage') && (
          <form className="flex items-end gap-2" onSubmit={event => {
            event.preventDefault();
            void run('capacity', async () => {
              await put(`/floor/dining-areas/${selected.area.id}/tables/${table.id}/capacity`, {capacity: Number(capacity)});
              await onChanged();
            }, 'Nombre de places enregistré.');
          }}>
            <Field label="Places à cette table" className="w-40"><Input type="number" min={1} max={99} required value={capacity} onChange={event => setCapacity(event.target.value)} /></Field>
            <Button type="submit" variant="secondary" busy={busy === 'capacity'} disabled={Number(capacity) === table.capacity || Number(capacity) < 1}>Modifier</Button>
          </form>
        )}

        {!table.isInService && can('floor.serve') && (
          <Button busy={busy === 'restore'} onClick={() => run('restore', async () => {
            await post(`/floor/dining-areas/${selected.area.id}/tables/${table.id}/restore`);
            await onChanged();
            onClose();
          }, 'Table remise en service.')}>Remettre en service</Button>
        )}

        {table.isInService && !session && can('floor.serve') && (
          <>
            <Field label="Nombre de couverts"><Input type="number" min={1} max={99} value={guests} onChange={event => setGuests(event.target.value)} /></Field>
            <div className="flex flex-wrap gap-2">
              {can('orders.take') && location.status === 'Active' && <Button busy={busy === 'open-order'} onClick={() => openTable(true)}>Ouvrir et commander</Button>}
              <Button variant="secondary" busy={busy === 'open'} onClick={() => openTable(false)}>Ouvrir la table</Button>
              <Button variant="ghost" busy={busy === 'out'} onClick={() => run('out', async () => {
                await post(`/floor/dining-areas/${selected.area.id}/tables/${table.id}/out-of-service`);
                await onChanged();
                onClose();
              }, 'Table mise hors service.')}><Ban className="size-4" aria-hidden="true" />Hors service</Button>
            </div>
          </>
        )}

        {session && (
          <>
            <div>
              <h3 className="mb-2 text-sm font-semibold">Commandes de la table</h3>
              {!orders && <Spinner />}
              {orders?.length === 0 && <p className="text-sm text-ink-500">Aucune commande pour l’instant.</p>}
              <ul className="divide-y divide-slate-100 rounded-xl border border-slate-200">
                {orders?.map(order => (
                  <li key={order.id}>
                    <Link href={`/commandes/${order.id}`} className="flex items-center justify-between gap-2 px-4 py-3 hover:bg-slate-50">
                      <span className="font-semibold">#{order.number}</span>
                      <Badge tone={orderStatusTone[order.status]}>{orderStatusLabel[order.status]}</Badge>
                      <span className="ml-auto text-sm font-semibold">{money(order.total, order.currencyCode)}</span>
                    </Link>
                  </li>
                ))}
              </ul>
            </div>
            {can('kitchen.operate') && open.length > 0 && <Courses orders={open} location={location} />}
            {can('floor.serve') && (
              <form className="flex items-end gap-2" onSubmit={event => {
                event.preventDefault();
                void run('guests', async () => {
                  await put(`/floor/table-sessions/${session.id}/guest-count`, {guestCount: Number(guests) || 1});
                  await onChanged();
                }, 'Couverts mis à jour.');
              }}>
                <Field label="Couverts" className="w-32"><Input type="number" min={1} max={99} value={guests} onChange={event => setGuests(event.target.value)} /></Field>
                <Button type="submit" variant="secondary" busy={busy === 'guests'}>Enregistrer</Button>
              </form>
            )}
            <div className="flex flex-wrap gap-2">
              {can('orders.take') && location.status === 'Active' && (
                <Button busy={busy === 'order'} onClick={() => run('order', async () => {
                  const id = await createOrder(location.id, 'DineIn', session.id);
                  router.push(`/commandes/${id}`);
                })}><Plus className="size-4" aria-hidden="true" />Nouvelle commande</Button>
              )}
              {can('floor.serve') && (
                <Button variant="secondary" busy={busy === 'close'} disabled={open.length > 0} title={open.length > 0 ? 'Terminez ou annulez d’abord les commandes ouvertes' : undefined}
                  onClick={() => run('close', async () => {
                    await post(`/floor/table-sessions/${session.id}/close`);
                    await onChanged();
                    onClose();
                  }, 'Table libérée.')}>Libérer la table</Button>
              )}
            </div>
            {open.length > 0 && <p className="text-xs text-ink-500">La table se libère quand toutes ses commandes sont terminées ou annulées.</p>}
            {can('floor.serve') && (
              <div className="space-y-3 border-t border-slate-200 pt-4">
                <div className="flex items-end gap-2">
                  <Field label="Changer de table" className="min-w-0 flex-1">
                    <Select value={moveTo} onChange={event => setMoveTo(event.target.value)}>
                      <option value="">Table libre…</option>
                      {free.map(item => <option key={item.table.id} value={item.table.id}>{item.table.label} · {item.area.name}</option>)}
                    </Select>
                  </Field>
                  <Button variant="secondary" busy={busy === 'move'} disabled={!moveTo} onClick={() => run('move', async () => {
                    await post(`/floor/table-sessions/${session.id}/move`, {tableId: moveTo});
                    await onChanged();
                    onClose();
                  }, 'Les convives ont changé de table, avec leurs commandes.')}><ArrowRightLeft className="size-4" aria-hidden="true" />Déplacer</Button>
                </div>
                <div className="flex items-end gap-2">
                  <Field label="Réunir avec" className="min-w-0 flex-1">
                    <Select value={mergeInto} onChange={event => setMergeInto(event.target.value)}>
                      <option value="">Table occupée…</option>
                      {busyTables.map(item => <option key={item.table.id} value={item.table.openSessionId ?? ''}>{item.table.label} · {item.area.name}</option>)}
                    </Select>
                  </Field>
                  <Button variant="secondary" busy={busy === 'merge'} disabled={!mergeInto} onClick={() => run('merge', async () => {
                    await post(`/floor/table-sessions/${session.id}/merge`, {intoTableSessionId: mergeInto});
                    await onChanged();
                    onClose();
                  }, 'Tables réunies : les commandes et les couverts sont passés sur l’autre table.')}><Merge className="size-4" aria-hidden="true" />Réunir</Button>
                </div>
              </div>
            )}
          </>
        )}
      </div>
    </Modal>
  );
}

const shapeLabel: Record<TableShape, string> = {Square: 'Carrée', Round: 'Ronde', Rectangle: 'Rectangulaire'};
const sizes = [{value: 'S', label: 'Petite', size: 8}, {value: 'M', label: 'Moyenne', size: 10}, {value: 'L', label: 'Grande', size: 14}];

/** Forme et taille de la table sur le plan dessiné. */
function ShapeEditor({areaId, table, onChanged}: {areaId: string; table: DiningTable; onChanged: () => Promise<void>}) {
  const {busy, run} = useAction();
  const layout = table.layout ?? {...defaultLayout(table), x: 4, y: 4};
  const size = sizes.reduce((best, item) => (Math.abs(item.size - layout.height) < Math.abs(best.size - layout.height) ? item : best));
  const rotation = layout.rotation ?? 0;
  const save = (next: TableLayout) =>
    run('shape', async () => {
      await saveLayout(areaId, table.id, next);
      await onChanged();
    }, 'Plan mis à jour.');
  const apply = (shape: TableShape, side: number) => {
    const width = shape === 'Rectangle' ? Math.min(40, Math.round(side * 1.6)) : side;
    return save({shape, width, height: side, x: Math.min(layout.x, 100 - width), y: Math.min(layout.y, 100 - side), rotation: shape === 'Round' ? 0 : rotation});
  };
  const rotate = (angle: number) => save({...layout, rotation: angle});
  return (
    <div className="flex flex-wrap items-end gap-2">
      <Field label="Forme sur le plan" className="w-40">
        <Select value={layout.shape} disabled={busy === 'shape'} onChange={event => void apply(event.target.value as TableShape, size.size)}>
          {(Object.keys(shapeLabel) as TableShape[]).map(shape => <option key={shape} value={shape}>{shapeLabel[shape]}</option>)}
        </Select>
      </Field>
      <Field label="Taille" className="w-32">
        <Select value={size.value} disabled={busy === 'shape'} onChange={event => void apply(layout.shape, sizes.find(item => item.value === event.target.value)!.size)}>
          {sizes.map(item => <option key={item.value} value={item.value}>{item.label}</option>)}
        </Select>
      </Field>
      {layout.shape !== 'Round' && (
        <Field label="Orientation" className="w-44">
          <div className="flex items-center gap-1">
            <button type="button" aria-label="Tourner vers la gauche" disabled={busy === 'shape'} onClick={() => void rotate(turned(rotation, -ROTATION_STEP))}
              className="grid size-10 shrink-0 place-items-center rounded-xl border border-slate-200 bg-white text-ink-600 hover:bg-slate-50 disabled:opacity-50">
              <RotateCcw className="size-4" aria-hidden="true" />
            </button>
            <Select aria-label="Angle" value={String(rotation)} disabled={busy === 'shape'} onChange={event => void rotate(Number(event.target.value))}>
              {Array.from({length: 360 / ROTATION_STEP}, (_, i) => i * ROTATION_STEP).map(angle => <option key={angle} value={angle}>{angle}°</option>)}
            </Select>
            <button type="button" aria-label="Tourner vers la droite" disabled={busy === 'shape'} onClick={() => void rotate(turned(rotation, ROTATION_STEP))}
              className="grid size-10 shrink-0 place-items-center rounded-xl border border-slate-200 bg-white text-ink-600 hover:bg-slate-50 disabled:opacity-50">
              <RotateCw className="size-4" aria-hidden="true" />
            </button>
          </div>
        </Field>
      )}
    </div>
  );
}

/** Les services qui attendent en cuisine pour les commandes de la table, à envoyer quand la salle est prête. */
function Courses({orders, location}: {orders: OrderSummary[]; location: LocationSummary}) {
  const {busy, run} = useAction();
  const ids = orders.map(order => order.id).join(',');
  const {data, reload} = useResource(
    signal => api<KitchenTicket[]>('/kitchen/tickets', {query: {locationId: location.id}, signal}).then(tickets => tickets.filter(ticket => ids.split(',').includes(ticket.orderId))),
    [location.id, ids],
    15_000,
  );
  const waiting = (data ?? []).flatMap(ticket => (ticket.heldCourses ?? []).map(course => ({ticket, course})));
  if (waiting.length === 0) return null;
  const next = waiting.reduce<Course | null>((first, item) => (first === null || courseOrder.indexOf(item.course) < courseOrder.indexOf(first) ? item.course : first), null)!;
  const tickets = waiting.filter(item => item.course === next).map(item => item.ticket);
  const later = [...new Set(waiting.map(item => item.course).filter(course => course !== next))];
  return (
    <div className="flex flex-wrap items-center gap-2 rounded-xl bg-sky-50 px-4 py-3">
      <p className="min-w-0 flex-1 text-sm text-sky-900">
        En attente en cuisine : {[next, ...later].map(course => courseLabel[course].toLowerCase()).join(', ')}.
      </p>
      <Button size="sm" busy={busy === 'fire'} onClick={() => run('fire', async () => {
        for (const ticket of tickets) await fireCourse(ticket.id, next);
        await reload();
      }, `${courseLabel[next]} envoyé en cuisine.`)}><Send className="size-4" aria-hidden="true" />{fireLabel[next]}</Button>
    </div>
  );
}

const courseOrder: Course[] = ['Direct', 'Starter', 'Main', 'Dessert'];

function AreaEditor({area, onChanged}: {area: DiningArea; onChanged: () => Promise<void>}) {
  const {busy, run} = useAction();
  const [confirming, setConfirming] = useState(false);
  const occupied = area.tables.some(table => table.openSessionId);
  const [label, setLabel] = useState('');
  const [capacity, setCapacity] = useState('4');
  const [name, setName] = useState(area.name);
  return (
    <div className="mt-4 space-y-3 rounded-xl border border-dashed border-slate-300 p-4">
      <form className="flex flex-wrap items-end gap-2" onSubmit={event => {
        event.preventDefault();
        void run('rename', async () => {
          await put(`/floor/dining-areas/${area.id}/name`, {name: name.trim()});
          await onChanged();
        }, 'Salle renommée.');
      }}>
        <Field label="Nom de la salle" className="min-w-40 flex-1"><Input required maxLength={60} value={name} onChange={event => setName(event.target.value)} /></Field>
        <Button type="submit" variant="secondary" busy={busy === 'rename'}>Renommer</Button>
      </form>
      <form className="flex flex-wrap items-end gap-2" onSubmit={event => {
        event.preventDefault();
        void run('add', async () => {
          await post(`/floor/dining-areas/${area.id}/tables`, {label: label.trim(), capacity: Number(capacity) || 1});
          setLabel('');
          await onChanged();
        }, 'Table ajoutée.');
      }}>
        <Field label="Nouvelle table" className="w-32"><Input required maxLength={10} value={label} onChange={event => setLabel(event.target.value)} placeholder="T12" /></Field>
        <Field label="Places" className="w-24"><Input type="number" min={1} max={99} value={capacity} onChange={event => setCapacity(event.target.value)} /></Field>
        <Button type="submit" busy={busy === 'add'}><Plus className="size-4" aria-hidden="true" />Ajouter</Button>
      </form>
      <div className="flex flex-wrap items-center gap-2 border-t border-slate-200 pt-3">
        {!confirming ? (
          <Button variant="ghost" disabled={occupied} title={occupied ? 'Libérez d’abord les tables occupées' : undefined} onClick={() => setConfirming(true)}>
            <Trash2 className="size-4" aria-hidden="true" />Supprimer la salle
          </Button>
        ) : (
          <>
            <span className="text-sm text-ink-700">Supprimer {area.name} et ses {area.tables.length} table{area.tables.length > 1 ? 's' : ''} ?</span>
            <Button variant="danger" busy={busy === 'delete'} onClick={() => run('delete', async () => {
              await del(`/floor/dining-areas/${area.id}`);
              await onChanged();
            }, 'Salle supprimée.')}>Confirmer la suppression</Button>
            <Button variant="ghost" onClick={() => setConfirming(false)}>Annuler</Button>
          </>
        )}
      </div>
    </div>
  );
}

function FloorPlan({location}: {location: LocationSummary}) {
  const {can, tenant} = useConsole();
  const now = useNow(30_000);
  const {busy, run} = useAction();
  const [editing, setEditing] = useState(false);
  const [selected, setSelected] = useState<{areaId: string; tableId: string}>();
  const [areaName, setAreaName] = useState('');
  const [views, setViews] = useState<Record<string, 'plan' | 'grid'>>({});
  const [areaId, setAreaId] = useState<string>();
  const {data, error, reload} = useResource(
    async signal => {
      const [areas, sessions, orders] = await Promise.all([
        api<DiningArea[]>('/floor/dining-areas', {query: {locationId: location.id}, signal}),
        api<TableSession[]>('/floor/table-sessions', {query: {locationId: location.id}, signal}),
        can('orders.view') || can('orders.take')
          ? api<Page<OrderSummary>>('/orders', {query: {locationId: location.id, pageSize: 100}, signal}).then(page => page.items).catch(() => [])
          : Promise.resolve([] as OrderSummary[]),
      ]);
      // Montant en cours par table : les commandes non annulées de sa session ouverte.
      const open = new Set(sessions.map(session => session.id));
      const totals = new Map<string, number>();
      orders.filter(order => order.tableSessionId && open.has(order.tableSessionId) && order.status !== 'Cancelled')
        .forEach(order => totals.set(order.tableSessionId!, (totals.get(order.tableSessionId!) ?? 0) + order.total));
      return {areas, sessions: new Map(sessions.map(session => [session.id, session])), totals};
    },
    [location.id],
    15_000,
  );

  const current = (() => {
    if (!selected || !data) return undefined;
    const area = data.areas.find(item => item.id === selected.areaId);
    const table = area?.tables.find(item => item.id === selected.tableId);
    if (!area || !table) return undefined;
    return {area, table, session: table.openSessionId ? data.sessions.get(table.openSessionId) : undefined};
  })();

  if (error) return <Alert>{error}</Alert>;
  if (!data) return <Spinner />;
  const tables = data.areas.flatMap(area => area.tables);
  const viewOf = (area: DiningArea) => views[area.id] ?? 'plan';
  const shownAreas = data.areas.length > 1 && !editing ? data.areas.filter(area => area.id === (areaId ?? data.areas[0].id)) : data.areas;
  const busyIn = (area: DiningArea) => area.tables.filter(table => table.openSessionId).length;
  const occupied = tables.filter(table => table.openSessionId).length;

  return (
    <>
      <PageHeader
        title="Salle"
        description={`${occupied} table${occupied > 1 ? 's' : ''} occupée${occupied > 1 ? 's' : ''} sur ${tables.filter(table => table.isInService).length} en service.`}
        actions={
          <div className="flex flex-wrap gap-2">
            {tables.length > 0 && <Link href="/salle/qr" className="inline-flex h-10 items-center gap-2 rounded-xl border border-slate-300 bg-white px-3 text-sm font-semibold text-ink-800 hover:bg-slate-50"><QrCode className="size-4" aria-hidden="true" />QR codes</Link>}
            {can('floor.manage') && <Button variant={editing ? 'primary' : 'secondary'} onClick={() => setEditing(v => !v)}><Pencil className="size-4" aria-hidden="true" />{editing ? 'Terminer' : 'Modifier le plan'}</Button>}
          </div>
        }
      />
      <div className="mb-5 flex flex-wrap gap-4 text-sm text-ink-600">
        <span className="flex items-center gap-2"><span className="size-3 rounded bg-white ring-1 ring-slate-300" />Libre</span>
        <span className="flex items-center gap-2"><span className="size-3 rounded bg-brand-100 ring-1 ring-brand-300" />Occupée</span>
        <span className="flex items-center gap-2"><span className="size-3 rounded bg-amber-500" />Plus d’1 h 30</span>
        <span className="flex items-center gap-2"><span className="size-3 rounded bg-slate-300" />Hors service</span>
      </div>

      {data.areas.length === 0 && !editing && (
        <Empty icon={<Store className="size-8" />} title="Aucune salle">
          {can('floor.manage') ? 'Cliquez sur « Modifier le plan » pour créer vos salles et vos tables.' : 'Le gérant n’a pas encore créé le plan de salle.'}
        </Empty>
      )}

      <div className="space-y-6">
        {shownAreas.map(area => (
          <Card key={area.id} className="p-5">
            <div className="flex flex-wrap items-center gap-2">
              <h2 className="min-w-0 flex-1 font-semibold">{area.name} <span className="font-normal text-ink-500">· {area.seatsInService} places</span></h2>
              {area.tables.length > 0 && (
                <div className="flex rounded-lg bg-slate-100 p-0.5" role="tablist" aria-label={`Affichage de ${area.name}`}>
                  {([['plan', 'Plan', MapIcon], ['grid', 'Liste', LayoutGrid]] as const).map(([value, text, Icon]) => (
                    <button key={value} type="button" role="tab" aria-selected={viewOf(area) === value} onClick={() => setViews(current => ({...current, [area.id]: value}))}
                      className={cx('flex items-center gap-1 rounded-md px-2.5 py-1 text-xs font-semibold', viewOf(area) === value ? 'bg-white shadow-sm' : 'text-ink-500')}>
                      <Icon className="size-3.5" aria-hidden="true" />{text}
                    </button>
                  ))}
                </div>
              )}
            </div>
            {viewOf(area) === 'plan' ? (
              <div className="mt-4">
                <DrawnPlan area={area} sessions={data.sessions} totals={data.totals} currency={tenant.defaultCurrencyCode} now={now} canArrange={can('floor.manage')}
                  onSelect={tableId => setSelected({areaId: area.id, tableId})} onChanged={reload} />
              </div>
            ) : (
            <div className="mt-4 grid grid-cols-[repeat(auto-fill,minmax(7.5rem,1fr))] gap-3">
              {area.tables.map(table => {
                const session = table.openSessionId ? data.sessions.get(table.openSessionId) : undefined;
                return (
                  <div key={table.id} className="relative">
                    <button
                      type="button"
                      onClick={() => setSelected({areaId: area.id, tableId: table.id})}
                      className={cx(
                        'flex aspect-[4/3] w-full flex-col items-center justify-center rounded-xl p-2 text-center transition',
                        !table.isInService ? 'bg-slate-200 text-ink-500' : session ? 'bg-brand-600 text-white shadow-md hover:bg-brand-700' : 'bg-white ring-1 ring-slate-300 hover:ring-brand-400',
                      )}>
                      <span className="text-lg font-bold">{table.label}</span>
                      <span className={cx('flex items-center gap-1 text-xs', session ? 'text-brand-100' : 'text-ink-500')}>
                        <Users className="size-3" aria-hidden="true" />
                        {session ? `${session.guestCount}/${table.capacity}` : table.capacity}
                      </span>
                      {session && <span className="text-xs text-brand-100">{elapsed(session.openedAtUtc, now)}</span>}
                    </button>
                    {editing && !session && (
                      <button type="button" aria-label={`Supprimer la table ${table.label}`} disabled={!!busy}
                        onClick={() => run(table.id, async () => {
                          await del(`/floor/dining-areas/${area.id}/tables/${table.id}`);
                          await reload();
                        }, 'Table supprimée.')}
                        className="absolute -top-2 -right-2 grid size-7 place-items-center rounded-full bg-white text-red-600 shadow ring-1 ring-slate-200">
                        <Trash2 className="size-3.5" />
                      </button>
                    )}
                  </div>
                );
              })}
              {area.tables.length === 0 && <p className="text-sm text-ink-500">Aucune table.</p>}
            </div>
            )}
            {data.areas.length > 1 && !editing && (
              <div className="mt-4 flex justify-center">
                <div role="tablist" aria-label="Salles" className="inline-flex max-w-full gap-1 overflow-x-auto rounded-full bg-slate-100 p-1">
                  {data.areas.map(item => {
                    const active = item.id === area.id;
                    return (
                      <button key={item.id} type="button" role="tab" aria-selected={active} onClick={() => setAreaId(item.id)}
                        className={cx('flex shrink-0 items-center gap-2 rounded-full px-4 py-1.5 text-sm font-semibold transition', active ? 'bg-ink-900 text-white shadow' : 'text-ink-600 hover:text-ink-900')}>
                        {busyIn(item) > 0 && <span className={cx('size-1.5 rounded-full', active ? 'bg-brand-300' : 'bg-brand-500')} aria-hidden="true" />}
                        {item.name}
                        {busyIn(item) > 0 && <span className="sr-only">, {busyIn(item)} occupée(s)</span>}
                      </button>
                    );
                  })}
                </div>
              </div>
            )}
            {editing && <AreaEditor area={area} onChanged={reload} />}
          </Card>
        ))}
        {editing && (
          <form className="flex flex-wrap items-end gap-2" onSubmit={event => {
            event.preventDefault();
            void run('area', async () => {
              await post('/floor/dining-areas', {locationId: location.id, name: areaName.trim()});
              setAreaName('');
              await reload();
            }, 'Salle créée.');
          }}>
            <Field label="Nouvelle salle" className="min-w-48 flex-1 sm:max-w-sm"><Input required maxLength={60} value={areaName} onChange={event => setAreaName(event.target.value)} placeholder="Terrasse, Salle principale…" /></Field>
            <Button type="submit" busy={busy === 'area'}><Plus className="size-4" aria-hidden="true" />Créer la salle</Button>
          </form>
        )}
      </div>
      {current && <TableDialog key={`${current.table.id}-${current.session?.id ?? ''}`} selected={current} areas={data.areas} location={location} onClose={() => setSelected(undefined)} onChanged={reload} />}
    </>
  );
}

export default function FloorPage() {
  return <WithLocation>{location => <FloorPlan location={location} />}</WithLocation>;
}
