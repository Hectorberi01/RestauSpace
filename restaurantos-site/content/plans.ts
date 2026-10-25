import catalogue from './catalogue.json';

export type Market = 'bj' | 'fr';
export type Interval = 'Monthly' | 'Yearly';

interface CatalogueEntitlement {
  featureCode: string;
  enabled: boolean;
  limit: number | null;
}

export interface CataloguePlan {
  name: string;
  price: number;
  currencyCode: string;
  billingInterval: Interval;
  kind?: 'Base' | 'AddOn';
  entitlements: CatalogueEntitlement[];
}

export interface Tier {
  name: string;
  audience: string;
  /** Points forts de la carte du plan. */
  highlights: string[];
  highlighted: boolean;
  monthly: CataloguePlan;
  yearly?: CataloguePlan;
}

export const markets: Record<Market, {label: string; currency: string; taxNote: string}> = {
  fr: {label: 'France', currency: 'EUR', taxNote: 'Prix en euros hors taxes.'},
  bj: {label: 'Bénin et zone UEMOA', currency: 'XOF', taxNote: 'Prix en francs CFA.'},
};

const audiences: Record<string, string> = {
  Essentiel: 'Pour un restaurant indépendant qui veut un service bien tenu',
  Pro: 'Pour vendre en ligne, fidéliser et relier vos outils',
  'Multi-sites': 'Pour une enseigne de plusieurs établissements',
};

const highlights: Record<string, string[]> = {
  Essentiel: ['Commandes, caisse, cuisine et réservations', 'Carte, salle, stock et équipe', 'Programme de fidélité'],
  Pro: ['Tout Essentiel, plus :', 'Site de commande en ligne inclus', 'Campagnes, cartes cadeaux et automatisations', 'API et webhooks inclus', 'Support prioritaire'],
  'Multi-sites': ['Tout Pro, plus :', '5 établissements, extensibles', 'Rapports consolidés de tous vos établissements'],
};

const plans = (catalogue as {plans: CataloguePlan[]}).plans;

/** Plans de base mensuels d'un marché, du moins cher au plus cher. */
export function tiers(market: Market): Tier[] {
  const currency = markets[market].currency;
  const monthly = plans
    .filter(p => p.currencyCode === currency && p.billingInterval === 'Monthly' && p.kind !== 'AddOn')
    .sort((a, b) => a.price - b.price);
  return monthly.map(plan => ({
    name: plan.name,
    audience: audiences[plan.name] ?? '',
    highlights: highlights[plan.name] ?? [],
    highlighted: plan.name === 'Pro',
    monthly: plan,
    yearly: plans.find(p => p.currencyCode === currency && p.billingInterval === 'Yearly' && p.name === `${plan.name} annuel`),
  }));
}

function entitlement(plan: CataloguePlan, code: string) {
  return plan.entitlements.find(e => e.featureCode === code);
}

export function enabled(plan: CataloguePlan, code: string): boolean {
  return entitlement(plan, code)?.enabled ?? false;
}

/** Limite d'un quota : 0 si désactivé, Infinity si illimité. */
export function capacity(plan: CataloguePlan, code: string): number {
  const e = entitlement(plan, code);
  if (!e || !e.enabled) return 0;
  return e.limit ?? Number.POSITIVE_INFINITY;
}

const number = (value: number) => new Intl.NumberFormat('fr-FR').format(value);

export function quota(plan: CataloguePlan, code: string): string {
  const e = entitlement(plan, code);
  if (!e || !e.enabled) return '—';
  if (e.limit === null) return 'Illimité';
  return number(e.limit);
}

/** Les commandes au-delà de 10 000 par mois s'affichent « Élevé ». */
export function orders(plan: CataloguePlan): string {
  const limit = capacity(plan, 'orders.monthly');
  return limit > 10000 ? 'Élevé' : quota(plan, 'orders.monthly');
}

// Tableau comparatif

export type Cell = {kind: 'check'} | {kind: 'dash'} | {kind: 'text'; text: string; strong?: boolean};

export interface CompareRow {
  code: string;
  label: string;
  hint?: string;
  cell: (plan: CataloguePlan, market: Market) => Cell;
}

export interface CompareSection {
  title: string;
  rows: CompareRow[];
}

const text = (value: string, strong = false): Cell => (value === '—' ? {kind: 'dash'} : {kind: 'text', text: value, strong});

/** Coche si le plan l'inclut, « En option » si une option le donne, sinon tiret. */
function feature(code: string, optionText = 'En option'): CompareRow['cell'] {
  return (plan, market) => {
    if (enabled(plan, code)) return {kind: 'check'};
    return addOnPlans(market).some(a => enabled(a, code)) ? {kind: 'text', text: optionText} : {kind: 'dash'};
  };
}

export const compareSections: CompareSection[] = [
  {
    title: 'Volumes',
    rows: [
      {code: 'locations.count', label: 'Établissements', cell: plan => text(quota(plan, 'locations.count'))},
      {code: 'staff.count', label: 'Membres de l’équipe', cell: plan => text(quota(plan, 'staff.count'))},
      {code: 'orders.monthly', label: 'Commandes par mois', cell: plan => text(orders(plan))},
      {code: 'notifications.email.monthly', label: 'Emails par mois', hint: 'par le compte de la plateforme', cell: plan => text(quota(plan, 'notifications.email.monthly'))},
      {code: 'notifications.sms.monthly', label: 'SMS par mois', hint: 'par le compte de la plateforme', cell: plan => text(quota(plan, 'notifications.sms.monthly'))},
      {code: 'notifications.whatsapp.monthly', label: 'Messages WhatsApp par mois', hint: 'par le compte de la plateforme', cell: plan => text(quota(plan, 'notifications.whatsapp.monthly'))},
      {
        code: 'notifications.byok',
        label: 'Envoi avec votre propre compte (email, SMS, WhatsApp)',
        cell: plan => (enabled(plan, 'notifications.byok') ? {kind: 'text', text: 'Sans limite', strong: true} : {kind: 'dash'}),
      },
    ],
  },
  {
    title: 'Fonctionnalités',
    rows: [
      {code: 'core', label: 'Commandes, caisse, cuisine, carte, réservations, salle, stock et équipe', cell: () => ({kind: 'check'})},
      {code: 'loyalty', label: 'Programme de fidélité', cell: feature('loyalty')},
      {code: 'customers.advanced', label: 'Clients avancés', hint: 'import, étiquettes et segments', cell: feature('customers.advanced')},
      {code: 'marketing.campaigns', label: 'Campagnes email, SMS et WhatsApp', cell: feature('marketing.campaigns')},
      {code: 'marketing.gift_cards', label: 'Cartes cadeaux', cell: feature('marketing.gift_cards')},
      {code: 'automations', label: 'Automatisations', hint: 'rapports planifiés, campagnes programmées', cell: feature('automations')},
      {code: 'reporting.advanced', label: 'Rapports avancés', hint: 'exports comptables, rapprochement', cell: feature('reporting.advanced')},
      {code: 'reporting.consolidated', label: 'Rapports consolidés', hint: 'tous établissements', cell: feature('reporting.consolidated')},
      {code: 'storefront.web', label: 'Site de commande en ligne', cell: feature('storefront.web')},
      {code: 'storefront.mobile_app', label: 'Application mobile à votre marque', cell: feature('storefront.mobile_app')},
      {code: 'storefront.kiosk', label: 'Borne de commande', cell: feature('storefront.kiosk', 'En option, par borne')},
      {code: 'storefront.api', label: 'API d’intégration et clés d’API', cell: feature('storefront.api')},
      {code: 'integrations.webhooks', label: 'Webhooks', cell: feature('integrations.webhooks')},
      {
        code: 'locations.extra',
        label: 'Établissements supplémentaires',
        cell: plan => (enabled(plan, 'locations.extra') ? {kind: 'text', text: 'En option'} : {kind: 'dash'}),
      },
      {code: 'support.priority', label: 'Support prioritaire', cell: feature('support.priority')},
    ],
  },
];

// Options

const addOnDescriptions: Record<string, string> = {
  'Site web': 'Commande en ligne sur votre site, avec paiement',
  'Application mobile': 'Votre application de commande à votre marque',
  Borne: 'Prise de commande sur place, prix par borne',
  'API et webhooks': 'Clés d’API et webhooks pour relier votre caisse ou vos outils',
  'Établissement supplémentaire': 'Une adresse de plus sur le même compte, prix par établissement',
};

function addOnPlans(market: Market): CataloguePlan[] {
  const currency = markets[market].currency;
  return plans.filter(p => p.currencyCode === currency && p.kind === 'AddOn');
}

const list = (names: string[]) => (names.length > 1 ? `${names.slice(0, -1).join(', ')} et ${names[names.length - 1]}` : names[0]);

/** « Inclus dans … » si les plans donnent déjà tout, « Disponible à partir de … » si l'option exige locations.extra. */
function availability(addOn: CataloguePlan, bases: CataloguePlan[]): string | undefined {
  const included = bases.filter(base => addOn.entitlements.every(e => capacity(base, e.featureCode) === Number.POSITIVE_INFINITY));
  if (included.length) return `Inclus dans ${list(included.map(b => b.name))}`;
  if (addOn.entitlements.some(e => e.featureCode === 'locations.count')) {
    const allowed = bases.filter(base => enabled(base, 'locations.extra'));
    if (allowed.length && allowed.length < bases.length) return `Disponible à partir de ${allowed[0].name}`;
  }
  return undefined;
}

export interface AddOn {
  name: string;
  description: string;
  availability?: string;
  price: number;
  plan: CataloguePlan;
}

/** Options mensuelles, à ajouter au plan depuis la console. */
export function addOns(market: Market): AddOn[] {
  const bases = tiers(market).map(t => t.monthly);
  return addOnPlans(market).map(p => ({
    name: p.name,
    description: addOnDescriptions[p.name] ?? '',
    availability: availability(p, bases),
    price: p.price,
    plan: p,
  }));
}

export function price(amount: number, currency: string): string {
  return new Intl.NumberFormat('fr-FR', {style: 'currency', currency, maximumFractionDigits: amount % 1 ? 2 : 0}).format(amount);
}

/** Essai gratuit sur le plan choisi : réglage Billing:TrialLength de l'API. */
export const trial = '1 mois';
