import catalogue from './catalogue.json';

export type Market = 'bj' | 'fr';
export type Interval = 'Monthly' | 'Yearly';

interface CatalogueEntitlement {
  featureCode: string;
  enabled: boolean;
  limit: number | null;
}

interface CataloguePlan {
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
  highlighted: boolean;
  monthly: CataloguePlan;
  yearly?: CataloguePlan;
}

export const markets: Record<Market, {label: string; currency: string; taxNote: string}> = {
  fr: {label: 'France', currency: 'EUR', taxNote: 'Prix en euros hors taxes.'},
  bj: {label: 'Bénin et zone UEMOA', currency: 'XOF', taxNote: 'Prix en francs CFA.'},
};

const audiences: Record<string, string> = {
  Découverte: 'Pour démarrer : un mois gratuit, petit volume',
  Essentiel: 'Pour un restaurant indépendant',
  Pro: 'Pour un restaurant établi, avec livraison',
  Groupe: 'Pour une enseigne de 2 à 5 établissements',
};

export const quotaRows: {code: string; label: string}[] = [
  {code: 'locations.count', label: 'Établissements'},
  {code: 'staff.count', label: 'Membres de l’équipe'},
  {code: 'orders.monthly', label: 'Commandes par mois'},
  {code: 'notifications.email.monthly', label: 'Emails par mois'},
  {code: 'notifications.sms.monthly', label: 'SMS par mois'},
  {code: 'notifications.whatsapp.monthly', label: 'Messages WhatsApp par mois'},
  {code: 'notifications.push.monthly', label: 'Notifications push par mois'},
];

const plans = (catalogue as {plans: CataloguePlan[]}).plans;

export function tiers(market: Market): Tier[] {
  const currency = markets[market].currency;
  const monthly = plans.filter(p => p.currencyCode === currency && p.billingInterval === 'Monthly' && p.kind !== 'AddOn');
  return monthly.map(plan => ({
    name: plan.name,
    audience: audiences[plan.name] ?? '',
    highlighted: plan.name === 'Pro',
    monthly: plan,
    yearly: plans.find(p => p.currencyCode === currency && p.billingInterval === 'Yearly' && p.name === `${plan.name} annuel`),
  }));
}

const addOnDescriptions: Record<string, string> = {
  'Site web': 'Commande en ligne sur votre site, avec paiement',
  'Application mobile': 'Votre application de commande à votre marque',
  Borne: 'Prise de commande sur place, prix par borne',
  API: 'Clés d’API pour relier votre caisse ou vos outils',
};

/** Options mensuelles, à ajouter à n'importe quel plan payant. */
export function addOns(market: Market): {name: string; description: string; price: number}[] {
  const currency = markets[market].currency;
  return plans
    .filter(p => p.currencyCode === currency && p.kind === 'AddOn')
    .map(p => ({name: p.name, description: addOnDescriptions[p.name] ?? '', price: p.price}));
}

export function quota(plan: CataloguePlan, code: string): string {
  const entitlement = plan.entitlements.find(e => e.featureCode === code);
  if (!entitlement || !entitlement.enabled) return '—';
  if (entitlement.limit === null) return 'Illimité';
  return new Intl.NumberFormat('fr-FR').format(entitlement.limit);
}

export function price(amount: number, currency: string): string {
  return new Intl.NumberFormat('fr-FR', {style: 'currency', currency, maximumFractionDigits: amount % 1 ? 2 : 0}).format(amount);
}

/** Essai gratuit : un mois (comme le plan Découverte), réglage Billing:TrialLength de l'API. */
export const trial = '1 mois';
