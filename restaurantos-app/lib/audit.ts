import {api, type Page} from './api';

export type ActorType = 'User' | 'ApiCredential' | 'System';

export interface AuditEntry {
  id: string;
  tenantId: string | null;
  occurredAtUtc: string;
  recordedAtUtc: string;
  action: string;
  targetType: string;
  targetId: string;
  actorType: ActorType | null;
  actorId: string | null;
  correlationId: string;
  metadata: Record<string, string>;
  importance: Importance;
}

export type Importance = 'High' | 'Medium' | 'Low';

export interface AuditFilter {
  module?: string;
  action?: string;
  targetType?: string;
  targetId?: string;
  actorType?: ActorType;
  actorId?: string;
  from?: string;
  to?: string;
  importance?: Importance;
}

export interface AuditSummary {
  total: number;
  important: number;
  partial: boolean;
  modules: Record<string, number>;
  days: {date: string; total: number; important: number; modules: Record<string, number>}[];
}

export const importanceLabel: Record<Importance, string> = {High: 'Haute', Medium: 'Moyenne', Low: 'Basse'};
export const importanceTone: Record<Importance, 'red' | 'blue' | 'neutral'> = {High: 'red', Medium: 'blue', Low: 'neutral'};

export const modules: Record<string, string> = {
  tenants: 'Restaurant',
  staff: 'Personnel',
  identity: 'Comptes',
  payments: 'Paiements',
  customers: 'Clients',
  inventory: 'Stock',
  reviews: 'Avis',
  integrations: 'Intégrations',
  notifications: 'Notifications',
  billing: 'Abonnement',
  reporting: 'Rapports',
};

export const actorLabel: Record<ActorType, string> = {User: 'Membre de l’équipe', ApiCredential: 'Clé d’API', System: 'Système'};

const actions: Record<string, string> = {
  'tenants.tenant.created': 'Restaurant créé',
  'tenants.tenant.activated': 'Restaurant activé',
  'tenants.tenant.suspended': 'Restaurant suspendu',
  'tenants.tenant.reactivated': 'Restaurant réactivé',
  'tenants.tenant.closed': 'Restaurant fermé',
  'tenants.tenant.renamed': 'Nom du restaurant modifié',
  'tenants.tenant.branding_updated': 'Identité visuelle modifiée',
  'tenants.tenant.public_profile_updated': 'Présentation publique modifiée',
  'tenants.tenant.currency_changed': 'Devise modifiée',
  'tenants.tenant.price_tax_mode_changed': 'Mode des prix (TTC/HT) modifié',
  'tenants.tenant.time_zone_changed': 'Fuseau horaire modifié',
  'staff.member.invited': 'Membre invité',
  'staff.member.activated': 'Membre arrivé',
  'staff.member.owner_created': 'Propriétaire créé',
  'staff.member.suspended': 'Membre suspendu',
  'staff.member.reinstated': 'Membre réintégré',
  'staff.member.left': 'Membre retiré de l’équipe',
  'staff.member.location_assigned': 'Membre affecté à un établissement',
  'staff.member.location_removed': 'Membre retiré d’un établissement',
  'staff.member.tenant_role_assigned': 'Rôle attribué',
  'staff.member.tenant_role_removed': 'Rôle retiré',
  'staff.role.created': 'Rôle créé',
  'staff.role.renamed': 'Rôle renommé',
  'staff.role.permission_granted': 'Permission ajoutée à un rôle',
  'staff.role.permission_revoked': 'Permission retirée d’un rôle',
  'identity.session.started': 'Connexion',
  'identity.session.revoked': 'Session fermée',
  'identity.session.replay_detected': 'Session suspecte bloquée',
  'identity.user.registered': 'Compte créé',
  'identity.user.invited': 'Compte invité',
  'identity.user.invitation_accepted': 'Invitation acceptée',
  'identity.user.email_verified': 'Email vérifié',
  'identity.user.email_changed': 'Email modifié',
  'identity.user.password_changed': 'Mot de passe modifié',
  'identity.user.locked': 'Compte verrouillé',
  'identity.user.unlocked': 'Compte déverrouillé',
  'identity.user.disabled': 'Compte désactivé',
  'identity.user.erased': 'Compte effacé',
  'identity.user.profile_changed': 'Profil modifié',
  'identity.user.preferences_changed': 'Préférences modifiées',
  'identity.user.avatar_changed': 'Photo de profil modifiée',
  'payments.configuration.created': 'Moyen de paiement ajouté',
  'payments.configuration.configured': 'Moyen de paiement configuré',
  'payments.configuration.enabled': 'Moyen de paiement activé',
  'payments.configuration.disabled': 'Moyen de paiement désactivé',
  'payments.configuration.mode_changed': 'Mode d’un moyen de paiement modifié',
  'payments.configuration.method_activated': 'Méthode de paiement activée',
  'payments.configuration.method_deactivated': 'Méthode de paiement désactivée',
  'payments.configuration.location_method_assigned': 'Méthode ouverte dans un établissement',
  'payments.configuration.location_method_removed': 'Méthode retirée d’un établissement',
  'payments.refund.requested': 'Remboursement demandé',
  'payments.refund.registered': 'Remboursement enregistré',
  'payments.refund.succeeded': 'Remboursement effectué',
  'payments.payment.captured_after_cancellation': 'Paiement reçu après annulation',
  'customers.customer.anonymized': 'Client anonymisé',
  'customers.consent.granted': 'Consentement marketing accordé',
  'customers.consent.withdrawn': 'Consentement marketing retiré',
  'inventory.stock.adjusted': 'Stock ajusté (inventaire)',
  'inventory.stock.count_confirmed': 'Inventaire confirmé',
  'inventory.stock.waste_recorded': 'Perte enregistrée',
  'reviews.feedback.reviewed': 'Avis traité',
  'reviews.feedback.archived': 'Avis archivé',
  'reviews.feedback.published': 'Avis publié sur le site',
  'reviews.feedback.unpublished': 'Avis retiré du site',
  'reviews.feedback.customer_detached': 'Avis détaché d’un client anonymisé',
  'reviews.survey.published': 'Questionnaire publié',
  'reviews.survey.closed': 'Questionnaire clos',
  'integrations.api_key.issued': 'Clé d’API créée',
  'integrations.api_key.rotated': 'Clé d’API renouvelée',
  'integrations.api_key.revoked': 'Clé d’API révoquée',
  'integrations.webhook_endpoint.created': 'Webhook créé',
  'integrations.webhook_endpoint.enabled': 'Webhook activé',
  'integrations.webhook_endpoint.disabled': 'Webhook désactivé',
  'integrations.webhook_endpoint.auto_disabled': 'Webhook désactivé automatiquement (échecs)',
  'integrations.webhook_secret.rotated': 'Secret de webhook renouvelé',
  'notifications.configuration.created': 'Canal d’envoi ajouté',
  'notifications.configuration.configured': 'Canal d’envoi configuré',
  'notifications.configuration.enabled': 'Canal d’envoi activé',
  'notifications.configuration.disabled': 'Canal d’envoi désactivé',
  'notifications.configuration.mode_changed': 'Mode d’un canal d’envoi modifié',
  'notifications.configuration.provider_changed': 'Prestataire d’un canal modifié',
  'notifications.notification.redacted': 'Notification effacée',
  'billing.subscription.trial_started': 'Essai démarré',
  'billing.subscription.activated': 'Abonnement activé',
  'billing.subscription.plan_changed': 'Plan changé',
  'billing.subscription.past_due': 'Abonnement en retard de paiement',
  'billing.subscription.suspended': 'Abonnement suspendu',
  'billing.subscription.cancelled': 'Abonnement résilié',
  'billing.subscription.cancellation_scheduled': 'Résiliation programmée',
  'billing.subscription.cancellation_unscheduled': 'Résiliation annulée',
  'billing.subscription.payment_recorded': 'Paiement d’abonnement enregistré',
  'reporting.export.requested': 'Export de rapport demandé',
};

export const actionLabel = (action: string) => actions[action] ?? action;

export const moduleOf = (action: string) => action.split('.')[0];

const metadataLabels: Record<string, string> = {
  amount: 'Montant',
  billing_interval: 'Périodicité',
  change: 'Changement',
  channel: 'Canal',
  code: 'Code',
  configuration_type: 'Type de configuration',
  consecutive_dead_letters: 'Échecs consécutifs',
  counted_quantity: 'Quantité comptée',
  current_period: 'Période',
  customer_id: 'Client',
  default_currency: 'Devise',
  display_name: 'Nom affiché',
  effective_at_utc: 'Effectif le',
  expires_at_utc: 'Expire le',
  export_type: 'Type d’export',
  has_avatar: 'Photo',
  held_tenant_wide_role: 'Rôle restaurant',
  ingredient_id: 'Ingrédient',
  is_enabled: 'Activé',
  is_full_refund: 'Remboursement total',
  is_system: 'Rôle système',
  language: 'Langue',
  location_id: 'Établissement',
  method: 'Méthode',
  mode: 'Mode',
  name: 'Nom',
  new_currency: 'Nouvelle devise',
  new_name: 'Nouveau nom',
  new_plan_id: 'Nouveau plan',
  new_time_zone: 'Nouveau fuseau',
  order_id: 'Commande',
  outcome: 'Résultat',
  paid_period: 'Période payée',
  payment_id: 'Paiement',
  payment_status: 'Statut du paiement',
  permission: 'Permission',
  permissions: 'Permissions',
  plan_id: 'Plan',
  previous_currency: 'Ancienne devise',
  previous_name: 'Ancien nom',
  previous_plan_id: 'Ancien plan',
  previous_provider: 'Ancien prestataire',
  previous_status: 'Ancien statut',
  previous_time_zone: 'Ancien fuseau',
  previous_valid_until_utc: 'Ancien secret valable jusqu’au',
  price: 'Prix',
  prices_include_tax: 'Prix TTC',
  product_news_emails: 'Nouveautés par email',
  provider: 'Prestataire',
  quantity: 'Quantité',
  quantity_after: 'Quantité après',
  quantity_delta: 'Écart',
  question_count: 'Questions',
  reason: 'Motif',
  removed_location_ids: 'Établissements retirés',
  report_definition_id: 'Rapport',
  restored_status: 'Statut rétabli',
  revoked_count: 'Sessions fermées',
  role_id: 'Rôle',
  scopes: 'Permissions de la clé',
  source: 'Origine',
  status: 'Statut',
  time_zone: 'Fuseau',
  total_refunded: 'Total remboursé',
  trial_period: 'Période d’essai',
  type: 'Type',
  unit: 'Unité',
  url: 'Adresse',
  user_id: 'Utilisateur',
};

export const metadataLabel = (key: string) => metadataLabels[key] ?? key;

export const searchAudit = (filter: AuditFilter, page: number, pageSize = 25, signal?: AbortSignal) =>
  api<Page<AuditEntry>>('/audit', {query: {...filter, page, pageSize}, signal});

/** Chiffres d'une période bornée, avec les mêmes filtres que la liste ; jours du fuseau du navigateur. */
export const summarizeAudit = (filter: AuditFilter, signal?: AbortSignal) =>
  api<AuditSummary>('/audit/summary', {query: {...filter, utcOffsetMinutes: -new Date().getTimezoneOffset()}, signal});

/** Toutes les écritures du filtre, page après page (pour l'export), <paramref name="max"/> au plus. */
export async function collectAudit(filter: AuditFilter, max = 5000): Promise<AuditEntry[]> {
  const result: AuditEntry[] = [];
  for (let page = 1; result.length < max; page++) {
    const data = await searchAudit(filter, page, 100);
    result.push(...data.items);
    if (!data.hasMore || data.items.length === 0) break;
  }
  return result.slice(0, max);
}

/** Le verbe d'une action, d'après son dernier segment. */
const verbs: [RegExp, string][] = [
  [/replay_detected$/, 'Alerte'],
  [/(^|_)(created|added)$|\.created$/, 'Ajout'],
  [/invited$/, 'Invitation'],
  [/issued$/, 'Création'],
  [/rotated$/, 'Renouvellement'],
  [/(assigned|granted)$/, 'Attribution'],
  [/(revoked|removed|withdrawn)$/, 'Retrait'],
  [/(left|erased|anonymized|archived|closed|cancelled|detached)$/, 'Suppression'],
  [/(disabled|deactivated|suspended|locked|past_due)$/, 'Désactivation'],
  [/(enabled|activated|reinstated|unlocked|reactivated|accepted|verified|unscheduled)$/, 'Activation'],
  [/(requested|scheduled)$/, 'Demande'],
  [/(succeeded|registered|recorded|confirmed|captured_after_cancellation)$/, 'Enregistrement'],
  [/(started)$/, 'Connexion'],
  [/(published)$/, 'Publication'],
  [/(reviewed)$/, 'Traitement'],
  [/(adjusted)$/, 'Ajustement'],
  [/(renamed|updated|changed|configured)$/, 'Modification'],
];

export const verbOf = (action: string) => verbs.find(([pattern]) => pattern.test(action))?.[1] ?? 'Action';

export const verbTone = (verb: string): 'red' | 'green' | 'amber' | 'neutral' =>
  verb === 'Suppression' || verb === 'Alerte' ? 'red' : verb === 'Ajout' || verb === 'Activation' || verb === 'Création' ? 'green' : verb === 'Désactivation' || verb === 'Retrait' ? 'amber' : 'neutral';

export const targetLabel: Record<string, string> = {
  tenant: 'Restaurant', member: 'Membre', role: 'Rôle', user: 'Compte', session: 'Session', payment: 'Paiement', refund: 'Remboursement',
  payment_configuration: 'Moyen de paiement', customer: 'Client', consent: 'Consentement', stock_item: 'Article de stock', feedback: 'Avis',
  survey: 'Questionnaire', api_credential: 'Clé d’API', webhook_endpoint: 'Webhook', notification_configuration: 'Canal d’envoi',
  notification: 'Notification', subscription: 'Abonnement', export: 'Export',
};

/** Couleurs des domaines (pastille) : texte, fond. */
export const moduleTone: Record<string, string> = {
  tenants: 'bg-indigo-50 text-indigo-700', staff: 'bg-orange-50 text-orange-700', identity: 'bg-sky-50 text-sky-700',
  payments: 'bg-emerald-50 text-emerald-700', customers: 'bg-pink-50 text-pink-700', inventory: 'bg-lime-50 text-lime-700',
  reviews: 'bg-amber-50 text-amber-700', integrations: 'bg-slate-100 text-slate-700', notifications: 'bg-violet-50 text-violet-700',
  billing: 'bg-teal-50 text-teal-700', reporting: 'bg-cyan-50 text-cyan-700',
};

/** Les actions connues d'un domaine (ou de tous), pour le filtre « Toutes les actions ». */
export const actionsOf = (module: string) =>
  Object.entries(actions).filter(([code]) => !module || code.startsWith(`${module}.`)).sort((a, b) => a[1].localeCompare(b[1], 'fr'));

export const loadAuditEntry = (id: string, signal?: AbortSignal) => api<AuditEntry>(`/audit/${id}`, {signal});
