'use client';
import {useEffect, useMemo, useState} from 'react';
import {Ban, Pause, Pencil, Play, Plus, Shuffle, Square, Tag, Ticket} from 'lucide-react';
import {all} from '@/lib/api';
import {money, orderTypeLabel} from '@/lib/format';
import {useResource} from '@/lib/hooks';
import {
  benefitLabel, changeRules, couponStatusLabel, createCoupon, createPromotion, disableCoupon, listCoupons, listPromotions, promotionAction,
  promotionStatusLabel, promotionStatusTone, type BenefitType, type Promotion, type PromotionStatus, type RulesInput,
} from '@/lib/marketing';
import type {OrderType, ProductSummary} from '@/lib/types';
import {useConsole} from '@/components/console';
import {Alert, Badge, Button, Card, Empty, Field, Input, Modal, Select, Spinner, cx, useAction} from '@/components/ui';

const orderTypes: OrderType[] = ['DineIn', 'Takeaway', 'Delivery'];

const day = (iso: string) => new Intl.DateTimeFormat('fr-FR', {day: 'numeric', month: 'short', year: 'numeric'}).format(new Date(iso));

/** Valeur d'un champ datetime-local (heure locale du navigateur). */
function localInput(date: Date) {
  const pad = (value: number) => String(value).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

function number(value: string): number | null {
  const parsed = Number(value.replace(',', '.').replace(/\s/g, ''));
  return value.trim() && Number.isFinite(parsed) ? parsed : null;
}

interface Form {
  name: string;
  from: string;
  until: string;
  type: BenefitType;
  value: string;
  cap: string;
  minimum: string;
  orderTypes: OrderType[];
  locationIds: string[];
  everyProduct: boolean;
  productIds: string[];
  limit: string;
  requiresCoupon: boolean;
}

function formOf(promotion: Promotion | null): Form {
  if (!promotion) {
    const start = new Date();
    start.setMinutes(0, 0, 0);
    const end = new Date(start.getTime() + 30 * 86_400_000);
    end.setHours(23, 59, 0, 0);
    return {
      name: '', from: localInput(start), until: localInput(end), type: 'Percentage', value: '10', cap: '', minimum: '',
      orderTypes: [], locationIds: [], everyProduct: true, productIds: [], limit: '', requiresCoupon: false,
    };
  }
  const {rules} = promotion;
  const benefit = rules.benefit;
  return {
    name: promotion.name,
    from: localInput(new Date(rules.validFromUtc)),
    until: localInput(new Date(rules.validUntilUtc)),
    type: benefit?.type ?? 'Percentage',
    value: String((benefit?.type === 'FixedAmount' ? benefit.amount : benefit?.percentage) ?? ''),
    cap: benefit?.maxDiscount ? String(benefit.maxDiscount) : '',
    minimum: rules.minimumOrderAmount ? String(rules.minimumOrderAmount) : '',
    orderTypes: rules.orderTypes,
    locationIds: rules.locationIds,
    everyProduct: rules.productIds.length === 0,
    productIds: rules.productIds,
    limit: rules.usageLimit ? String(rules.usageLimit) : '',
    requiresCoupon: rules.requiresCoupon,
  };
}

function PromotionDialog({open, promotion, onClose, onDone}: {open: boolean; promotion: Promotion | null; onClose: () => void; onDone: (id: string) => Promise<void>}) {
  const {tenant, location, locations} = useConsole();
  const {busy, run} = useAction();
  const [form, setForm] = useState<Form>(() => formOf(null));
  const [search, setSearch] = useState('');
  const products = useResource(() => (open ? all<ProductSummary>('/catalog/products', {}, 1000) : Promise.resolve([] as ProductSummary[])), [open]);
  useEffect(() => {
    if (!open) return;
    // Une nouvelle promotion vaut d'abord pour l'établissement affiché (à élargir dans la liste ci-dessous).
    setForm(promotion || !location || locations.length < 2 ? formOf(promotion) : {...formOf(null), locationIds: [location.id]});
    setSearch('');
    // Seulement à l'ouverture.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open]);

  const currency = promotion?.rules.currencyCode ?? tenant.defaultCurrencyCode;
  const set = <K extends keyof Form>(key: K, value: Form[K]) => setForm(current => ({...current, [key]: value}));
  const toggle = <T,>(list: T[], item: T) => (list.includes(item) ? list.filter(value => value !== item) : [...list, item]);
  const used = (promotion?.usageCount ?? 0) > 0;
  const value = number(form.value);
  const invalid = !form.name.trim() || value === null || value <= 0 || (form.type === 'Percentage' && value > 100)
    || !form.from || !form.until || new Date(form.until) <= new Date(form.from) || (!form.everyProduct && form.productIds.length === 0);
  const catalog = (products.data ?? []).filter(product => product.status !== 'Archived');
  const shown = catalog.filter(product => product.name.toLowerCase().includes(search.trim().toLowerCase())).slice(0, 50);

  const rules = (): RulesInput => {
    const cap = form.type === 'Percentage' ? number(form.cap) : null;
    const minimum = number(form.minimum);
    const fixed = form.type === 'FixedAmount';
    return {
      validFromUtc: new Date(form.from).toISOString(),
      validUntilUtc: new Date(form.until).toISOString(),
      benefit: {type: form.type, percentage: fixed ? null : value, amount: fixed ? value : null, maxDiscount: cap},
      currencyCode: fixed || cap || minimum ? currency : null,
      minimumOrderAmount: minimum,
      productIds: form.everyProduct ? [] : form.productIds,
      locationIds: form.locationIds,
      orderTypes: form.orderTypes,
      usageLimit: number(form.limit),
      requiresCoupon: form.requiresCoupon,
    };
  };

  return (
    <Modal wide open={open} onClose={onClose} title={promotion ? `Modifier « ${promotion.name} »` : 'Nouvelle promotion'} footer={
      <>
        <Button variant="secondary" onClick={onClose}>Annuler</Button>
        <Button type="submit" form="promotion-form" busy={busy === 'save'} disabled={invalid}>{promotion ? 'Enregistrer' : 'Créer en brouillon'}</Button>
      </>
    }>
      <form id="promotion-form" className="space-y-5" onSubmit={event => {
        event.preventDefault();
        void run('save', async () => {
          if (promotion) {
            await changeRules(promotion.id, rules());
            await onDone(promotion.id);
          } else {
            const {id} = await createPromotion(form.name.trim(), rules());
            await onDone(id);
          }
          onClose();
        }, promotion ? 'Promotion enregistrée.' : 'Promotion créée en brouillon : activez-la quand elle est prête.');
      }}>
        {used && <Alert tone="amber">Déjà utilisée {promotion?.usageCount} fois : seuls un report de la date de fin ou une limite plus haute seront acceptés.</Alert>}
        {!promotion && (
          <Field label="Nom" hint="Visible par l’équipe et sur le ticket de la commande.">
            <Input required maxLength={120} value={form.name} onChange={event => set('name', event.target.value)} placeholder="Happy hour -15 %" />
          </Field>
        )}

        <fieldset className="space-y-3">
          <legend className="mb-1.5 text-sm font-semibold">Remise</legend>
          <div className="grid grid-cols-2 gap-2" role="radiogroup" aria-label="Type de remise">
            {([['Percentage', 'Pourcentage'], ['FixedAmount', `Montant fixe (${currency})`]] as const).map(([type, label]) => (
              <button key={type} type="button" role="radio" aria-checked={form.type === type} onClick={() => set('type', type)}
                className={cx('rounded-xl border p-3 text-left text-sm font-medium', form.type === type ? 'border-brand-500 bg-brand-50 ring-1 ring-brand-500' : 'border-slate-200 hover:border-slate-300')}>
                {label}
              </button>
            ))}
          </div>
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
            <Field label={form.type === 'Percentage' ? 'Pourcentage' : 'Montant'}>
              <Input inputMode="decimal" required value={form.value} onChange={event => set('value', event.target.value)} placeholder={form.type === 'Percentage' ? '15' : '1000'} />
            </Field>
            {form.type === 'Percentage' && (
              <Field label="Plafond (facultatif)" hint={`En ${currency}.`}>
                <Input inputMode="decimal" value={form.cap} onChange={event => set('cap', event.target.value)} placeholder="Aucun" />
              </Field>
            )}
            <Field label="Commande minimum (facultatif)" hint={`En ${currency}.`}>
              <Input inputMode="decimal" value={form.minimum} onChange={event => set('minimum', event.target.value)} placeholder="Aucun" />
            </Field>
          </div>
        </fieldset>

        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
          <Field label="Début"><Input type="datetime-local" required value={form.from} onChange={event => set('from', event.target.value)} /></Field>
          <Field label="Fin"><Input type="datetime-local" required value={form.until} onChange={event => set('until', event.target.value)} /></Field>
        </div>

        <fieldset>
          <legend className="mb-1.5 text-sm font-semibold">Types de commande</legend>
          <div className="flex flex-wrap gap-x-5 gap-y-1">
            {orderTypes.map(type => (
              <label key={type} className="flex items-center gap-2 text-sm">
                <input type="checkbox" className="size-4 accent-brand-600" checked={form.orderTypes.includes(type)} onChange={() => set('orderTypes', toggle(form.orderTypes, type))} />
                {orderTypeLabel[type]}
              </label>
            ))}
          </div>
          <p className="mt-1 text-xs text-ink-500">Aucune case cochée : tous les types.</p>
        </fieldset>

        {locations.length > 1 && (
          <fieldset>
            <legend className="mb-1.5 text-sm font-semibold">Établissements</legend>
            <div className="flex flex-wrap gap-x-5 gap-y-1">
              {locations.map(location => (
                <label key={location.id} className="flex items-center gap-2 text-sm">
                  <input type="checkbox" className="size-4 accent-brand-600" checked={form.locationIds.includes(location.id)} onChange={() => set('locationIds', toggle(form.locationIds, location.id))} />
                  {location.name}
                </label>
              ))}
            </div>
            <p className="mt-1 text-xs text-ink-500">Aucune case cochée : tous les établissements.</p>
          </fieldset>
        )}

        <fieldset>
          <legend className="mb-1.5 text-sm font-semibold">Produits concernés</legend>
          <div className="flex flex-wrap gap-x-5 gap-y-1 text-sm">
            <label className="flex items-center gap-2"><input type="radio" name="scope" className="size-4 accent-brand-600" checked={form.everyProduct} onChange={() => set('everyProduct', true)} />Toute la carte</label>
            <label className="flex items-center gap-2"><input type="radio" name="scope" className="size-4 accent-brand-600" checked={!form.everyProduct} onChange={() => set('everyProduct', false)} />Certains produits</label>
          </div>
          {!form.everyProduct && (
            <div className="mt-2 rounded-xl border border-slate-200 p-3">
              <Input type="search" aria-label="Chercher un produit" value={search} onChange={event => setSearch(event.target.value)} placeholder="Chercher un produit…" />
              {!products.data ? <Spinner /> : (
                <div className="mt-2 max-h-48 space-y-1 overflow-y-auto">
                  {shown.map(product => (
                    <label key={product.id} className="flex items-center gap-2 text-sm">
                      <input type="checkbox" className="size-4 accent-brand-600" checked={form.productIds.includes(product.id)} onChange={() => set('productIds', toggle(form.productIds, product.id))} />
                      <span className="flex-1 truncate">{product.name}</span>
                      <span className="text-xs text-ink-500">{money(product.basePrice, product.currencyCode)}</span>
                    </label>
                  ))}
                  {shown.length === 0 && <p className="text-sm text-ink-500">Aucun produit.</p>}
                </div>
              )}
              <p className="mt-2 text-xs text-ink-500">{form.productIds.length} produit{form.productIds.length > 1 ? 's' : ''} sélectionné{form.productIds.length > 1 ? 's' : ''}. La remise ne porte que sur ces lignes.</p>
            </div>
          )}
        </fieldset>

        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
          <Field label="Nombre d’utilisations maximum (facultatif)" hint="Toutes commandes confondues.">
            <Input type="number" min={1} value={form.limit} onChange={event => set('limit', event.target.value)} placeholder="Illimité" />
          </Field>
          <label className="flex items-start gap-3 rounded-xl border border-slate-200 p-3 text-sm sm:mt-6">
            <input type="checkbox" className="mt-0.5 size-4 accent-brand-600" checked={form.requiresCoupon} onChange={event => set('requiresCoupon', event.target.checked)} />
            <span><span className="font-medium">Réservée aux codes promo</span><span className="block text-xs text-ink-500">Sinon, elle est proposée sur toute commande éligible.</span></span>
          </label>
        </div>
      </form>
    </Modal>
  );
}

function Coupons({promotion, manage}: {promotion: Promotion; manage: boolean}) {
  const {busy, run} = useAction();
  const {data, error, reload} = useResource(signal => listCoupons(promotion.id, signal), [promotion.id]);
  const [code, setCode] = useState('');
  const [uses, setUses] = useState('100');
  const canAdd = manage && promotion.status !== 'Expired';
  return (
    <div>
      {error ? <Alert>{error}</Alert> : !data ? <Spinner /> : data.length === 0 ? (
        <p className="text-sm text-ink-500">Aucun code pour cette promotion.</p>
      ) : (
        <ul className="divide-y divide-slate-100">
          {data.map(coupon => {
            const status = coupon.status === 'Active' && coupon.remainingUses === 0 ? 'Exhausted' : coupon.status;
            return (
              <li key={coupon.id} className="flex flex-wrap items-center gap-3 py-2.5 text-sm">
                <code className="rounded-lg bg-slate-100 px-2 py-1 font-semibold tracking-wide">{coupon.code}</code>
                <span className="flex-1 text-ink-500">{coupon.usageCount} / {coupon.maxUses} utilisation{coupon.maxUses > 1 ? 's' : ''}</span>
                <Badge tone={status === 'Active' ? 'green' : status === 'Exhausted' ? 'amber' : 'neutral'}>{couponStatusLabel[status]}</Badge>
                {manage && coupon.status === 'Active' && (
                  <Button size="sm" variant="ghost" className="text-red-600" busy={busy === coupon.id} onClick={() => run(coupon.id, async () => {
                    await disableCoupon(coupon.id);
                    await reload();
                  }, 'Code désactivé.')}><Ban className="size-4" aria-hidden="true" />Désactiver</Button>
                )}
              </li>
            );
          })}
        </ul>
      )}
      {canAdd && (
        <form className="mt-3 flex flex-wrap items-end gap-2" onSubmit={event => {
          event.preventDefault();
          void run('coupon', async () => {
            await createCoupon(promotion.id, code.trim(), Number(uses));
            setCode('');
            await reload();
          }, 'Code créé.');
        }}>
          <Field label="Code" className="min-w-40 flex-1">
            <div className="flex gap-1">
              <Input required minLength={3} maxLength={32} pattern="[A-Za-z0-9\-]+" value={code} onChange={event => setCode(event.target.value.toUpperCase())} placeholder="BIENVENUE10" />
              <Button variant="secondary" aria-label="Générer un code" title="Générer un code" onClick={() => setCode(Math.random().toString(36).slice(2, 10).toUpperCase())}><Shuffle className="size-4" /></Button>
            </div>
          </Field>
          <Field label="Utilisations" className="w-28"><Input type="number" min={1} required value={uses} onChange={event => setUses(event.target.value)} /></Field>
          <Button type="submit" variant="secondary" busy={busy === 'coupon'}><Plus className="size-4" aria-hidden="true" />Ajouter</Button>
        </form>
      )}
    </div>
  );
}

function PromotionDetail({promotion, onClose, onEdit, onChanged}: {promotion: Promotion | null; onClose: () => void; onEdit: () => void; onChanged: () => Promise<void>}) {
  const {can, locations} = useConsole();
  const {busy, run} = useAction();
  const [ending, setEnding] = useState(false);
  useEffect(() => setEnding(false), [promotion?.id]);
  const products = useResource(
    () => (promotion && promotion.rules.productIds.length ? all<ProductSummary>('/catalog/products', {}, 1000) : Promise.resolve([] as ProductSummary[])),
    [promotion?.id],
  );
  if (!promotion) return null;
  const manage = can('promotions.manage');
  const {rules} = promotion;
  const act = (action: 'activate' | 'pause' | 'resume' | 'expire', success: string) => run(action, async () => {
    await promotionAction(promotion.id, action);
    await onChanged();
  }, success);
  const names = (ids: string[], source: {id: string; name: string}[]) => ids.map(id => source.find(item => item.id === id)?.name ?? '—').join(', ');

  return (
    <Modal wide open onClose={onClose} title={promotion.name} footer={manage && promotion.status !== 'Expired' ? (
      <>
        {ending ? (
          <span className="mr-auto flex flex-wrap items-center gap-2">
            <span className="text-sm text-ink-600">Définitif : elle ne pourra plus reprendre.</span>
            <Button variant="danger" busy={busy === 'expire'} onClick={() => act('expire', 'Promotion terminée.')}>Terminer</Button>
            <Button variant="ghost" onClick={() => setEnding(false)}>Non</Button>
          </span>
        ) : (
          <Button variant="ghost" className="mr-auto text-red-600" onClick={() => setEnding(true)}><Square className="size-4" aria-hidden="true" />Terminer</Button>
        )}
        <Button variant="secondary" onClick={onEdit}><Pencil className="size-4" aria-hidden="true" />Modifier</Button>
        {promotion.status === 'Draft' && <Button busy={busy === 'activate'} onClick={() => act('activate', 'Promotion activée.')}><Play className="size-4" aria-hidden="true" />Activer</Button>}
        {promotion.status === 'Active' && <Button variant="secondary" busy={busy === 'pause'} onClick={() => act('pause', 'Promotion mise en pause.')}><Pause className="size-4" aria-hidden="true" />Mettre en pause</Button>}
        {promotion.status === 'Paused' && <Button busy={busy === 'resume'} onClick={() => act('resume', 'Promotion reprise.')}><Play className="size-4" aria-hidden="true" />Reprendre</Button>}
      </>
    ) : undefined}>
      <div className="space-y-5">
        <div className="flex flex-wrap items-center gap-2">
          <Badge tone={promotionStatusTone[promotion.status]}>{promotionStatusLabel[promotion.status]}</Badge>
          {rules.requiresCoupon && <Badge tone="blue">Avec code</Badge>}
          <span className="text-sm text-ink-500">Utilisée {promotion.usageCount} fois{rules.usageLimit ? ` sur ${rules.usageLimit}` : ''}</span>
        </div>
        <dl className="grid grid-cols-1 gap-x-6 gap-y-3 text-sm sm:grid-cols-2">
          <div><dt className="text-ink-500">Remise</dt><dd className="font-semibold">{benefitLabel(rules, money)}</dd></div>
          <div><dt className="text-ink-500">Période</dt><dd>du {day(rules.validFromUtc)} au {day(rules.validUntilUtc)}</dd></div>
          <div><dt className="text-ink-500">Commande minimum</dt><dd>{rules.minimumOrderAmount ? money(rules.minimumOrderAmount, rules.currencyCode ?? '') : 'Aucune'}</dd></div>
          <div><dt className="text-ink-500">Types de commande</dt><dd>{rules.orderTypes.length ? rules.orderTypes.map(type => orderTypeLabel[type]).join(', ') : 'Tous'}</dd></div>
          {locations.length > 1 && <div><dt className="text-ink-500">Établissements</dt><dd>{rules.locationIds.length ? names(rules.locationIds, locations) : 'Tous'}</dd></div>}
          <div><dt className="text-ink-500">Produits</dt><dd>{rules.productIds.length ? (products.data ? names(rules.productIds, products.data) : '…') : 'Toute la carte'}</dd></div>
        </dl>
        {promotion.status === 'Draft' && <Alert tone="blue">Brouillon : elle ne s’applique à aucune commande tant qu’elle n’est pas activée.</Alert>}
        <section>
          <h3 className="mb-2 flex items-center gap-2 font-semibold"><Ticket className="size-4" aria-hidden="true" />Codes promo</h3>
          {!rules.requiresCoupon && <p className="mb-2 text-xs text-ink-500">Facultatif : la promotion s’applique déjà sans code ; un code permet de la suivre par canal (affiche, réseaux…).</p>}
          <Coupons promotion={promotion} manage={manage} />
        </section>
      </div>
    </Modal>
  );
}

export function Promotions() {
  const {can, location, locations} = useConsole();
  const [status, setStatus] = useState<PromotionStatus | ''>('');
  const several = locations.length > 1 && !!location;
  const [here, setHere] = useState(true);
  const [editing, setEditing] = useState<Promotion | 'new' | null>(null);
  const [openId, setOpenId] = useState<string | null>(null);
  const {data, error, reload} = useResource(signal => listPromotions(status, 1, signal), [status]);
  const opened = useMemo(() => data?.items.find(item => item.id === openId) ?? null, [data, openId]);
  // Ici : les promotions sans restriction d'établissement et celles valables dans l'établissement affiché.
  const items = useMemo(
    () => (data?.items ?? []).filter(item => !several || !here || item.rules.locationIds.length === 0 || item.rules.locationIds.includes(location!.id)),
    [data, several, here, location],
  );
  const placeLabel = (ids: string[]) =>
    ids.length === 0 ? 'Tous les établissements' : locations.filter(item => ids.includes(item.id)).map(item => item.name).join(', ');
  const manage = can('promotions.manage');

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center gap-2">
        <Select aria-label="Filtrer par statut" className="w-full! sm:w-auto!" value={status} onChange={event => setStatus(event.target.value as PromotionStatus | '')}>
          <option value="">Toutes les promotions</option>
          {(Object.keys(promotionStatusLabel) as PromotionStatus[]).map(item => <option key={item} value={item}>{promotionStatusLabel[item]}</option>)}
        </Select>
        {several && (
          <Select aria-label="Filtrer par établissement" className="w-full! sm:w-auto!" value={here ? 'here' : 'all'} onChange={event => setHere(event.target.value === 'here')}>
            <option value="here">Valables à {location!.name}</option>
            <option value="all">Tous les établissements</option>
          </Select>
        )}
        {manage && <Button className="ml-auto" onClick={() => setEditing('new')}><Plus className="size-4" aria-hidden="true" />Nouvelle promotion</Button>}
      </div>
      {error ? <Alert>{error}</Alert> : !data ? <Spinner /> : items.length === 0 ? (
        <Card className="p-6">
          <Empty icon={<Tag className="size-8" />} title={status ? 'Aucune promotion avec ce statut' : 'Aucune promotion'}>
            Remise en pourcentage ou en montant, sur toute la carte ou certains produits, avec ou sans code promo.
          </Empty>
        </Card>
      ) : (
        <div className="grid grid-cols-1 gap-3 md:grid-cols-2 xl:grid-cols-3">
          {items.map(promotion => (
            <button key={promotion.id} type="button" onClick={() => setOpenId(promotion.id)} className="text-left">
              <Card className="h-full p-4 transition hover:border-brand-300 hover:shadow-md">
                <div className="flex items-start justify-between gap-2">
                  <p className="min-w-0 truncate font-semibold">{promotion.name}</p>
                  <Badge tone={promotionStatusTone[promotion.status]}>{promotionStatusLabel[promotion.status]}</Badge>
                </div>
                <p className="mt-2 text-2xl font-bold tracking-tight text-brand-700">{benefitLabel(promotion.rules, money)}</p>
                <p className="mt-1 text-sm text-ink-500">Du {day(promotion.rules.validFromUtc)} au {day(promotion.rules.validUntilUtc)}</p>
                <div className="mt-3 flex flex-wrap gap-1.5 text-xs">
                  {several && <Badge>{placeLabel(promotion.rules.locationIds)}</Badge>}
                  {promotion.rules.requiresCoupon && <Badge tone="blue">Avec code</Badge>}
                  {promotion.rules.minimumOrderAmount ? <Badge>Min. {money(promotion.rules.minimumOrderAmount, promotion.rules.currencyCode ?? '')}</Badge> : null}
                  {promotion.rules.productIds.length > 0 && <Badge>{promotion.rules.productIds.length} produit{promotion.rules.productIds.length > 1 ? 's' : ''}</Badge>}
                  <Badge>{promotion.usageCount} utilisation{promotion.usageCount > 1 ? 's' : ''}{promotion.rules.usageLimit ? ` / ${promotion.rules.usageLimit}` : ''}</Badge>
                </div>
              </Card>
            </button>
          ))}
        </div>
      )}
      <PromotionDetail promotion={opened} onClose={() => setOpenId(null)} onEdit={() => {
        setEditing(opened);
        setOpenId(null);
      }} onChanged={reload} />
      <PromotionDialog open={editing !== null} promotion={editing === 'new' ? null : editing} onClose={() => setEditing(null)} onDone={async id => {
        await reload();
        setOpenId(id);
      }} />
    </div>
  );
}
