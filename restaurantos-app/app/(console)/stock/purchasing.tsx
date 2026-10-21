'use client';
import {useEffect, useMemo, useState} from 'react';
import {ClipboardList, Copy, MessageCircle, PackageCheck, Pencil, Plus, Send, Trash2, Truck, X} from 'lucide-react';
import {useResource} from '@/lib/hooks';
import {
  cancelPurchaseOrder, composePurchaseOrder, createPurchaseOrder, createSupplier, listItems, listPurchaseOrders, listSuppliers, parseMoney, parseQuantity,
  purchaseStatusLabel, quantity, receivePurchaseOrder, reorderSuggestions, sendPurchaseOrder, setSupplierActive, unitShort, updateSupplier,
  type PurchaseOrder, type PurchaseStatus, type StockItem, type Supplier,
} from '@/lib/inventory';
import {dateTime, money} from '@/lib/format';
import {useConsole} from '@/components/console';
import {Alert, Badge, Button, Card, Empty, Field, Input, Modal, Select, Spinner, Textarea, cx, useAction, useToast} from '@/components/ui';

const statusTone: Record<PurchaseStatus, 'neutral' | 'blue' | 'amber' | 'green' | 'red'> = {
  Draft: 'neutral',
  Sent: 'blue',
  PartiallyReceived: 'amber',
  Received: 'green',
  Cancelled: 'red',
};

const blank = {name: '', phone: '', email: '', notes: ''};

function SupplierDialog({supplier, open, onClose, onDone}: {supplier: Supplier | null; open: boolean; onClose: () => void; onDone: () => Promise<void>}) {
  const {busy, run} = useAction();
  const [form, setForm] = useState(blank);
  useEffect(() => {
    if (!open) return;
    setForm(supplier ? {name: supplier.name, phone: supplier.phone ?? '', email: supplier.email ?? '', notes: supplier.notes ?? ''} : blank);
  }, [open, supplier]);
  const set = (key: keyof typeof blank) => (event: {target: {value: string}}) => setForm(current => ({...current, [key]: event.target.value}));

  return (
    <Modal open={open} onClose={onClose} title={supplier ? supplier.name : 'Nouveau fournisseur'} footer={
      <>
        <Button variant="secondary" onClick={onClose}>Annuler</Button>
        <Button type="submit" form="supplier" busy={busy === 'save'} disabled={!form.name.trim()}>Enregistrer</Button>
      </>
    }>
      <form id="supplier" className="space-y-4" onSubmit={event => {
        event.preventDefault();
        const input = {name: form.name.trim(), phone: form.phone.trim() || null, email: form.email.trim() || null, notes: form.notes.trim() || null};
        void run('save', async () => {
          if (supplier) await updateSupplier(supplier.id, input);
          else await createSupplier(input);
          await onDone();
          onClose();
        }, 'Fournisseur enregistré.');
      }}>
        <Field label="Nom"><Input required maxLength={100} value={form.name} onChange={set('name')} placeholder="Grossiste du marché" /></Field>
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
          <Field label="Téléphone / WhatsApp"><Input type="tel" maxLength={40} value={form.phone} onChange={set('phone')} placeholder="+229 …" /></Field>
          <Field label="E-mail"><Input type="email" maxLength={200} value={form.email} onChange={set('email')} /></Field>
        </div>
        <Field label="Notes" hint="Jours de livraison, conditions, contact…"><Textarea maxLength={500} rows={3} value={form.notes} onChange={set('notes')} /></Field>
      </form>
    </Modal>
  );
}

export function Suppliers() {
  const {can} = useConsole();
  const {busy, run} = useAction();
  const {data, error, reload} = useResource(signal => listSuppliers(signal), []);
  const [editing, setEditing] = useState<Supplier | null>(null);
  const [open, setOpen] = useState(false);
  const manage = can('inventory.manage');

  if (error) return <Alert>{error}</Alert>;
  if (!data) return <Spinner />;

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between gap-3">
        <p className="text-sm text-ink-500">{data.filter(supplier => supplier.isActive).length} fournisseur(s) actif(s)</p>
        {manage && <Button onClick={() => {
          setEditing(null);
          setOpen(true);
        }}><Plus className="size-4" aria-hidden="true" />Nouveau fournisseur</Button>}
      </div>
      {data.length === 0 ? (
        <Card className="p-6"><Empty icon={<Truck className="size-8" />} title="Aucun fournisseur">Ajoutez vos fournisseurs pour leur adresser des bons de commande.</Empty></Card>
      ) : (
        <Card className="overflow-hidden">
          <ul className="divide-y divide-slate-100">
            {data.map(supplier => (
              <li key={supplier.id} className={cx('flex flex-wrap items-center gap-3 px-4 py-3 text-sm', !supplier.isActive && 'opacity-60')}>
                <div className="min-w-0 flex-1">
                  <p className="flex items-center gap-2 font-medium">{supplier.name}{!supplier.isActive && <Badge>Archivé</Badge>}</p>
                  <p className="truncate text-ink-500">{[supplier.phone, supplier.email, supplier.notes].filter(Boolean).join(' · ') || 'Aucune coordonnée'}</p>
                </div>
                {manage && (
                  <div className="flex gap-1">
                    <Button size="sm" variant="ghost" aria-label={`Modifier ${supplier.name}`} onClick={() => {
                      setEditing(supplier);
                      setOpen(true);
                    }}><Pencil className="size-4" /></Button>
                    <Button size="sm" variant="secondary" busy={busy === supplier.id} onClick={() => run(supplier.id, async () => {
                      await setSupplierActive(supplier.id, !supplier.isActive);
                      await reload();
                    }, supplier.isActive ? 'Fournisseur archivé.' : 'Fournisseur réactivé.')}>{supplier.isActive ? 'Archiver' : 'Réactiver'}</Button>
                  </div>
                )}
              </li>
            ))}
          </ul>
        </Card>
      )}
      <SupplierDialog supplier={editing} open={open} onClose={() => setOpen(false)} onDone={reload} />
    </div>
  );
}

interface DraftLine {
  itemId: string;
  amount: string;
  price: string;
}

function OrderDialog({order, prefill, open, items, suppliers, onClose, onDone}: {
  order: PurchaseOrder | null; prefill: DraftLine[]; open: boolean; items: StockItem[]; suppliers: Supplier[]; onClose: () => void; onDone: () => Promise<void>;
}) {
  const {location, currency} = useConsole();
  const {busy, run} = useAction();
  const [supplierId, setSupplierId] = useState('');
  const [notes, setNotes] = useState('');
  const [lines, setLines] = useState<DraftLine[]>([]);
  useEffect(() => {
    if (!open) return;
    setSupplierId(order?.supplierId ?? suppliers.find(supplier => supplier.isActive)?.id ?? '');
    setNotes(order?.notes ?? '');
    setLines(order
      ? order.lines.map(line => ({itemId: line.itemId, amount: String(line.ordered), price: line.unitPrice === null ? '' : String(line.unitPrice)}))
      : prefill.length ? prefill : [{itemId: '', amount: '', price: ''}]);
  }, [open, order, prefill, suppliers]);
  const byId = new Map(items.map(item => [item.id, item]));
  const filled = lines.filter(line => line.itemId);
  const invalid = !supplierId || filled.length === 0
    || filled.some(line => !(parseQuantity(line.amount)! > 0) || Number.isNaN(parseMoney(line.price)))
    || new Set(filled.map(line => line.itemId)).size !== filled.length;
  const total = filled.every(line => parseMoney(line.price) !== null)
    ? filled.reduce((sum, line) => sum + (parseQuantity(line.amount) ?? 0) * (parseMoney(line.price) ?? 0), 0)
    : null;
  const update = (index: number, change: Partial<DraftLine>) => setLines(current => current.map((line, at) => (at === index ? {...line, ...change} : line)));

  const save = (thenSend: boolean) => run(thenSend ? 'send' : 'save', async () => {
    const input = filled.map(line => {
      const item = byId.get(line.itemId)!;
      return {itemId: line.itemId, quantity: {amount: parseQuantity(line.amount)!, unit: item.unit}, unitPrice: parseMoney(line.price)};
    });
    let id = order?.id;
    if (id) await composePurchaseOrder(id, input, notes.trim() || null);
    else id = (await createPurchaseOrder(location!.id, supplierId, input, notes.trim() || null)).id;
    if (thenSend) await sendPurchaseOrder(id);
    await onDone();
    onClose();
  }, thenSend ? 'Bon de commande envoyé.' : 'Brouillon enregistré.');

  return (
    <Modal wide open={open} onClose={onClose} title={order ? `Bon ${order.number}` : 'Nouveau bon de commande'} footer={
      <>
        <Button variant="secondary" onClick={onClose}>Annuler</Button>
        <Button variant="secondary" busy={busy === 'save'} disabled={invalid} onClick={() => void save(false)}>Enregistrer le brouillon</Button>
        <Button busy={busy === 'send'} disabled={invalid} onClick={() => void save(true)}><Send className="size-4" aria-hidden="true" />Marquer envoyé</Button>
      </>
    }>
      <div className="space-y-4">
        <Field label="Fournisseur">
          <Select required value={supplierId} disabled={Boolean(order)} onChange={event => setSupplierId(event.target.value)}>
            <option value="">Choisir</option>
            {suppliers.filter(supplier => supplier.isActive || supplier.id === supplierId).map(supplier => <option key={supplier.id} value={supplier.id}>{supplier.name}</option>)}
          </Select>
        </Field>
        <div className="space-y-2">
          <p className="text-sm font-medium">Articles</p>
          {lines.map((line, index) => {
            const item = byId.get(line.itemId);
            return (
              <div key={index} className="grid grid-cols-[1fr_1fr_auto] items-center gap-2 rounded-xl border border-slate-200 p-2 sm:grid-cols-[1fr_7rem_8rem_auto]">
                <Select aria-label="Article" value={line.itemId} onChange={event => update(index, {itemId: event.target.value})} className="col-span-3 sm:col-span-1">
                  <option value="">Choisir un ingrédient</option>
                  {items.map(option => <option key={option.id} value={option.id}>{option.ingredientName ?? 'Ingrédient'} ({quantity(option.currentQuantity, option.unit)})</option>)}
                </Select>
                <div className="flex items-center gap-1">
                  <Input aria-label="Quantité" inputMode="decimal" value={line.amount} onChange={event => update(index, {amount: event.target.value})} placeholder="Qté" />
                  <span className="w-8 text-sm text-ink-500">{item ? unitShort(item.unit) : ''}</span>
                </div>
                <Input aria-label="Prix unitaire" inputMode="decimal" value={line.price} onChange={event => update(index, {price: event.target.value})}
                  placeholder={item?.averageUnitCost != null ? String(item.averageUnitCost) : `Prix / ${item ? unitShort(item.unit) : 'unité'}`} />
                <Button size="sm" variant="ghost" aria-label="Retirer la ligne" onClick={() => setLines(current => current.filter((_, at) => at !== index))}><X className="size-4" /></Button>
              </div>
            );
          })}
          <Button size="sm" variant="secondary" onClick={() => setLines(current => [...current, {itemId: '', amount: '', price: ''}])}><Plus className="size-4" aria-hidden="true" />Ajouter un article</Button>
        </div>
        <Field label="Note pour le fournisseur (facultatif)"><Textarea rows={2} maxLength={500} value={notes} onChange={event => setNotes(event.target.value)} placeholder="Livraison avant 10 h" /></Field>
        <p className="text-sm text-ink-600">Total estimé : <strong>{total === null ? 'prix incomplets' : money(total, currency)}</strong></p>
      </div>
    </Modal>
  );
}

function ReceiveDialog({order, onClose, onDone}: {order: PurchaseOrder | null; onClose: () => void; onDone: () => Promise<void>}) {
  const {currency} = useConsole();
  const {busy, run} = useAction();
  const [received, setReceived] = useState<Record<string, {amount: string; price: string}>>({});
  const [close, setClose] = useState(false);
  useEffect(() => {
    if (!order) return;
    setReceived(Object.fromEntries(order.lines.map(line => [line.itemId, {amount: String(Math.max(0, line.ordered - line.received)), price: line.unitPrice === null ? '' : String(line.unitPrice)}])));
    setClose(false);
  }, [order]);
  if (!order) return null;
  const entries = order.lines.map(line => ({line, amount: parseQuantity(received[line.itemId]?.amount ?? '') ?? 0, price: parseMoney(received[line.itemId]?.price ?? '')}));
  const invalid = entries.every(entry => entry.amount <= 0) || entries.some(entry => entry.amount < 0 || Number.isNaN(entry.price));
  const partial = entries.some(entry => entry.line.received + entry.amount < entry.line.ordered);

  return (
    <Modal wide open onClose={onClose} title={`Réception du bon ${order.number}`} footer={
      <>
        <Button variant="secondary" onClick={onClose}>Annuler</Button>
        <Button busy={busy === 'receive'} disabled={invalid} onClick={() => void run('receive', async () => {
          await receivePurchaseOrder(order.id, entries.filter(entry => entry.amount > 0).map(entry => ({itemId: entry.line.itemId, quantity: entry.amount, unitPrice: entry.price})), close);
          await onDone();
          onClose();
        }, 'Livraison enregistrée : le stock est à jour.')}><PackageCheck className="size-4" aria-hidden="true" />Enregistrer la livraison</Button>
      </>
    }>
      <div className="space-y-3">
        <p className="text-sm text-ink-600">Saisissez ce qui a vraiment été livré, et le prix facturé s’il diffère. Chaque quantité entre en stock avec la référence {order.number}.</p>
        <ul className="divide-y divide-slate-100 rounded-xl border border-slate-200">
          {entries.map(({line}) => (
            <li key={line.itemId} className="grid grid-cols-1 gap-2 px-3 py-2 text-sm sm:grid-cols-[1fr_8rem_8rem] sm:items-center">
              <div>
                <p className="font-medium">{line.ingredientName ?? 'Ingrédient'}</p>
                <p className="text-xs text-ink-500">Commandé {quantity(line.ordered, line.unit)}{line.received > 0 ? ` · déjà reçu ${quantity(line.received, line.unit)}` : ''}</p>
              </div>
              <div className="flex items-center gap-1">
                <Input aria-label={`Reçu : ${line.ingredientName}`} inputMode="decimal" value={received[line.itemId]?.amount ?? ''}
                  onChange={event => setReceived(current => ({...current, [line.itemId]: {...current[line.itemId], amount: event.target.value}}))} />
                <span className="w-8 text-ink-500">{unitShort(line.unit)}</span>
              </div>
              <Input aria-label={`Prix : ${line.ingredientName}`} inputMode="decimal" value={received[line.itemId]?.price ?? ''} placeholder={`Prix / ${unitShort(line.unit)}${currency ? ` (${currency})` : ''}`}
                onChange={event => setReceived(current => ({...current, [line.itemId]: {...current[line.itemId], price: event.target.value}}))} />
            </li>
          ))}
        </ul>
        {partial && (
          <label className="flex items-start gap-3 text-sm">
            <input type="checkbox" className="mt-0.5 size-4 accent-brand-600" checked={close} onChange={event => setClose(event.target.checked)} />
            <span>Le reste ne sera pas livré <span className="block text-xs text-ink-500">Sinon, le bon reste ouvert pour une prochaine livraison.</span></span>
          </label>
        )}
      </div>
    </Modal>
  );
}

function orderText(order: PurchaseOrder, restaurant: string): string {
  const lines = order.lines.map(line => `- ${line.ingredientName ?? 'Article'} : ${quantity(line.ordered, line.unit)}`);
  return [`Bon de commande ${order.number} — ${restaurant}`, ...lines, order.notes ? `Note : ${order.notes}` : null].filter(Boolean).join('\n');
}

export function PurchaseOrders() {
  const {location, can, tenant, currency} = useConsole();
  const toast = useToast();
  const {busy, run} = useAction();
  const [editing, setEditing] = useState<PurchaseOrder | null>(null);
  const [composing, setComposing] = useState(false);
  const [prefill, setPrefill] = useState<DraftLine[]>([]);
  const [receiving, setReceiving] = useState<PurchaseOrder | null>(null);
  const [showClosed, setShowClosed] = useState(false);
  const orders = useResource(signal => (location ? listPurchaseOrders(location.id, signal) : Promise.resolve([])), [location?.id]);
  const items = useResource(signal => (location ? listItems(location.id, false, signal) : Promise.resolve([])), [location?.id]);
  const suggestions = useResource(signal => (location ? reorderSuggestions(location.id, signal) : Promise.resolve([])), [location?.id, orders.data]);
  const suppliers = useResource(signal => listSuppliers(signal), []);
  const operate = can('inventory.operate');
  const supplierById = useMemo(() => new Map((suppliers.data ?? []).map(supplier => [supplier.id, supplier])), [suppliers.data]);
  const visible = (orders.data ?? []).filter(order => showClosed || order.status === 'Draft' || order.status === 'Sent' || order.status === 'PartiallyReceived');
  const reload = async () => {
    await Promise.all([orders.reload(), items.reload()]);
  };

  if (!location) return <Alert tone="blue">Choisissez un établissement pour voir ses bons de commande.</Alert>;
  if (orders.error || suppliers.error) return <Alert>{orders.error ?? suppliers.error}</Alert>;
  if (!orders.data || !suppliers.data || !items.data) return <Spinner />;

  const startOrder = (lines: DraftLine[]) => {
    setEditing(null);
    setPrefill(lines);
    setComposing(true);
  };

  return (
    <div className="space-y-4">
      {(suggestions.data ?? []).length > 0 && (
        <Card className="border-amber-300 bg-amber-50 p-4">
          <div className="flex flex-wrap items-center gap-3">
            <div className="min-w-0 flex-1">
              <p className="font-semibold">{suggestions.data!.length} ingrédient(s) sous le seuil d’alerte</p>
              <p className="truncate text-sm text-ink-600">{suggestions.data!.map(item => `${item.ingredientName} : ${quantity(item.suggestedQuantity, item.unit)}`).join(' · ')}</p>
            </div>
            {operate && suppliers.data.some(supplier => supplier.isActive) && (
              <Button variant="secondary" onClick={() => startOrder(suggestions.data!.map(item => ({
                itemId: item.itemId, amount: String(item.suggestedQuantity), price: item.averageUnitCost === null ? '' : String(item.averageUnitCost),
              })))}><ClipboardList className="size-4" aria-hidden="true" />Préparer le réassort</Button>
            )}
          </div>
        </Card>
      )}
      <div className="flex flex-wrap items-center gap-3">
        <label className="flex flex-1 items-center gap-2 text-sm text-ink-600">
          <input type="checkbox" className="size-4 accent-brand-600" checked={showClosed} onChange={event => setShowClosed(event.target.checked)} />Afficher les bons reçus et annulés
        </label>
        {operate && <Button disabled={!suppliers.data.some(supplier => supplier.isActive)} onClick={() => startOrder([])}><Plus className="size-4" aria-hidden="true" />Nouveau bon</Button>}
      </div>
      {!suppliers.data.some(supplier => supplier.isActive) && <Alert tone="blue">Ajoutez d’abord un fournisseur (onglet Fournisseurs).</Alert>}
      {visible.length === 0 ? (
        <Card className="p-6"><Empty icon={<ClipboardList className="size-8" />} title="Aucun bon de commande en cours">Un bon envoyé puis réceptionné met le stock et le coût matière à jour en une fois.</Empty></Card>
      ) : (
        <Card className="overflow-hidden">
          <ul className="divide-y divide-slate-100">
            {visible.map(order => {
              const supplier = supplierById.get(order.supplierId);
              const text = orderText(order, location.name || tenant.name);
              const phone = supplier?.phone?.replace(/[^\d]/g, '');
              return (
                <li key={order.id} className="flex flex-wrap items-center gap-3 px-4 py-3 text-sm">
                  <div className="min-w-0 flex-1 basis-56">
                    <p className="flex flex-wrap items-center gap-2 font-medium">{order.number}<Badge tone={statusTone[order.status]}>{purchaseStatusLabel[order.status]}</Badge></p>
                    <p className="truncate text-ink-500">
                      {order.supplierName ?? 'Fournisseur'} · {order.lines.length} article(s) · {dateTime(order.sentAtUtc ?? order.createdAtUtc)}
                      {order.estimatedTotal !== null ? ` · ${money(order.estimatedTotal, currency)}` : ''}
                    </p>
                  </div>
                  {operate && (
                    <div className="flex flex-wrap gap-1">
                      {order.status === 'Draft' && <Button size="sm" variant="secondary" onClick={() => {
                        setEditing(order);
                        setComposing(true);
                      }}><Pencil className="size-4" aria-hidden="true" />Modifier</Button>}
                      {(order.status === 'Sent' || order.status === 'PartiallyReceived') && (
                        <Button size="sm" onClick={() => setReceiving(order)}><PackageCheck className="size-4" aria-hidden="true" />Réceptionner</Button>
                      )}
                      {order.status !== 'Cancelled' && order.status !== 'Draft' && phone && (
                        <a className="inline-flex items-center gap-1 rounded-xl px-2.5 py-1.5 text-sm font-semibold text-emerald-700 hover:bg-emerald-50" target="_blank" rel="noreferrer"
                          href={`https://wa.me/${phone}?text=${encodeURIComponent(text)}`}><MessageCircle className="size-4" aria-hidden="true" />WhatsApp</a>
                      )}
                      {order.status !== 'Cancelled' && (
                        <Button size="sm" variant="ghost" aria-label={`Copier le bon ${order.number}`} title="Copier le texte du bon" onClick={() => {
                          void navigator.clipboard?.writeText(text).then(() => toast('success', 'Bon copié.'), () => toast('error', 'Copie impossible.'));
                        }}><Copy className="size-4" /></Button>
                      )}
                      {(order.status === 'Draft' || order.status === 'Sent') && (
                        <Button size="sm" variant="ghost" className="text-red-600" aria-label={`Annuler le bon ${order.number}`} busy={busy === order.id}
                          onClick={() => run(order.id, async () => {
                            await cancelPurchaseOrder(order.id);
                            await reload();
                          }, 'Bon annulé.')}><Trash2 className="size-4" /></Button>
                      )}
                    </div>
                  )}
                </li>
              );
            })}
          </ul>
        </Card>
      )}
      <OrderDialog order={editing} prefill={prefill} open={composing} items={items.data} suppliers={suppliers.data} onClose={() => setComposing(false)} onDone={reload} />
      <ReceiveDialog order={receiving} onClose={() => setReceiving(null)} onDone={reload} />
    </div>
  );
}
