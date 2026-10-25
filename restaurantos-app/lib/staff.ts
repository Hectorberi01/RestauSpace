import {all, api, del, post, put} from './api';

export type StaffStatus = 'Invited' | 'Active' | 'Suspended' | 'Left';

export interface StaffMember {
  id: string;
  userId: string;
  status: StaffStatus;
  tenantWideRoleId: string | null;
  assignments: {locationId: string; roleId: string}[];
  invitedAtUtc: string;
  email: string | null;
  hasPin?: boolean;
  /** Au-delà de la limite du plan : connexion au restaurant et caisse refusées. */
  isFrozen?: boolean;
}

export interface Role {
  id: string;
  name: string;
  isSystem: boolean;
  permissions: string[];
}

export const statusLabel: Record<StaffStatus, string> = {Invited: 'Invité', Active: 'Actif', Suspended: 'Suspendu', Left: 'Parti'};
export const statusTone: Record<StaffStatus, 'amber' | 'green' | 'red' | 'neutral'> = {Invited: 'amber', Active: 'green', Suspended: 'red', Left: 'neutral'};

/** Noms français des rôles système (le nom stocké reste « Owner » / « Manager »). */
export function roleName(role: Role | undefined | null): string {
  if (!role) return 'Sans rôle';
  if (role.isSystem && role.name === 'Owner') return 'Propriétaire';
  if (role.isSystem && role.name === 'Manager') return 'Gérant';
  return role.name;
}

export const permissionGroups: {key: string; label: string}[] = [
  {key: 'orders', label: 'Commandes'},
  {key: 'kitchen', label: 'Cuisine'},
  {key: 'floor', label: 'Salle & tables'},
  {key: 'reservations', label: 'Réservations'},
  {key: 'delivery', label: 'Livraison & retrait'},
  {key: 'catalog', label: 'Menu & produits'},
  {key: 'inventory', label: 'Stock'},
  {key: 'payments', label: 'Paiements'},
  {key: 'customers', label: 'Clients'},
  {key: 'loyalty', label: 'Fidélité'},
  {key: 'promotions', label: 'Promotions'},
  {key: 'reviews', label: 'Avis'},
  {key: 'reporting', label: 'Rapports'},
  {key: 'notifications', label: 'Notifications'},
  {key: 'files', label: 'Fichiers & photos'},
  {key: 'locations', label: 'Établissements'},
  {key: 'tenant', label: 'Paramètres du restaurant'},
  {key: 'staff', label: 'Personnel'},
  {key: 'roles', label: 'Personnel'},
  {key: 'billing', label: 'Abonnement'},
  {key: 'integrations', label: 'Intégrations'},
  {key: 'storefront', label: 'Vente en ligne'},
  {key: 'printing', label: 'Imprimantes'},
  {key: 'fiscal', label: 'Facture normalisée'},
  {key: 'audit', label: 'Journal d’audit'},
];

export const permissionLabel: Record<string, string> = {
  'orders.take': 'Prendre des commandes',
  'orders.process': 'Traiter les commandes (accepter, préparer, terminer)',
  'orders.discount': 'Accorder des remises',
  'kitchen.operate': 'Utiliser l’écran cuisine',
  'kitchen.configure': 'Configurer les postes de cuisine',
  'floor.serve': 'Ouvrir et fermer les tables',
  'floor.manage': 'Configurer les salles et les tables',
  'reservations.manage': 'Gérer les réservations',
  'reservations.configure': 'Régler la politique de réservation',
  'tenant.configure': 'Modifier les paramètres du restaurant (nom, identité, devise, fuseau)',
  'delivery.operate': 'Suivre les livraisons et les retraits',
  'delivery.configure': 'Régler la livraison et le retrait',
  'delivery.couriers': 'Gérer les fiches livreurs',
  'delivery.remit': 'Faire les remises des livreurs (espèces, tarifs, écarts)',
  'catalog.manage': 'Gérer le menu et les produits',
  'inventory.operate': 'Saisir les mouvements de stock',
  'inventory.manage': 'Gérer le stock',
  'payments.collect': 'Encaisser',
  'payments.refund': 'Rembourser',
  'payments.configure': 'Configurer les moyens de paiement',
  'customers.view': 'Voir les clients',
  'customers.manage': 'Gérer les clients',
  'customers.anonymize': 'Anonymiser un client',
  'customers.configure': 'Régler la fiche client',
  'loyalty.view': 'Voir la fidélité',
  'loyalty.operate': 'Créditer et utiliser des points',
  'loyalty.manage': 'Gérer les comptes fidélité',
  'loyalty.configure': 'Configurer le programme de fidélité',
  'promotions.view': 'Voir les promotions',
  'promotions.apply': 'Appliquer une promotion',
  'promotions.manage': 'Gérer les promotions',
  'reviews.view': 'Voir les avis',
  'reviews.submit': 'Saisir un avis',
  'reviews.manage': 'Modérer les avis',
  'reporting.view': 'Voir les rapports',
  'reporting.export': 'Exporter les rapports',
  'reporting.manage': 'Gérer les rapports enregistrés',
  'notifications.view': 'Voir les notifications',
  'notifications.send': 'Envoyer des notifications',
  'notifications.configure': 'Configurer les notifications',
  'notifications.manage_preferences': 'Gérer les préférences de notification',
  'files.view': 'Voir les fichiers et photos',
  'files.upload': 'Envoyer des fichiers et photos',
  'files.manage': 'Supprimer des fichiers et photos',
  'locations.manage': 'Gérer les établissements',
  'staff.manage': 'Gérer le personnel',
  'staff.schedule': 'Établir le planning, valider absences et remplacements',
  'roles.manage': 'Gérer les rôles et les permissions',
  'billing.view': 'Voir l’abonnement',
  'billing.manage': 'Gérer l’abonnement',
  'integrations.view': 'Voir les intégrations',
  'integrations.manage': 'Gérer les intégrations',
  'audit.view': 'Consulter le journal d’audit',
  'storefront.view': 'Voir la vente en ligne',
  'storefront.manage': 'Gérer la vente en ligne (applications, clés, réglages, bornes)',
  'storefront.receipts': 'Envoyer les reçus aux clients (WhatsApp, SMS)',
  'printing.view': 'Voir les imprimantes et les derniers tickets',
  'printing.manage': 'Gérer les imprimantes (jetons, page de test, réimpression)',
  'fiscal.view': 'Voir la facture normalisée (e-MECeF) et son suivi',
  'fiscal.configure': 'Régler la facture normalisée (IFU, jeton DGI, environnement) et relancer une facture',
};

export function groupOf(code: string): string {
  const prefix = code.split('.')[0];
  return permissionGroups.find(group => group.key === prefix)?.label ?? 'Autres';
}

export const listMembers = () => all<StaffMember>('/staff/members', {}, 1000);
export const listRoles = (signal?: AbortSignal) => api<Role[]>('/staff/roles', {signal});
export const permissionCatalog = (signal?: AbortSignal) => api<string[]>('/staff/permissions', {signal});

export const invite = (email: string, roleId: string | null) => post<{id: string}>('/staff/members/invitations', {email, tenantWideRoleId: roleId});
export const setTenantRole = (memberId: string, roleId: string | null) =>
  roleId ? put(`/staff/members/${memberId}/tenant-role`, {roleId}) : del(`/staff/members/${memberId}/tenant-role`);
export const assign = (memberId: string, locationId: string, roleId: string) => post(`/staff/members/${memberId}/locations`, {locationId, roleId});
export const unassign = (memberId: string, locationId: string) => del(`/staff/members/${memberId}/locations/${locationId}`);
export const suspend = (memberId: string) => post(`/staff/members/${memberId}/suspend`);
export const reinstate = (memberId: string) => post(`/staff/members/${memberId}/reinstate`);
export const remove = (memberId: string) => del(`/staff/members/${memberId}`);
export const resendInvitation = (memberId: string) => post(`/staff/members/${memberId}/invitation`);

export const createRole = (name: string) => post<{id: string}>('/staff/roles', {name});
export const deleteRole = (id: string) => del(`/staff/roles/${id}`);
export const renameRole = (id: string, name: string) => put(`/staff/roles/${id}/name`, {name});
export const grant = (id: string, permission: string) => post(`/staff/roles/${id}/permissions`, {permission});
export const revoke = (id: string, permission: string) => del(`/staff/roles/${id}/permissions/${encodeURIComponent(permission)}`);
