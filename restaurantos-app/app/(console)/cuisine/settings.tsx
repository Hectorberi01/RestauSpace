'use client';
import {useMemo, useState} from 'react';
import {Check, Pencil, Search, X} from 'lucide-react';
import {describe, post, put} from '@/lib/api';
import {useResource} from '@/lib/hooks';
import {courseLabel, loadRouting, renameStation, routableProducts, routeProduct, setProductCourse} from '@/lib/kitchen';
import type {Course, LocationSummary, Station, StationRouting} from '@/lib/types';
import {Alert, Badge, Button, Empty, Field, Input, Modal, Select, Spinner, cx, useAction, useToast} from '@/components/ui';

type Tab = 'stations' | 'routes';

function StationRow({station, routing, used, onChanged}: {station: Station; routing: StationRouting | null; used: number; onChanged: () => Promise<void>}) {
  const {busy, run} = useAction();
  const [editing, setEditing] = useState(false);
  const [name, setName] = useState(station.name);
  const isDefault = routing?.defaultStationId === station.id;

  if (editing) {
    return (
      <li className="px-4 py-3">
        <form className="flex items-center gap-2" onSubmit={event => {
          event.preventDefault();
          void run('rename', async () => {
            await renameStation(station.id, name.trim());
            setEditing(false);
            await onChanged();
          }, 'Poste renommé.');
        }}>
          <Input autoFocus required maxLength={60} aria-label="Nom du poste" value={name} onChange={event => setName(event.target.value)} className="min-w-0 flex-1" />
          <Button type="submit" size="sm" busy={busy === 'rename'} aria-label="Enregistrer"><Check className="size-4" /></Button>
          <Button type="button" size="sm" variant="ghost" aria-label="Annuler" onClick={() => {
            setName(station.name);
            setEditing(false);
          }}><X className="size-4" /></Button>
        </form>
      </li>
    );
  }

  return (
    <li className="flex flex-wrap items-center gap-2 px-4 py-3">
      <div className="min-w-0 flex-1">
        <p className="flex flex-wrap items-center gap-1.5 font-medium">
          <span className="truncate">{station.name}</span>
          {isDefault && <Badge tone="brand">Par défaut</Badge>}
          {!station.isActive && <Badge>Inactif</Badge>}
        </p>
        <p className="text-xs text-ink-500">{isDefault ? 'Reçoit tous les plats non affectés' : used > 0 ? `${used} plat${used > 1 ? 's' : ''} affecté${used > 1 ? 's' : ''}` : 'Aucun plat affecté'}</p>
      </div>
      <span className="flex shrink-0 gap-1">
        <Button size="sm" variant="ghost" aria-label={`Renommer ${station.name}`} title="Renommer" onClick={() => setEditing(true)}><Pencil className="size-4" /></Button>
        {station.isActive && routing && !isDefault && (
          <Button size="sm" variant="ghost" busy={busy === 'default'} onClick={() => run('default', async () => {
            await put(`/kitchen/routings/${station.locationId}/default-station`, {stationId: station.id});
            await onChanged();
          }, 'Poste par défaut enregistré.')}>Par défaut</Button>
        )}
        {!isDefault && (
          <Button size="sm" variant="ghost" busy={busy === 'toggle'} disabled={station.isActive && used > 0}
            title={station.isActive && used > 0 ? 'Affectez d’abord ses plats à un autre poste' : undefined}
            onClick={() => run('toggle', async () => {
              await post(`/kitchen/stations/${station.id}/${station.isActive ? 'deactivate' : 'activate'}`);
              await onChanged();
            }, station.isActive ? 'Poste désactivé.' : 'Poste activé.')}>{station.isActive ? 'Désactiver' : 'Activer'}</Button>
        )}
      </span>
    </li>
  );
}

function Routes({location, stations, routing, onChanged}: {location: LocationSummary; stations: Station[]; routing: StationRouting; onChanged: () => Promise<void>}) {
  const toast = useToast();
  const [query, setQuery] = useState('');
  const [pending, setPending] = useState<Set<string>>(new Set());
  const [local, setLocal] = useState<Map<string, string>>(() => new Map(routing.routes.map(route => [route.productId, route.stationId])));
  const [courses, setCourses] = useState<Map<string, Course>>(() => new Map((routing.courses ?? []).map(item => [item.productId, item.course])));

  const changeCourse = async (productId: string, course: Course) => {
    setPending(current => new Set([...current, productId]));
    try {
      await setProductCourse(location.id, productId, course);
      setCourses(current => new Map(current).set(productId, course));
    } catch (error) {
      toast('error', describe(error));
    } finally {
      setPending(current => {
        const next = new Set(current);
        next.delete(productId);
        return next;
      });
    }
  };
  const sections = useResource(signal => routableProducts(location.id, signal), [location.id]);
  const active = stations.filter(station => station.isActive);
  const fallback = stations.find(station => station.id === routing.defaultStationId);
  const nameOf = (id: string) => stations.find(station => station.id === id)?.name ?? 'Poste inconnu';

  const filtered = useMemo(() => {
    const needle = query.trim().toLocaleLowerCase('fr');
    return (sections.data ?? [])
      .map(section => ({...section, products: needle ? section.products.filter(product => product.name.toLocaleLowerCase('fr').includes(needle)) : section.products}))
      .filter(section => section.products.length > 0);
  }, [sections.data, query]);

  const counts = useMemo(() => {
    const map = new Map<string, number>();
    local.forEach(stationId => map.set(stationId, (map.get(stationId) ?? 0) + 1));
    return map;
  }, [local]);

  /** Applique les changements un par un : chaque plat garde son ancien poste si son appel échoue. */
  const apply = async (changes: {productId: string; stationId: string | null}[]) => {
    const todo = changes.filter(change => (local.get(change.productId) ?? null) !== change.stationId);
    if (todo.length === 0) return;
    setPending(current => new Set([...current, ...todo.map(change => change.productId)]));
    let failed = 0;
    for (const change of todo) {
      try {
        await routeProduct(location.id, change.productId, change.stationId);
        setLocal(current => {
          const next = new Map(current);
          if (change.stationId) next.set(change.productId, change.stationId);
          else next.delete(change.productId);
          return next;
        });
      } catch (error) {
        failed += 1;
        if (failed === 1) toast('error', describe(error));
      } finally {
        setPending(current => {
          const next = new Set(current);
          next.delete(change.productId);
          return next;
        });
      }
    }
    if (todo.length > 1 && failed === 0) toast('success', `${todo.length} plats affectés.`);
    await onChanged();
  };

  if (sections.error) return <Alert>{sections.error}</Alert>;
  if (!sections.data) return <Spinner />;
  if (sections.data.length === 0) return <Empty title="Aucun plat">Créez d’abord vos produits dans Menu & produits.</Empty>;

  const options = (
    <>
      <option value="">Par défaut{fallback ? ` (${fallback.name})` : ''}</option>
      {active.filter(station => station.id !== routing.defaultStationId).map(station => <option key={station.id} value={station.id}>{station.name}</option>)}
    </>
  );

  return (
    <div className="space-y-4">
      <p className="text-sm text-ink-600">
        Choisissez le poste qui prépare chaque plat. Un plat non affecté part au poste par défaut{fallback ? <> : <strong>{fallback.name}</strong></> : ''}.
        Les tickets déjà envoyés ne changent pas.
      </p>
      <p className="text-sm text-ink-600">
        Le service (entrée, plat, dessert) règle l’envoi à table : seul le premier service part en cuisine, les suivants attendent que la salle les envoie.
      </p>
      {active.length < 2 && <Alert tone="blue">Créez au moins un deuxième poste actif (onglet Postes) pour répartir les plats.</Alert>}
      <div className="flex flex-wrap gap-2 text-xs">
        {active.map(station => (
          <span key={station.id} className="rounded-full bg-slate-100 px-2.5 py-1 text-ink-700">
            {station.name} : {station.id === routing.defaultStationId ? 'reste de la carte' : `${counts.get(station.id) ?? 0} plat${(counts.get(station.id) ?? 0) > 1 ? 's' : ''}`}
          </span>
        ))}
      </div>
      <label className="relative block">
        <span className="sr-only">Rechercher un plat</span>
        <Search className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-ink-500" aria-hidden="true" />
        <Input value={query} onChange={event => setQuery(event.target.value)} placeholder="Rechercher un plat…" className="pl-9" />
      </label>
      {filtered.length === 0 && <p className="text-sm text-ink-500">Aucun plat ne correspond.</p>}
      <div className="max-h-[55vh] space-y-4 overflow-y-auto pr-1">
        {filtered.map(section => (
          <section key={section.key} className="rounded-xl border border-slate-200">
            <header className="flex flex-wrap items-center gap-2 border-b border-slate-100 bg-slate-50 px-4 py-2">
              <h3 className="min-w-0 flex-1 text-sm font-semibold">{section.name} <span className="font-normal text-ink-500">· {section.products.length}</span></h3>
              {active.length > 1 && (
                <Select aria-label={`Envoyer toute la section ${section.name} vers`} className="h-8 w-full! py-0 text-xs sm:w-auto!" value="__"
                  disabled={section.products.some(product => pending.has(product.id))}
                  onChange={event => {
                    const value = event.target.value;
                    if (value === '__') return;
                    void apply(section.products.map(product => ({productId: product.id, stationId: value || null})));
                  }}>
                  <option value="__" disabled>Tout envoyer vers…</option>
                  {options}
                </Select>
              )}
            </header>
            <ul className="divide-y divide-slate-100">
              {section.products.map(product => {
                const current = local.get(product.id) ?? '';
                const orphan = current !== '' && (current === routing.defaultStationId || !active.some(station => station.id === current));
                return (
                  <li key={product.id} className="flex flex-wrap items-center gap-2 px-4 py-2">
                    <span className={cx('min-w-0 flex-1 truncate text-sm', product.status !== 'Active' && 'text-ink-500')}>
                      {product.name}
                      {product.status === 'Draft' && <span className="ml-1 text-xs">(brouillon)</span>}
                      {product.status === 'Unavailable' && <span className="ml-1 text-xs">(indisponible)</span>}
                    </span>
                    <Select aria-label={`Poste pour ${product.name}`} className={cx('h-9 w-full! py-0 text-sm sm:w-48!', current && 'font-semibold text-brand-700')}
                      value={current} disabled={pending.has(product.id) || active.length < 2}
                      onChange={event => void apply([{productId: product.id, stationId: event.target.value || null}])}>
                      {orphan && <option value={current}>{nameOf(current)}{current === routing.defaultStationId ? '' : ' (inactif)'}</option>}
                      {options}
                    </Select>
                    <Select aria-label={`Service pour ${product.name}`} className={cx('h-9 w-full! py-0 text-sm sm:w-36!', (courses.get(product.id) ?? 'Direct') !== 'Direct' && 'font-semibold text-brand-700')}
                      value={courses.get(product.id) ?? 'Direct'} disabled={pending.has(product.id)}
                      onChange={event => void changeCourse(product.id, event.target.value as Course)}>
                      {(Object.keys(courseLabel) as Course[]).map(course => <option key={course} value={course}>{courseLabel[course]}</option>)}
                    </Select>
                  </li>
                );
              })}
            </ul>
          </section>
        ))}
      </div>
    </div>
  );
}

export function KitchenSettings({location, stations, onClose, onChanged}: {location: LocationSummary; stations: Station[]; onClose: () => void; onChanged: () => Promise<void>}) {
  const {busy, run} = useAction();
  const [tab, setTab] = useState<Tab>('stations');
  const [name, setName] = useState('');
  const {data: routing, reload, loading} = useResource(signal => loadRouting(location.id, signal).catch(() => null), [location.id]);
  const changed = async () => {
    await onChanged();
    await reload();
  };
  const used = (stationId: string) => routing?.routes.filter(route => route.stationId === stationId).length ?? 0;
  const firstActive = stations.find(station => station.isActive);

  return (
    <Modal wide open onClose={onClose} title="Postes de cuisine">
      <div className="space-y-5">
        <div role="tablist" aria-label="Réglages cuisine" className="flex gap-1 border-b border-slate-200">
          {([['stations', 'Postes'], ['routes', 'Affectation des plats']] as [Tab, string][]).map(([value, label]) => (
            <button key={value} type="button" role="tab" aria-selected={tab === value} onClick={() => setTab(value)}
              className={cx('-mb-px border-b-2 px-3 py-2 text-sm font-semibold', tab === value ? 'border-brand-600 text-brand-700' : 'border-transparent text-ink-500 hover:text-ink-800')}>{label}</button>
          ))}
        </div>

        {tab === 'stations' ? (
          <>
            <p className="text-sm text-ink-600">Un poste reçoit les plats qui lui sont affectés ; le poste par défaut reçoit tous les autres.</p>
            <ul className="divide-y divide-slate-100 rounded-xl border border-slate-200">
              {stations.length === 0 && <li className="px-4 py-3 text-sm text-ink-500">Aucun poste.</li>}
              {stations.map(station => <StationRow key={station.id} station={station} routing={routing ?? null} used={used(station.id)} onChanged={changed} />)}
            </ul>
            {!loading && !routing && firstActive && (
              <Alert tone="amber">
                Aucun poste par défaut : les commandes acceptées ne peuvent pas partir en cuisine.{' '}
                <button type="button" className="font-semibold underline" disabled={busy === 'init'} onClick={() => run('init', async () => {
                  await post(`/kitchen/routings/${location.id}`, {defaultStationId: firstActive.id});
                  await changed();
                }, 'Poste par défaut enregistré.')}>Utiliser « {firstActive.name} »</button>
              </Alert>
            )}
            <form className="flex items-end gap-2" onSubmit={event => {
              event.preventDefault();
              void run('create', async () => {
                const created = await post<{id: string}>('/kitchen/stations', {locationId: location.id, name: name.trim()});
                if (!routing && created?.id) await post(`/kitchen/routings/${location.id}`, {defaultStationId: created.id});
                setName('');
                await changed();
              }, 'Poste créé.');
            }}>
              <Field label="Nouveau poste" className="min-w-0 flex-1"><Input required maxLength={60} value={name} onChange={event => setName(event.target.value)} placeholder="Grill, Froid, Bar…" /></Field>
              <Button type="submit" busy={busy === 'create'}>Ajouter</Button>
            </form>
          </>
        ) : loading && !routing ? <Spinner /> : !routing ? (
          <Alert tone="amber">Choisissez d’abord un poste par défaut dans l’onglet Postes.</Alert>
        ) : (
          <Routes key={routing.id} location={location} stations={stations} routing={routing} onChanged={changed} />
        )}
      </div>
    </Modal>
  );
}
