# Module Staff & RBAC

But : gérer l'appartenance des employés à un tenant et leurs autorisations opérationnelles.

## 1. Frontières d'agrégat

| Agrégat | Contenu | Pourquoi |
|---|---|---|
| `StaffMember` (racine) | statut + liste de `StaffAssignment` (VO `LocationId` + `RoleId`) + au plus un rôle **tenant-wide** (`TenantWideRoleId`) | Les règles « pas de double affectation », « un seul rôle tenant-wide », « Left ne peut agir », « Suspended ne peut être affecté » portent toutes sur un seul profil : il est l'unité de cohérence. |
| `Role` (racine) | nom + ensemble de `PermissionCode` | Un rôle est modifié indépendamment des employés qui le portent ; le lier à `StaffMember` verrouillerait tous les employés à chaque Grant. |

Références par identifiant uniquement : `UserId` (Identity), `LocationId` (Locations), `RoleId` (entre `StaffMember` et `Role`).
Aucune navigation d'objet. Pour prouver l'appartenance au même tenant, les méthodes d'affectation reçoivent de petites
références portant le `TenantId` de l'agrégat référencé :
- `RoleReference` : **constructeur `internal`**, seul `Role.ToReference()` la produit (le rôle vit dans ce module, on ne
  peut donc pas forger le tenant d'un rôle) ;
- `LocationReference(TenantId, LocationId, IsOperational)` : reste publique car la location vit dans le module Locations ;
  l'Application la construit à partir du `TenantId`, de l'`Id` et du statut de la location chargée là-bas, jamais d'une
  saisie utilisateur. Ses deux ids sont gardés (`Guard.NotEmpty`), `IsOperational` = la location est `Active`.

Le « dernier propriétaire » (ne pas retirer/suspendre le dernier owner actif du tenant) porte sur **plusieurs** profils :
ce n'est pas un invariant de l'agrégat, voir § 5.

## 2. Classes

| Classe | Rôle DDD | Responsabilité | Transitions | Events | Erreurs |
|---|---|---|---|---|---|
| `StaffMember` | AggregateRoot, tenant-owned | Cycle de vie du profil, affectations par location et rôle tenant-wide | `(Invite) -> Invited` ; `(CreateOwner) -> Active` ; `Invited -> Active` (Activate) ; `Active -> Suspended` (Suspend) ; `Suspended -> Active` (Reinstate) ; `Invited/Active/Suspended -> Left` (Leave, terminal) | `StaffInvited`, `StaffOwnerCreated`, `StaffActivated`, `StaffAssignedToLocation`, `StaffRemovedFromLocation`, `StaffAssignedTenantWide`, `StaffTenantWideAssignmentRemoved`, `StaffSuspended`, `StaffReinstated`, `StaffLeft` | `staff.staff_member.user_already_in_tenant`, `.has_left`, `.not_invited`, `.not_active`, `.not_suspended`, `.suspended_cannot_be_assigned`, `.location_not_operational`, `.already_assigned_to_location`, `.not_assigned_to_location`, `.already_assigned_tenant_wide`, `.not_assigned_tenant_wide`, `common.tenant.mismatch`, `common.id.empty` |
| `StaffAssignment` | ValueObject | Une affectation = (location, rôle) | — | — | — |
| `RoleReference` | ValueObject (référence, ctor `internal`) | Porter le `TenantId` du rôle ; produite uniquement par `Role.ToReference()` | — | — | — |
| `LocationReference` | ValueObject (référence, publique) | Porter le `TenantId` de la location (module Locations) et son caractère opérationnel ; construite par l'Application | — | — | `common.id.empty` |
| `StaffStatus` | Enum | Invited / Active / Suspended / Left | — | — | — |
| `Role` | AggregateRoot, tenant-owned | Nom et permissions d'un rôle ; protection des rôles système | — (pas d'état) | `RoleCreated`, `RoleRenamed`, `RolePermissionGranted`, `RolePermissionRevoked` | `staff.role.name_required`, `.name_too_long`, `.name_already_used`, `.system_role_protected`, `.permission_unknown`, `.permission_already_granted`, `.permission_not_granted` |
| `RoleName` | ValueObject | Nom canonique (NFKC, sans caractères de contrôle/format, espaces réduits, trimé), 1..80 caractères, comparé sans casse | — | — | `staff.role.name_required`, `.name_too_long` |
| `PermissionCode` | ValueObject | Code stable `orders.cancel` | — | — | `staff.permission_code.invalid_format` |
| `PermissionCatalog` | ValueObject | Liste explicite des permissions connues | — | — | `staff.permission_catalog.empty` |
| `IStaffMemberUniquenessChecker`, `IRoleNameUniquenessChecker` | Ports | Unicité inter-agrégats | — | — | — |

## 3. Choix faits là où la spec était ouverte

- **Double affectation** : une location ne peut apparaître qu'une fois par employé (donc un seul rôle par location). Changer de rôle = `RemoveFromLocation` puis `AssignToLocation`.
- **Qui peut être affecté** : `Invited` et `Active` (on prépare les accès avant l'acceptation de l'invitation) ; `Suspended` refusé ; `Left` refusé. `RemoveFromLocation` reste possible pendant une suspension (retirer un accès est toujours permis).
- **`Reinstate`** ajouté (`Suspended -> Active`) : la spec ne donnait aucune sortie de suspension. `Activate` reste réservé à `Invited`.
- **`Leave`** vide les affectations **et le rôle tenant-wide** (un employé parti ne doit plus rien conférer) ; le profil est conservé pour l'historique. `StaffLeft` porte `RemovedLocationIds` et `HeldTenantWideRole` : une projection se met à jour sans recharger le membre (aucun `StaffRemovedFromLocation` / `StaffTenantWideAssignmentRemoved` n'est levé en plus).
- **Rôle tenant-wide** : au plus un par profil (`AssignTenantWide` / `RemoveTenantWide`), mêmes règles de statut que les affectations par location (affectable en `Invited`/`Active`, refusé en `Suspended`/`Left` ; retrait permis sauf `Left`). Changer de rôle = `RemoveTenantWide` puis `AssignTenantWide` ; réaffecter le même rôle est rejeté (transition répétée, pas un setter). Il porte les droits des actions de niveau tenant (facturation, rôles, invitations, identifiants d'API, création d'une location).
- **`EffectiveTenantRole`** : rôle tenant-wide d'un membre `Active`, sinon `null`. `TenantWideRoleId` expose la valeur stockée quel que soit le statut.
- **`EffectiveRoleAt(locationId)`** : seul un membre `Active` a un rôle effectif ; une suspension conserve les affectations mais les neutralise. **Précédence** : le rôle affecté à cette location l'emporte ; à défaut, le rôle tenant-wide s'applique ; les deux ne sont jamais fusionnés (un rôle local plus restreint restreint donc réellement, sur cette location, un membre qui a aussi un rôle tenant-wide).
- **Premier propriétaire** : `StaffMember.CreateOwner(tenantId, userId, ownerRole, isUserUniqueInTenant, clock)` crée en une étape un membre `Active` portant le rôle tenant-wide (personne n'existe encore pour l'inviter, aucune location n'existe encore). Événements, dans l'ordre : `StaffOwnerCreated` (création — et non `StaffInvited`, pour qu'aucun e-mail d'invitation ne parte), `StaffActivated`, `StaffAssignedTenantWide`. `InvitedAtUtc` vaut alors la date de création. La fabrique ne vérifie pas que le rôle est « Owner » : une `RoleReference` ne porte que tenant + id, le choix du rôle système relève de l'onboarding.
- **Pas d'idempotence** : suspendre un suspendu, accorder une permission déjà accordée, révoquer une permission absente sont des erreurs.
- **Permissions connues** : `Grant` reçoit un `PermissionCatalog` explicite, assemblé par l'Application à partir des permissions déclarées par chaque module (Staff ne connaît pas les autres modules). `Revoke` ne demande pas de catalogue : on doit pouvoir retirer une permission devenue obsolète.
- **Rôle système** : créé par `Role.CreateSystem(...)` avec ses permissions (validées contre le catalogue), puis `Rename`/`Grant`/`Revoke` refusés. Faire évoluer un rôle système est une opération de plateforme (migration), pas un cas d'usage tenant.
- **`PermissionCode`** : au moins deux segments séparés par des points, chaque segment `[a-z][a-z0-9_]*`, 100 caractères max, espaces de bord retirés, aucune mise en minuscules automatique (une majuscule est rejetée).
- **Location opérationnelle** : `AssignToLocation` exige `LocationReference.IsOperational` (`staff.staff_member.location_not_operational`) — on n'affecte personne à une location fermée ou archivée. `RemoveFromLocation` ne prend qu'un `LocationId` : retirer un accès reste possible après la fermeture.
- **Ids vides** : `Invite`, `CreateOwner` (tenant, user), `Role.Create`/`CreateSystem` (tenant) et `LocationReference` (tenant, location) rejettent un id `default` avec `common.id.empty`.
- **Forme canonique du nom de rôle** : `RoleName.Create` applique, dans l'ordre, la normalisation Unicode NFKC, la suppression des caractères de contrôle et de format (zéro-largeur, BOM, marques directionnelles ; un substitut UTF-16 isolé est écarté au lieu de faire échouer la normalisation), la réduction de toute suite d'espaces (tabulation, insécable...) à un seul espace, puis le trim ; le vide et la longueur sont testés **après**. « Chef  de rang » et « Chef de rang » sont donc le même nom, et un nom fait uniquement d'invisibles est refusé (`staff.role.name_required`). `Value` stocke cette forme.
- **Nom de rôle** : unicité insensible à la casse ; un simple changement de casse du même nom ne requiert pas la vérification d'unicité ; renommer à l'identique est un no-op sans événement.
- Événements ajoutés : `StaffRemovedFromLocation`, `StaffReinstated`, `StaffLeft`, `StaffOwnerCreated`, `StaffAssignedTenantWide`, `StaffTenantWideAssignmentRemoved`, `RoleCreated`, `RoleRenamed`.
- **Même valeur = no-op silencieux** : le seul setter du module est `Role.Rename` (déjà no-op sans événement à l'identique). Tout le reste est une transition et reste rejeté quand elle est répétée.

## 4. Matrice invariant -> méthode -> test

| Invariant | Méthode | Test |
|---|---|---|
| UserId unique par tenant | `StaffMember.Invite` | `StaffMemberTests.Invite_rejects_a_user_who_already_has_a_profile_in_the_tenant` |
| Invitation crée un profil Invited + event | `Invite` | `StaffMemberTests.Invite_creates_an_invited_member_and_raises_StaffInvited` |
| Activation seulement depuis Invited | `Activate` | `StaffMemberTests.Activate_moves_an_invited_member_to_active_and_raises_StaffActivated`, `Activate_rejects_an_already_active_member`, `Activate_rejects_a_suspended_member` |
| Pas de double affectation à une location | `AssignToLocation` | `StaffMemberTests.AssignToLocation_rejects_a_second_assignment_to_the_same_location`, `AssignToLocation_rejects_the_exact_same_location_and_role_twice`, `AssignToLocation_accepts_several_distinct_locations` |
| Location du même tenant | `AssignToLocation` | `StaffMemberTests.AssignToLocation_rejects_a_location_of_another_tenant` |
| Location opérationnelle (ni fermée ni archivée) | `AssignToLocation` | `StaffMemberTests.AssignToLocation_rejects_a_location_that_is_not_operational`, `RemoveFromLocation_does_not_need_the_location_to_be_operational` |
| Ids non vides (tenant, user, location) | `Invite`, `CreateOwner`, `LocationReference`, `Role.Create`, `CreateSystem` | `StaffMemberTests.Invite_rejects_an_empty_tenant_or_user`, `CreateOwner_rejects_an_empty_tenant_or_user`, `LocationReference_rejects_empty_ids`, `RoleTests.Create_rejects_an_empty_tenant` |
| Rôle du même tenant | `AssignToLocation` | `StaffMemberTests.AssignToLocation_rejects_a_role_of_another_tenant` |
| Suspended ne peut pas être affecté | `AssignToLocation` | `StaffMemberTests.AssignToLocation_rejects_a_suspended_member` |
| Retrait d'une affectation existante uniquement | `RemoveFromLocation` | `StaffMemberTests.RemoveFromLocation_removes_the_assignment_and_raises_StaffRemovedFromLocation`, `RemoveFromLocation_rejects_a_location_the_member_is_not_assigned_to` |
| Suspension seulement depuis Active | `Suspend` | `StaffMemberTests.Suspend_moves_an_active_member_to_suspended_and_raises_StaffSuspended`, `Suspend_rejects_an_invited_member`, `Suspend_rejects_an_already_suspended_member` |
| Suspension neutralise le rôle effectif | `Suspend`, `EffectiveRoleAt` | `StaffMemberTests.Suspend_keeps_assignments_but_removes_the_effective_role` |
| Réintégration seulement depuis Suspended | `Reinstate` | `StaffMemberTests.Reinstate_moves_a_suspended_member_back_to_active`, `Reinstate_rejects_a_member_who_is_not_suspended` |
| Left accessible depuis tout état non terminal, vide les affectations | `Leave` | `StaffMemberTests.Leave_is_allowed_from_every_non_terminal_status`, `Leave_drops_every_assignment` |
| Left retire aussi le rôle tenant-wide ; `StaffLeft` liste ce qui a été retiré | `Leave` | `StaffMemberTests.Leave_clears_the_tenant_wide_role_and_StaffLeft_lists_what_was_removed` |
| Premier propriétaire : Active + rôle tenant-wide en une étape, sans invitation | `StaffMember.CreateOwner` | `StaffMemberTests.CreateOwner_builds_an_active_member_holding_the_tenant_wide_owner_role`, `CreateOwner_lets_the_founder_act_before_any_location_exists`, `An_owner_follows_the_normal_lifecycle_afterwards` |
| Owner : UserId unique par tenant, rôle du même tenant | `CreateOwner` | `StaffMemberTests.CreateOwner_rejects_a_user_who_already_has_a_profile_in_the_tenant`, `CreateOwner_rejects_a_role_of_another_tenant` |
| Au plus un rôle tenant-wide | `AssignTenantWide` | `StaffMemberTests.AssignTenantWide_sets_the_role_and_raises_StaffAssignedTenantWide`, `AssignTenantWide_rejects_a_second_tenant_wide_role` |
| Rôle tenant-wide du même tenant | `AssignTenantWide` | `StaffMemberTests.AssignTenantWide_rejects_a_role_of_another_tenant` |
| Rôle tenant-wide : mêmes règles de statut que par location | `AssignTenantWide`, `RemoveTenantWide` | `StaffMemberTests.AssignTenantWide_is_allowed_while_still_invited_but_not_effective`, `AssignTenantWide_rejects_a_suspended_member`, `RemoveTenantWide_is_allowed_while_suspended_and_then_a_new_role_can_be_assigned_once_reinstated` |
| Retrait d'un rôle tenant-wide existant uniquement | `RemoveTenantWide` | `StaffMemberTests.RemoveTenantWide_clears_the_role_and_raises_StaffTenantWideAssignmentRemoved`, `RemoveTenantWide_rejects_a_member_without_tenant_wide_role` |
| Précédence : rôle de la location, sinon rôle tenant-wide | `EffectiveRoleAt` | `StaffMemberTests.EffectiveRoleAt_prefers_the_location_role_and_falls_back_to_the_tenant_wide_role` |
| Rôle tenant effectif seulement si Active, indépendant des locations | `EffectiveTenantRole` | `StaffMemberTests.Suspend_neutralises_the_tenant_wide_role_and_Reinstate_restores_it`, `EffectiveTenantRole_ignores_location_assignments` |
| `RoleReference` non forgeable | `Role.ToReference` | `RoleTests.ToReference_carries_the_role_tenant_and_id`, `A_role_reference_can_only_be_built_by_its_role` |
| Left est terminal | toutes | `StaffMemberTests.Left_member_rejects_every_action` |
| Nom de rôle requis, borné | `Role.Create`, `Rename` | `RoleTests.Create_rejects_a_missing_name`, `Create_enforces_the_name_length_limit`, `Rename_rejects_an_empty_name` |
| Nom de rôle canonique (NFKC, sans invisibles, espaces réduits) avant test de vide/longueur | `RoleName.Create` | `RoleTests.RoleName_collapses_internal_whitespace_so_spacing_variants_are_one_name`, `RoleName_applies_compatibility_normalisation_before_comparing`, `RoleName_removes_control_and_format_characters`, `RoleName_rejects_a_name_made_only_of_invisible_characters`, `RoleName_drops_unpaired_surrogates_instead_of_failing_on_ill_formed_text`, `RoleName_measures_the_length_after_normalisation` |
| Nom unique par tenant | `Role.Create`, `CreateSystem`, `Rename` | `RoleTests.Create_rejects_a_name_already_used_in_the_tenant`, `CreateSystem_rejects_a_name_already_used_in_the_tenant`, `Rename_rejects_a_name_already_used_in_the_tenant`, `Rename_to_a_different_casing_of_its_own_name_does_not_need_uniqueness` |
| Permissions connues | `Grant`, `CreateSystem` | `RoleTests.Grant_rejects_a_permission_outside_the_catalog`, `CreateSystem_rejects_a_permission_outside_the_catalog` |
| Grant / Revoke | `Grant`, `Revoke` | `RoleTests.Grant_adds_a_known_permission_and_raises_RolePermissionGranted`, `Grant_rejects_a_permission_already_granted`, `Revoke_removes_a_granted_permission_and_raises_RolePermissionRevoked`, `Revoke_rejects_a_permission_that_was_never_granted`, `Revoke_then_Grant_restores_the_permission` |
| Rôle système protégé | `Rename`, `Grant`, `Revoke` | `RoleTests.System_role_rejects_rename_grant_and_revoke` |
| Permissions non modifiables de l'extérieur | `Permissions` | `RoleTests.Permissions_cannot_be_mutated_from_outside` |
| Format du code de permission | `PermissionCode.Create` | `PermissionCodeTests.Create_accepts_lowercase_dotted_segments`, `Create_rejects_an_invalid_format`, `Create_enforces_the_length_limit` |
| Catalogue non vide, codes valides, élément `null` = `ArgumentNullException` | `PermissionCatalog.Create/Of` | `PermissionCatalogTests.Create_rejects_an_empty_catalog`, `Of_rejects_an_invalid_code`, `Create_rejects_a_null_code_as_a_programming_error` |

## 5. Application / Infrastructure

- **Unicité** : `IStaffMemberUniquenessChecker` et `IRoleNameUniquenessChecker` sont appelés par l'Application, qui passe le booléen à l'agrégat. Le garde-fou final est un index unique en base : `(TenantId, UserId)` pour les profils (profils `Left` compris : la réembauche d'un ancien employé n'est pas spécifiée), `(TenantId, upper(Name))` pour les rôles (`Name` est déjà la forme canonique produite par `RoleName` ; le port `IRoleNameUniquenessChecker` reçoit un `RoleName`, donc compare la même forme).
- **Références** : l'Application charge le rôle sous isolation tenant et appelle `Role.ToReference()` (seule façon d'obtenir une `RoleReference`, le domaine le garantit). Pour la location, elle charge la location via le contrat du module Locations et construit `new LocationReference(location.TenantId, location.Id, IsOperational: location.Status == Active)` — jamais depuis une saisie utilisateur ; c'est le seul point où le domaine fait confiance à l'Application.
- **Dernier propriétaire (règle inter-agrégats, NON portée par le domaine)** : avant `RemoveTenantWide`, `Suspend` ou `Leave` sur un membre portant le rôle Owner tenant-wide, l'Application doit vérifier qu'il reste au moins un **autre** membre `Active` portant ce rôle tenant-wide dans le tenant, et refuser sinon. Elle le fait par requête (compte des owners actifs) sous verrou / dans la même transaction, car un agrégat seul ne peut pas le prouver.
- **Onboarding d'un tenant** : 1) créer les rôles système (`Role.CreateSystem` : Owner, Manager, ...) ; 2) `StaffMember.CreateOwner(tenantId, userId, ownerRole.ToReference(), ...)` pour le fondateur ; 3) il peut alors créer la première location (son `EffectiveTenantRole` / `EffectiveRoleAt` répondent déjà).
- **Catalogue des permissions** : les codes de permission sont **déclarés par l'Application sous forme de constantes, par module** (ex. `orders.cancel`, `billing.manage`), puis assemblés en un `PermissionCatalog` unique fourni à `Grant` / `CreateSystem`. Staff ne connaît aucun autre module.
- **Rôles système et nouvelles permissions** : les rôles système (Owner, Manager, ...) sont créés par `Role.CreateSystem` à l'onboarding du tenant. Quand une nouvelle permission est ajoutée au catalogue, elle atteint les rôles système **existants** par une migration de plateforme (script/job qui met à jour les données) : l'agrégat continue de refuser `Grant`/`Revoke`/`Rename` sur un rôle système, ce n'est pas un cas d'usage tenant.
- **Suppression d'un rôle** : non spécifiée ; elle exigerait de vérifier qu'aucune affectation ne le référence (requête inter-agrégats).
- **Autorisation effective** : la résolution « utilisateur -> permissions sur une location » (lecture de `EffectiveRoleAt` pour une action sur une location, de `EffectiveTenantRole` pour une action de niveau tenant, + `Role.Permissions` ; cache, invalidation sur `RolePermissionGranted/Revoked`, `StaffAssignedToLocation`, `StaffRemovedFromLocation`, `StaffAssignedTenantWide`, `StaffTenantWideAssignmentRemoved`, `StaffSuspended`, `StaffReinstated`, `StaffLeft`) relève de l'Application.
- Concurrence : verrouillage optimiste (version) sur `StaffMember` et `Role`.

## 6. Application (`Staff.Application`)

| Cas d'usage | Qui | Effet | Erreurs propres |
|---|---|---|---|
| `OnboardTenantOwner(tenantId, email)` | **plateforme** (`POST /tenants/{tenantId}/owners`) | Crée les rôles système du tenant s'ils manquent (`Owner` = tout le catalogue, `Manager` = tout sauf `staff.manage` et `roles.manage`), trouve ou invite le compte Identity, crée un owner **Active** | `staff.tenant.not_found`, `staff.tenant.not_writable` |
| `InviteStaff(email, tenantWideRoleId?)` | `staff.manage` | Profil `Invited` (compte Identity créé et invité s'il n'existe pas), rôle tenant-wide facultatif | `staff.role.not_found` |
| `AcceptStaffInvitation(tenantId)` | l'invité, sans restaurant sélectionné | `Invited -> Active` : il peut ensuite sélectionner ce restaurant | `staff.invitation.not_found` |
| `AssignStaffToLocation`, `RemoveStaffFromLocation`, `AssignTenantWideRole`, `RemoveTenantWideRole` | `staff.manage` | Affectations (location vérifiée via le module Locations) | `staff.location.not_found`, `staff.role.not_found` |
| `SuspendStaff`, `ReinstateStaff`, `RemoveStaff` (Leave) | `staff.manage` | Cycle de vie | `staff.staff_member.last_owner` |
| `CreateRole`, `RenameRole`, `GrantPermission`, `RevokePermission` | `roles.manage` | Rôles personnalisés ; permissions limitées au catalogue | — |
| `ListStaff`, `GetStaffMember`, `ListRoles` / `GetPermissionCatalog` | `staff.manage` / `roles.manage` | Lecture | `staff.paging.invalid` |
| `ListMyMemberships` | l'utilisateur, sans restaurant | Ses restaurants (profils non `Left`) | — |
| `GetMyPermissions` | tout membre du restaurant sélectionné | Ses permissions de niveau tenant (pour le front) | `staff.staff_member.not_found` |
| `IsActiveStaffMember` | l'hôte | Réponse à Identity pour `POST /me/tenant` | — |

- **Dernier propriétaire** (règle inter-agrégats, §5) : `StaffWorkspace.EnsureAnotherActiveOwnerAsync` avant `Suspend`,
  `Leave` et `RemoveTenantWide` d'un owner actif. Deux retraits simultanés ne passent pas tous les deux : chacun
  **réécrit la ligne du rôle Owner** (`IRoleRepository.GuardDecisionsOn`), le second échoue en conflit (409).
- **Catalogue des permissions** : chaque module déclare ses codes (`StaffPermissions`, `LocationsPermissions`…), l'hôte
  les passe à `AddStaffModule` ; c'est le seul ensemble qu'un rôle peut recevoir (`KnownPermissions`).
- **Autorisation** : `StaffPermissionChecker` implémente `IPermissionChecker` (BuildingBlocks) — rôle tenant-wide effectif
  pour une action de niveau tenant, rôle de la location sinon tenant-wide pour une action sur une location. **Sans
  cache** : une révocation, une suspension ou un départ s'applique à la requête suivante (une requête indexée par
  appel protégé).
- Ports fournis par l'hôte : `IStaffTenants` (Tenants), `ILocationDirectory` (Locations), `IUserAccounts` (Identity).
- Liste du personnel : ids d'utilisateurs, pas d'emails (Identity ne les expose pas encore aux autres modules).

## 7. Infrastructure (`Staff.Infrastructure`)

- Schéma `staff` : `staff_members`, `staff_assignments` (possédée, unique `(staff_member_id, location_id)`), `roles`
  (colonne générée `normalized_name = upper(name)`, unique `(tenant_id, normalized_name)`), `role_permissions`
  (possédée, clé `(role_id, code)`), `outbox_messages`. Unique `(tenant_id, user_id)` sur les profils.
- **Tenant explicite partout** : Staff est lu hors requête tenant (sélection du restaurant, onboarding, « mes
  restaurants »). Les repositories contournent donc le filtre de requête de `TenantDbContext` **à cet endroit
  seulement** et le remplacent toujours par un prédicat explicite sur le tenant (ou l'utilisateur).
- Violations d'unicité traduites en erreurs métier ; concurrence optimiste (`xmin`) sur `staff_members` et `roles`,
  réécrits aussi quand seules leurs lignes possédées changent.
- **Migration initiale à générer** :
  `dotnet ef migrations add InitialCreate --project src/Modules/Staff/Staff.Infrastructure --startup-project src/Modules/Staff/Staff.Infrastructure --output-dir Persistence/Migrations`
  (`database update` lit `STAFF_DB`).

## 8. API (`Staff.Api`)

`MapStaffEndpoints()` rend trois groupes, l'hôte y chaîne la protection :

| Groupe | Protection (hôte) | Routes |
|---|---|---|
| `/staff` | `tenant-member` + permission par route | `GET /me/permissions` ; `GET /members`, `GET /members/{id}`, `POST /members/invitations`, `POST /members/{id}/locations`, `DELETE /members/{id}/locations/{locationId}`, `PUT`/`DELETE /members/{id}/tenant-role`, `POST /members/{id}/suspend`, `/reinstate`, `DELETE /members/{id}` (`staff.manage`) ; `GET /roles` (`staff.manage`) ; `GET /permissions`, `POST /roles`, `PUT /roles/{id}/name`, `POST /roles/{id}/permissions`, `DELETE /roles/{id}/permissions/{code}` (`roles.manage`) |
| `/me/memberships` | utilisateur connecté | `GET /` (`tenantId`, `tenantName`, `staffMemberId`, `status`, triés par nom ; le nom vient de Tenants via `IStaffTenants.GetNameAsync`), `POST /{tenantId}/accept` |
| `/tenants/{tenantId}/owners` | policy plateforme | `POST /` `{ email }` |

**Parcours complet** : la plateforme crée le tenant (`POST /tenants`) puis son owner (`POST /tenants/{id}/owners`) →
l'owner se connecte, `POST /me/tenant { tenantId }` → il invite (`POST /staff/members/invitations`) → l'invité crée
son mot de passe (lien Identity), se connecte, accepte (`POST /me/memberships/{tenantId}/accept`), sélectionne le
restaurant.

Tests : `Staff.Application.Tests` (fakes), `Staff.Infrastructure.Tests` et `Staff.Api.Tests` (vrai PostgreSQL ;
**migration requise**). L'API est testée avec le vrai vérificateur de permissions de Staff derrière les vraies policies.


## 9. Planning de l'équipe (`Domain/Scheduling`, `Application/Scheduling`)

- **`ScheduleWeek`** : une semaine (lundi → dimanche, heure locale) d'un établissement. Le gérant édite le **brouillon**
  (`Shifts`) ; l'équipe ne voit que la **version publiée** (`PublishedShifts`). Les deux listes sont en jsonb
  (`staff.schedule_weeks`), une ligne unique par (tenant, établissement, lundi).
- **`Shift`** : membre, jour, début, fin (fin ≤ début = le lendemain), pause déduite, poste libre, note. 15 min à 16 h.
  Deux créneaux d'un même membre ne se chevauchent pas, même autour de minuit.
- **Publier** copie le brouillon et lève `ScheduleWeekPublished` avec les seuls membres dont les créneaux ont changé ;
  l'hôte les prévient (console, puis WhatsApp, à défaut SMS, sur le téléphone du compte). Le message ne contient que
  leurs propres horaires.
- **Copier la semaine précédente** : seulement dans une semaine vide, sans les membres qui ne travaillent plus ici.
- **`ScheduleSettings`** par établissement : postes proposés (20 au plus) et plafond d'heures hebdomadaire (alerte).
- **`TimeOffRequest`** : congé ou indisponibilité, en jours entiers (92 au plus), sans motif médical demandé. Le membre
  demande, le gérant valide ou refuse (motif facultatif) ; saisie par le gérant = validée d'emblée. Un créneau sur une
  absence est signalé, pas refusé.
- **`ShiftSwapRequest`** : un membre propose un créneau publié à un collègue du même établissement → le collègue accepte
  → le gérant valide : `ScheduleWeek.Reassign` change le titulaire dans le brouillon ET la version publiée, dans la même
  transaction. Une demande ouverte expire quand le créneau commence (`StatusAt`), sans worker.
- **Prévu / pointé** (`GetScheduleVarianceQuery`) : chaque créneau publié est rapproché des pointages du même membre qui
  le chevauchent (à 2 h près) ; retard et départ anticipé au-delà de 5 min ; les autres pointages sont « hors planning ».
- Les heures locales sont converties avec le fuseau de l'établissement (`IScheduleLocations`, servi par l'hôte).
- Permission **`staff.schedule`** (le rôle Manager la reçoit au démarrage via `CompleteSystemRolesCommand`). Chaque membre
  agit pour lui-même sous `/staff/me/...` : son profil vient du token, jamais d'un id envoyé.

| Méthode | Route | Permission |
|---|---|---|
| `GET` · `PUT` | `/staff/schedule/settings/{locationId}` | `staff.schedule` |
| `GET` | `/staff/schedule/weeks/{locationId}/{weekStart}` (+ `/variance`) | `staff.schedule` |
| `POST` · `PUT` · `DELETE` | `/staff/schedule/weeks/{locationId}/{weekStart}/shifts[/{shiftId}]` | `staff.schedule` |
| `POST` | `/staff/schedule/weeks/{locationId}/{weekStart}/copy-previous` · `/publish` | `staff.schedule` |
| `GET` · `POST` | `/staff/schedule/time-off` ; `POST /time-off/{id}/approve` · `/reject` · `/cancel` | `staff.schedule` |
| `GET` | `/staff/schedule/swaps?openOnly` ; `POST /swaps/{id}/approve` · `/reject` | `staff.schedule` |
| `GET` | `/staff/me/schedule?from&weeks` | membre |
| `POST` | `/staff/me/time-off` ; `/staff/me/time-off/{id}/cancel` | membre |
| `POST` | `/staff/me/swaps` ; `/staff/me/swaps/{id}/accept` · `/decline` · `/cancel` | membre |

Migration : `TeamSchedule` (script `scripts/migrations-planning.sh`). Lien des messages : `Staff:ConsoleBaseUrl`, à
défaut l'origine de `Identity:Emails:Links:AcceptInvitation`.
