'use client';
import {useState} from 'react';
import {ChevronDown, ChevronUp, Plus, Trash2, X} from 'lucide-react';
import {all, api, del, post, put} from '@/lib/api';
import {dateTime, menuStatusLabel, money} from '@/lib/format';
import {useResource} from '@/lib/hooks';
import type {LocationSummary, Menu, MenuSummary, ProductSummary} from '@/lib/types';
import {useConsole} from '@/components/console';
import {Alert, Badge, Button, Card, Empty, Field, Input, Modal, Select, Spinner, useAction} from '@/components/ui';
import {InlineEdit, nameOf} from './inline-edit';
import {MenuSchedule} from './schedule';

function MenuEditor({id, onClose, onChanged}: {id: string; onClose: () => void; onChanged: () => Promise<void>}) {
  const {can} = useConsole();
  const {busy, run} = useAction();
  const [sectionName, setSectionName] = useState('');
  const [picks, setPicks] = useState<Record<string, string>>({});
  const {data, error, reload} = useResource(async signal => {
    const [menu, products] = await Promise.all([api<Menu>(`/catalog/menus/${id}`, {signal}), all<ProductSummary>('/catalog/products')]);
    return {menu, products: products.filter(product => product.status !== 'Archived')};
  }, [id]);
  const base = `/catalog/menus/${id}`;
  const changed = async () => {
    await reload();
    await onChanged();
  };
  const act = (key: string, action: () => Promise<unknown>, success?: string) => run(key, async () => {
    await action();
    await changed();
  }, success);

  if (!data) return <Modal open onClose={onClose} title="Carte" wide>{error ? <Alert>{error}</Alert> : <Spinner />}</Modal>;
  const {menu, products} = data;
  const byId = new Map(products.map(product => [product.id, product]));
  const editable = can('catalog.manage') && menu.status === 'Draft';

  return (
    <Modal open onClose={onClose} wide title={menu.name} footer={can('catalog.manage') && (
      <>
        {menu.status === 'Draft' && <Button busy={busy === 'publish'} disabled={menu.sections.every(section => section.productIds.length === 0)} onClick={() => act('publish', () => post(`${base}/publish`), 'Carte publiée : elle est proposée à la prise de commande.')}>Publier</Button>}
        {menu.status === 'Published' && <Button variant="secondary" busy={busy === 'unpublish'} onClick={() => act('unpublish', () => post(`${base}/unpublish`), 'Carte repassée en brouillon.')}>Dépublier pour modifier</Button>}
        {menu.status !== 'Archived' && <Button variant="ghost" className="text-red-600" busy={busy === 'archive'} onClick={() => act('archive', () => post(`${base}/archive`), 'Carte archivée.')}>Archiver</Button>}
      </>
    )}>
      <div className="space-y-5">
        {editable && (
          <div className="text-sm">
            <span className="mr-2 text-ink-500">Nom de la carte :</span>
            <InlineEdit value={menu.name} label="Nom de la carte" editable maxLength={120} parse={nameOf(120)} success="Carte renommée." className="font-semibold"
              onSave={async (value: string) => {
                await put(`${base}/name`, {name: value});
                await changed();
              }} />
          </div>
        )}
        <div className="flex flex-wrap items-center gap-2 text-sm">
          <Badge tone={menu.status === 'Published' ? 'green' : 'neutral'}>{menuStatusLabel[menu.status]}</Badge>
          {menu.publishedAtUtc && <span className="text-ink-500">Publiée le {dateTime(menu.publishedAtUtc)}</span>}
        </div>
        <MenuSchedule key={JSON.stringify(menu.slots ?? [])} menuId={menu.id} slots={menu.slots ?? []} editable={can('catalog.manage') && menu.status !== 'Archived'} onChanged={changed} />
        {menu.status === 'Published' && <Alert tone="blue">Une carte publiée ne se modifie pas (nom, sections, ordre des produits) : dépubliez-la, modifiez-la, puis republiez-la. Les prix se changent dans chaque produit, sans dépublier.</Alert>}
        {menu.sections.length === 0 && <Empty title="Aucune section">Ajoutez des sections (Entrées, Plats, Boissons…) puis leurs produits.</Empty>}
        {menu.sections.map((section, index) => {
          const available = products.filter(product => !section.productIds.includes(product.id)
            && (!product.locationIds?.length || product.locationIds.includes(menu.locationId)));
          return (
            <Card key={section.id} className="p-4">
              <div className="flex items-center justify-between gap-2">
                <div className="min-w-0 font-semibold">
                  <InlineEdit value={section.name} label={`Nom de la section ${section.name}`} editable={editable} maxLength={120} parse={nameOf(120)} success="Section renommée."
                    onSave={async (value: string) => {
                      await put(`${base}/sections/${section.id}/name`, {name: value});
                      await changed();
                    }} />
                </div>
                {editable && (
                  <span className="flex gap-1">
                    <Button size="sm" variant="ghost" aria-label={`Monter la section ${section.name}`} disabled={index === 0 || !!busy} onClick={() => act(`up-${section.id}`, () => put(`${base}/sections/${section.id}/position`, {position: index - 1}))}><ChevronUp className="size-4" /></Button>
                    <Button size="sm" variant="ghost" aria-label={`Descendre la section ${section.name}`} disabled={index === menu.sections.length - 1 || !!busy} onClick={() => act(`down-${section.id}`, () => put(`${base}/sections/${section.id}/position`, {position: index + 1}))}><ChevronDown className="size-4" /></Button>
                    <Button size="sm" variant="ghost" className="text-red-600" aria-label={`Supprimer la section ${section.name}`} busy={busy === `ds-${section.id}`} onClick={() => act(`ds-${section.id}`, () => del(`${base}/sections/${section.id}`))}><Trash2 className="size-4" /></Button>
                  </span>
                )}
              </div>
              <ul className="mt-2 divide-y divide-slate-100">
                {section.productIds.map((productId, position) => {
                  const product = byId.get(productId);
                  const move = (to: number) => act(`mv-${section.id}-${productId}`, () => put(`${base}/sections/${section.id}/products/${productId}/position`, {position: to}));
                  return (
                    <li key={productId} className="flex items-center justify-between gap-2 py-2 text-sm">
                      <span className="min-w-0 truncate">{product?.name ?? 'Produit archivé'}{product && product.status !== 'Active' && <Badge tone="amber" className="ml-2">Pas en vente</Badge>}</span>
                      <span className="flex items-center gap-1">
                        {product && <span className="mr-1 text-ink-600">{money(product.basePrice, product.currencyCode)}</span>}
                        {editable && section.productIds.length > 1 && (
                          <>
                            <button type="button" className="rounded p-1 text-ink-500 hover:bg-slate-100 hover:text-ink-800 disabled:opacity-30" aria-label={`Monter ${product?.name ?? 'le produit'}`}
                              disabled={position === 0 || !!busy} onClick={() => move(position - 1)}><ChevronUp className="size-4" /></button>
                            <button type="button" className="rounded p-1 text-ink-500 hover:bg-slate-100 hover:text-ink-800 disabled:opacity-30" aria-label={`Descendre ${product?.name ?? 'le produit'}`}
                              disabled={position === section.productIds.length - 1 || !!busy} onClick={() => move(position + 1)}><ChevronDown className="size-4" /></button>
                          </>
                        )}
                        {editable && <button type="button" className="rounded p-1 text-ink-500 hover:bg-slate-100 hover:text-red-600" aria-label="Retirer de la section" onClick={() => act(`rp-${productId}`, () => del(`${base}/sections/${section.id}/products/${productId}`))}><X className="size-4" /></button>}
                      </span>
                    </li>
                  );
                })}
                {section.productIds.length === 0 && <li className="py-2 text-sm text-ink-500">Section vide.</li>}
              </ul>
              {editable && available.length > 0 && (
                <form className="mt-3 flex gap-2" onSubmit={event => {
                  event.preventDefault();
                  const productId = picks[section.id] || available[0].id;
                  void act(`ap-${section.id}`, () => post(`${base}/sections/${section.id}/products`, {productId}));
                }}>
                  <Select aria-label="Produit à ajouter" value={picks[section.id] || available[0].id} onChange={event => setPicks(p => ({...p, [section.id]: event.target.value}))}>
                    {available.map(product => <option key={product.id} value={product.id}>{product.name}</option>)}
                  </Select>
                  <Button type="submit" variant="secondary" busy={busy === `ap-${section.id}`}><Plus className="size-4" aria-hidden="true" />Ajouter</Button>
                </form>
              )}
            </Card>
          );
        })}
        {editable && (
          <form className="flex items-end gap-2" onSubmit={event => {
            event.preventDefault();
            void act('section', async () => {
              await post(`${base}/sections`, {name: sectionName.trim()});
              setSectionName('');
            });
          }}>
            <Field label="Nouvelle section" className="flex-1"><Input required maxLength={60} value={sectionName} onChange={event => setSectionName(event.target.value)} placeholder="Plats" /></Field>
            <Button type="submit" busy={busy === 'section'}><Plus className="size-4" aria-hidden="true" />Ajouter</Button>
          </form>
        )}
      </div>
    </Modal>
  );
}

export function Menus({location}: {location: LocationSummary}) {
  const {can} = useConsole();
  const {busy, run} = useAction();
  const [name, setName] = useState('');
  const [editing, setEditing] = useState<string>();
  const {data, error, reload} = useResource(signal => api<MenuSummary[]>('/catalog/menus', {query: {locationId: location.id}, signal}), [location.id]);

  if (error) return <Alert>{error}</Alert>;
  if (!data) return <Spinner />;
  const visible = data.filter(menu => menu.status !== 'Archived');
  return (
    <>
      <p className="mb-4 text-sm text-ink-600">Les cartes publiées de <strong>{location.name}</strong> sont proposées à la prise de commande, section par section.</p>
      {visible.length === 0 && <Empty title="Aucune carte">Créez une carte, ajoutez-y des sections et des produits, puis publiez-la.</Empty>}
      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
        {visible.map(menu => (
          <button key={menu.id} type="button" onClick={() => setEditing(menu.id)} className="rounded-2xl border border-slate-200 bg-white p-5 text-left transition hover:border-brand-300 hover:shadow-md">
            <span className="flex items-center justify-between gap-2">
              <span className="truncate font-semibold">{menu.name}</span>
              <Badge tone={menu.status === 'Published' ? 'green' : 'neutral'}>{menuStatusLabel[menu.status]}</Badge>
            </span>
            <span className="mt-1 block text-sm text-ink-500">{menu.sectionCount} section{menu.sectionCount > 1 ? 's' : ''}</span>
          </button>
        ))}
      </div>
      {can('catalog.manage') && (
        <form className="mt-6 flex max-w-md items-end gap-2" onSubmit={event => {
          event.preventDefault();
          void run('create', async () => {
            const created = await post<{id: string}>('/catalog/menus', {locationId: location.id, name: name.trim()});
            setName('');
            await reload();
            setEditing(created.id);
          }, 'Carte créée.');
        }}>
          <Field label="Nouvelle carte" className="flex-1"><Input required maxLength={80} value={name} onChange={event => setName(event.target.value)} placeholder="Carte du midi" /></Field>
          <Button type="submit" busy={busy === 'create'}><Plus className="size-4" aria-hidden="true" />Créer</Button>
        </form>
      )}
      {editing && <MenuEditor id={editing} onClose={() => setEditing(undefined)} onChanged={reload} />}
    </>
  );
}
