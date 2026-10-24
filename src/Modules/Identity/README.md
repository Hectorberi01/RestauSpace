# Module Identity — Domain

Identité, credentials et sessions. Aucune permission métier du restaurant ici (module Staff), aucune notion de tenant.

## 1. Frontière d'agrégat

- **`User`** est la seule racine. Compte humain **global** : pas de `TenantId`, pas d'`ITenantOwned`.
- **`RefreshSession`** est une entité enfant : elle n'a aucun sens hors de son utilisateur, et les règles
  « Disabled ne peut ouvrir de session », « rotation », « replay => tout révoquer » exigent de voir *toutes* les
  sessions du compte dans la même transaction. Ses mutateurs sont `internal`.
- Référencé par id uniquement : rien. Les autres modules référencent `UserId` (shared kernel).
- `Email` vient du shared kernel. `PasswordHash` / `TokenHash` sont opaques : le domaine ne voit jamais un mot de
  passe ni un token brut (hachage et vérification = Application/Infrastructure). Leur `ToString()` est masqué.

## 2. Classes

| Classe | Rôle DDD | Responsabilité | Transitions / events | Erreurs |
|---|---|---|---|---|
| `User` | AggregateRoot | Cycle de vie du compte, credential, sessions | `Pending -> Active` (VerifyEmail, ou AcceptInvitation pour un invité sans mot de passe) ; `Pending/Active -> Locked` (Lock) ; `Locked -> Active` ou `Pending` (Unlock : statut d'avant le verrouillage, `StatusBeforeLock`) ; `* -> Disabled` (terminal). Events : `UserRegistered`, `UserInvited`, `UserInvitationAccepted`, `EmailVerified`, `UserEmailChanged`, `PasswordChanged`, `UserLocked`, `UserUnlocked`, `UserDisabled`, `SessionStarted`, `SessionRevoked`, `RefreshTokenRotated`, `RefreshTokenReplayDetected` | `identity.user.*`, `identity.session.*` |
| `RefreshSession` | Entity (enfant) | Session renouvelable/révocable : `Hash`, `ExpiresAtUtc`, `RevokedAtUtc?`, `ReplacedBy?`, `RevocationReason?`, `IsUsableAt` | `Usable -> Rotated` (RevokedAtUtc + ReplacedBy), puis retirée de l'agrégat quand sa remplaçante est tournée à son tour ; `Usable -> Revoked` ; expiration par le temps. Events levés par `User` | `identity.session.revoked`, `.already_revoked`, `.expired` |
| `SessionRotationResult` | Résultat | Issue de `RotateSession` : nouvelle session, ou replay détecté (+ erreur à renvoyer) | — | `identity.session.refresh_token_replayed` |
| `PasswordHash`, `TokenHash` | ValueObject | Hash opaque, non vide, sans espace, longueur bornée | — | `identity.password_hash.*`, `identity.token_hash.*` |
| `SessionLifetime` | ValueObject (policy) | Durée de vie d'une session (0 < d <= 365 j), défaut 30 j | — | `identity.session.lifetime_invalid` |
| `SessionId` | Id typé local | — | — | `common.id.empty` |
| `UserStatus`, `SessionRevocationReason` | Enums | Listes fermées | — | — |
| `IUserEmailUniquenessChecker` | Port | Unicité de l'email sur toute la plateforme | — | — |

## 3. Choix faits là où la spec était ouverte

- **« Active » = compte activé, pas « email courant vérifié » (règle unique).** Un compte devient `Active` en prouvant
  une adresse une fois (`VerifyEmail` / `AcceptInvitation`). `ChangeEmail` ne touche **jamais** au statut : le compte
  reste `Active`, garde ses sessions et peut en ouvrir de nouvelles ; seule la nouvelle adresse est « à vérifier »
  (`IsEmailVerified == false` jusqu'à `VerifyEmail`). `Unlock` **restaure le statut d'avant le verrouillage**
  (`StatusBeforeLock`, posé par `Lock`, remis à `null` par `Unlock`/`Disable`) au lieu de le recalculer depuis le
  drapeau de vérification : `ChangeEmail -> Lock -> Unlock` rend donc un compte `Active` (avant : rétrogradé en
  `Pending` en silence). Un compte `Pending` verrouillé puis vérifié pendant le verrouillage revient `Active`
  (`StatusBeforeLock` est promu), il ne reste pas coincé en `Pending`. `CanStartSession` = `Active` + mot de passe,
  sans condition sur `IsEmailVerified`.
- **Qui peut ouvrir/renouveler une session : `Active` uniquement.** `Disabled` jamais (spec). `Locked` non (c'est le
  but du verrouillage). `Pending` non : tant que l'email n'est pas prouvé, le compte peut avoir été créé sur
  l'adresse d'un tiers ; la vérification passe par un token à usage unique géré par l'Application, sans session.
  Exposé par `User.CanStartSession`.
- **Utilisateur invité sans mot de passe** : `User.RegisterInvited(email, isEmailUnique, clock)` crée un compte
  `Pending` avec `PasswordHash == null` (`HasPassword == false`) et lève `UserInvited` (et non `UserRegistered` :
  l'email à envoyer est une invitation, pas une vérification). « Pas encore de mot de passe » est donc explicite, plus
  de faux hash. `AcceptInvitation(passwordHash, clock)` pose le mot de passe **et** vérifie l'email en une étape
  (suivre le lien d'invitation prouve la possession de l'adresse) : `Pending -> Active`, events `EmailVerified` puis
  `UserInvitationAccepted` (pas de `PasswordChanged` : ce n'est pas un changement, `PasswordChangedAtUtc` reste nul).
  Tant que l'invitation n'est pas acceptée : `VerifyEmail`, `ChangePassword`, `StartSession`/`RotateSession` lèvent
  `identity.user.password_not_set` (aucun chemin ne mène à un compte `Active` sans mot de passe) ; `ChangeEmail`
  (correction de l'adresse invitée), `Lock`, `Unlock`, `Disable` restent possibles. `AcceptInvitation` sur un compte
  qui a déjà un mot de passe (inscription classique, ou invitation déjà acceptée) lève
  `identity.user.no_pending_invitation`. Sur un compte `Locked`, comme `VerifyEmail`, elle est acceptée et le compte
  reste `Locked` (il deviendra `Active` au `Unlock`).
- **Même valeur = no-op silencieux** : `ChangeEmail` avec l'email courant et `ChangePassword` avec le hash courant
  retournent sans mutation, sans event et sans révoquer de session (les erreurs `identity.user.email_unchanged` et
  `identity.user.password_unchanged` sont supprimées). Les gardes d'état passent avant (`Disabled`, `Locked` pour
  l'email, `password_not_set`). Les transitions répétées (`Lock` d'un compte verrouillé, `VerifyEmail` déjà vérifié,
  révocation d'une session révoquée...) restent rejetées.
- **Durée de session** : paramètre explicite `SessionLifetime` de `StartSession`/`RotateSession` (défaut
  `SessionLifetime.Default` = 30 j), glissante. Plafond absolu d'une **famille** (connexion puis rotations) :
  `IdentitySettings.SessionFamilyLifetime` (90 j, `Identity:Sessions:FamilyMaxDays`) ; une rotation ne dépasse jamais
  la fin de la famille (`identity.session.family_expired` si la session la dépasse déjà).
- **Rotation** : `RotateSession(tokenPrésenté, nouveauHash, lifetime, clock)` révoque la session présentée, renseigne
  `ReplacedBy`, crée la suivante, lève `RefreshTokenRotated` (pas de `SessionRevoked` : la rotation n'est pas une
  déconnexion).
- **Replay** : présenter un token **déjà tourné** (`ReplacedBy != null`) signifie que le token a fuité. Réponse
  explicite : **toutes les sessions de l'utilisateur sont révoquées** (raison `ReplayDetected`, un `SessionRevoked`
  par session) + `RefreshTokenReplayDetected`. Ce cas est **retourné** (`SessionRotationResult.ReplayDetected`,
  `Error = identity.session.refresh_token_replayed`) et non levé en exception : l'agrégat a muté et l'Application
  doit **persister** cette révocation avant de répondre 401 (une exception ferait annuler la transaction).
- Présenter un token révoqué **sans** rotation (logout, changement de mot de passe, lock) est simplement rejeté
  (`identity.session.revoked`) sans toucher aux autres sessions : ce n'est pas un signal de vol.
- **Familles et collection bornée** : un jeton renouvelé porte le premier jeton de sa famille
  (`{premier jeton}.{secret}`, `RefreshTokenFamily`). La session du premier jeton (l'**ancre**) est gardée, révoquée,
  jusqu'à la fin de la famille ; à chaque rotation, la génération intermédiaire est retirée. Il reste au plus 3
  sessions par famille (ancre, session remplacée, session vivante). Présenter N'IMPORTE QUEL ancien jeton : soit sa
  session existe encore (rejeu, ci-dessus), soit l'ancre le reconnaît (`RevokeAfterFamilyTokenReuse`) : dans les deux
  cas toutes les sessions de l'utilisateur sont révoquées. Aucune colonne ajoutée : l'ancre est une session ordinaire.
- **Session de caisse** (`StartOperatorSession`, PIN vérifié par Staff) : access token marqué `till`/`till_loc`,
  valable `IdentitySettings.TillSessionLifetime` (12 h, `Identity:Sessions:TillSessionHours`), SANS refresh token.
  Les routes `/me` lui sont fermées sauf `GET /me`, `GET /me/avatar` et la fin de sa propre session ; ses permissions
  sont limitées par l'hôte (liste blanche `HostComposition.TillPermissions`).
- `Sessions` contient les sessions conservées (vivantes, révoquées, dernière tournée de chaque chaîne).
  `ActiveSessionsAt(instant)` donne les sessions utilisables. `PurgeExpiredSessions` retire les sessions expirées,
  **sauf** une session tournée dont la remplaçante est encore utilisable (ni révoquée ni expirée) : la détection de
  replay ne dépend donc ni du moment ni de la fréquence de la purge. Elle est retirée (si expirée) dès que sa
  remplaçante est révoquée, expirée ou purgée.
- `ChangePassword` révoque toutes les sessions, sauf éventuellement `sessionToKeep` (celle depuis laquelle
  l'utilisateur change son mot de passe). Autorisé en `Locked` (reset avant déverrouillage). Même hash : no-op.
- `ChangeEmail` : autorisé en `Pending`/`Active` ; le nouvel email redevient **non vérifié**
  (`EmailVerifiedAtUtc = null`) mais un compte `Active` reste `Active`, y compris après un `Lock` + `Unlock`
  ultérieur. `VerifyEmail` vérifie l'email courant.
- `Lock` et `Disable` révoquent toutes les sessions. `Unlock` ne les restaure pas.
- `Disabled` est **terminal** (la spec ne prévoit pas de réactivation) : toute mutation lève `identity.user.disabled`.
  **Exception délibérée : `PurgeExpiredSessions`** est du ménage technique, autorisé dans **tous** les statuts,
  `Disabled` compris : il ne change aucun fait métier, ne lève aucun event, et un compte désactivé doit pouvoir
  être vidé de ses sessions expirées.
- **`RevokeSession` sur une session déjà expirée** : aucun changement d'état et **aucun** `SessionRevoked` (un fait qui
  ne change rien) — retour silencieux. Une session déjà révoquée (ou tournée) reste rejetée par
  `identity.session.already_revoked`, même expirée ; une session inconnue par `identity.session.not_found`.
- `RevokeAllSessions` sans session à révoquer : no-op sans event (ce n'est pas une erreur métier).
- Un même `TokenHash` ne peut identifier deux sessions d'un utilisateur.
- Non modélisé (hors spec) : compteur d'échecs de connexion / verrouillage automatique, MFA, device/IP de session.

## 4. Matrice invariant -> méthode -> test

| Invariant | Méthode | Test |
|---|---|---|
| Email valide et normalisé | `Email.Create` (kernel) | `UserTests.Register_creates_a_pending_user_with_a_normalized_email`, `UserTests.Register_cannot_be_reached_with_an_invalid_email` |
| Email unique | `User.Register`, `User.ChangeEmail` | `UserTests.Register_rejects_an_email_already_used`, `UserTests.ChangeEmail_rejects_an_email_already_used` |
| Hash de mot de passe opaque et non vide | `PasswordHash.Create` | `CredentialValueObjectsTests.PasswordHash_rejects_an_empty_value`, `..._rejects_whitespace_inside_the_value`, `..._accepts_the_maximum_length_and_rejects_one_more`, `..._is_compared_by_value_and_never_printed` |
| Hash de token opaque et non vide | `TokenHash.Create` | `CredentialValueObjectsTests.TokenHash_rejects_an_empty_value`, `TokenHash_is_compared_by_value_and_never_printed` |
| Nouveau compte = Pending + `UserRegistered` | `User.Register` | `UserTests.Register_raises_UserRegistered`, `UserTests.Register_gives_the_user_a_password` |
| Invité = Pending sans mot de passe + `UserInvited`, email unique | `User.RegisterInvited` | `UserTests.RegisterInvited_creates_a_pending_user_without_password`, `UserTests.RegisterInvited_raises_UserInvited_only`, `UserTests.RegisterInvited_rejects_an_email_already_used`, `UserTests.RegisterInvited_rejects_null_arguments` |
| Acceptation = mot de passe + email vérifié + Active, en une étape | `User.AcceptInvitation` | `UserTests.AcceptInvitation_sets_the_password_verifies_the_email_and_activates_the_user`, `UserTests.AcceptInvitation_lets_the_user_start_a_session`, `UserTests.Invited_user_can_have_its_email_corrected_before_accepting`, `UserTests.AcceptInvitation_rejects_null_arguments` |
| Acceptation seulement sans mot de passe, une seule fois, jamais Disabled | `User.AcceptInvitation` | `UserTests.AcceptInvitation_rejects_a_user_that_already_has_a_password`, `UserTests.AcceptInvitation_cannot_be_done_twice`, `UserTests.AcceptInvitation_rejects_a_disabled_user` |
| Acceptation en Locked : reste Locked | `User.AcceptInvitation`, `User.Unlock` | `UserTests.AcceptInvitation_on_a_locked_user_keeps_it_locked_until_unlocked`, `UserTests.Invited_user_unlocked_before_accepting_is_pending_again` |
| Sans mot de passe : ni session, ni vérification d'email seule, ni changement de mot de passe | `User.StartSession`, `User.RotateSession`, `User.VerifyEmail`, `User.ChangePassword` | `UserTests.Invited_user_cannot_start_a_session_before_accepting`, `UserTests.Invited_user_cannot_verify_its_email_without_setting_a_password`, `UserTests.Invited_user_cannot_change_a_password_it_does_not_have` |
| Pending -> Active à la vérification | `User.VerifyEmail` | `UserTests.VerifyEmail_activates_a_pending_user_and_raises_EmailVerified` |
| Email déjà vérifié non revérifiable | `User.VerifyEmail` | `UserTests.VerifyEmail_rejects_an_already_verified_email` |
| Nouvel email à revérifier | `User.ChangeEmail` | `UserTests.ChangeEmail_replaces_the_email_requires_a_new_verification_and_keeps_the_user_active`, `UserTests.ChangeEmail_then_VerifyEmail_verifies_the_new_address` |
| Email identique = no-op ; compte verrouillé rejeté | `User.ChangeEmail` | `UserTests.ChangeEmail_with_the_same_email_is_a_silent_no_op`, `UserTests.ChangeEmail_rejects_a_locked_user` |
| Changement de mot de passe révoque les sessions | `User.ChangePassword` | `UserTests.ChangePassword_revokes_every_session_by_default`, `UserTests.ChangePassword_keeps_the_session_it_was_requested_from` |
| Même hash = no-op (sessions conservées) | `User.ChangePassword` | `UserTests.ChangePassword_with_the_same_hash_is_a_silent_no_op_and_keeps_the_sessions` |
| Lock : Pending/Active -> Locked, sessions révoquées | `User.Lock` | `UserTests.Lock_locks_a_pending_or_active_user_and_raises_UserLocked`, `UserTests.Lock_revokes_every_session`, `UserTests.Lock_rejects_an_already_locked_user` |
| Active reste Active après changement d'email, même après Lock + Unlock (statut restauré, non recalculé) | `User.ChangeEmail`, `User.Lock`, `User.Unlock` | `UserTests.ChangeEmail_then_Lock_then_Unlock_keeps_the_user_active`, `UserTests.ChangeEmail_then_Lock_then_Unlock_still_lets_the_new_address_be_verified`, `UserTests.ChangeEmail_keeps_the_existing_sessions_usable`, `UserTests.Lock_can_be_repeated_after_an_unlock_and_each_unlock_restores_the_status_of_its_own_lock` |
| Pending vérifié pendant le verrouillage -> Active au déverrouillage | `User.VerifyEmail`, `User.AcceptInvitation`, `User.Unlock` | `UserTests.A_pending_user_verified_while_locked_is_active_once_unlocked`, `UserTests.AcceptInvitation_on_a_locked_user_keeps_it_locked_until_unlocked` |
| Unlock : seulement depuis Locked | `User.Unlock` | `UserTests.Unlock_restores_an_active_user_when_the_email_is_verified`, `UserTests.Unlock_restores_a_pending_user_when_the_email_is_not_verified`, `UserTests.Unlock_rejects_a_user_that_is_not_locked`, `UserTests.Unlock_does_not_bring_revoked_sessions_back` |
| Disabled terminal | `User.Disable` + toutes | `UserTests.Disable_can_be_reached_from_pending_active_and_locked`, `UserTests.Disabled_is_terminal_and_rejects_every_mutation` |
| Disabled ne peut ouvrir de session | `User.StartSession`, `User.RotateSession` | `UserTests.Disabled_user_cannot_start_a_session`, `UserTests.Disabled_user_cannot_rotate_a_session`, `UserTests.Disable_revokes_every_session` |
| Pending / Locked ne peuvent ouvrir de session | `User.StartSession`, `User.RotateSession` | `UserTests.StartSession_rejects_a_pending_user`, `UserTests.StartSession_rejects_a_locked_user`, `UserTests.RotateSession_rejects_a_locked_user` |
| Expiration obligatoire, durée explicite | `SessionLifetime.Create`, `User.StartSession` | `CredentialValueObjectsTests.SessionLifetime_rejects_a_non_positive_duration`, `SessionLifetime_accepts_the_maximum_and_rejects_beyond`, `UserTests.StartSession_opens_a_usable_session_expiring_after_the_given_lifetime`, `UserTests.Session_is_usable_until_its_expiry_instant_excluded` |
| Hash de token unique par utilisateur | `User.StartSession`, `User.RotateSession` | `UserTests.StartSession_rejects_a_token_hash_already_used_by_the_user`, `UserTests.RotateSession_rejects_a_new_token_hash_already_used` |
| Rotation : révoque + `ReplacedBy` + event | `User.RotateSession` | `UserTests.RotateSession_revokes_the_presented_session_and_links_it_to_the_new_one`, `UserTests.RotateSession_raises_RefreshTokenRotated_only`, `UserTests.RotateSession_can_be_chained_with_each_new_token` |
| Replay d'un token tourné : rejet + révocation de tout | `User.RotateSession` | `UserTests.RotateSession_replay_of_a_rotated_token_is_rejected_and_revokes_every_session`, `UserTests.RotateSession_after_a_replay_the_legitimate_new_token_is_unusable_too`, `UserTests.RotateSession_second_replay_is_still_reported_without_revoking_anything_more` |
| Token révoqué inutilisable | `User.RotateSession`, `RefreshSession.IsUsableAt` | `UserTests.RotateSession_rejects_a_session_revoked_by_the_user_without_touching_the_others`, `UserTests.RevokeSession_makes_the_session_unusable_and_raises_SessionRevoked` |
| Token expiré / inconnu inutilisable | `User.RotateSession` | `UserTests.RotateSession_rejects_an_expired_session`, `UserTests.RotateSession_rejects_an_unknown_token` |
| Révocation non rejouable | `User.RevokeSession` | `UserTests.RevokeSession_rejects_an_already_revoked_session`, `UserTests.RevokeSession_rejects_a_rotated_session`, `UserTests.RevokeSession_rejects_an_unknown_session` |
| Révoquer une session expirée : ni mutation ni event | `User.RevokeSession` | `UserTests.RevokeSession_on_an_expired_session_changes_nothing_and_raises_nothing`, `UserTests.RevokeSession_still_rejects_a_revoked_session_once_it_has_expired` |
| Purge = ménage technique permis dans tout statut, Disabled compris | `User.PurgeExpiredSessions` | `UserTests.PurgeExpiredSessions_is_allowed_on_a_disabled_user_and_raises_nothing` |
| Révocation globale | `User.RevokeAllSessions` | `UserTests.RevokeAllSessions_revokes_only_sessions_not_already_revoked`, `UserTests.RevokeAllSessions_without_session_raises_nothing` |
| Sessions actives = non révoquées et non expirées | `User.ActiveSessionsAt`, `User.PurgeExpiredSessions` | `UserTests.ActiveSessionsAt_excludes_expired_and_revoked_sessions`, `UserTests.PurgeExpiredSessions_removes_only_expired_sessions` |
| Collection bornée : une génération tournée par chaîne | `User.RotateSession` | `UserTests.RotateSession_drops_the_rotated_session_whose_successor_has_been_rotated`, `UserTests.RotateSession_every_15_minutes_for_30_days_keeps_the_session_collection_bounded`, `UserTests.RotateSession_keeps_two_sessions_per_device_chain` |
| Rejeu du dernier token tourné toujours détecté ; token plus ancien = inconnu | `User.RotateSession` | `UserTests.RotateSession_replay_of_the_latest_rotated_token_is_still_detected_in_a_long_chain`, `UserTests.RotateSession_token_older_than_the_kept_generation_is_unknown_and_revokes_nothing` |
| La purge garde une session tournée tant que sa remplaçante est utilisable | `User.PurgeExpiredSessions` | `UserTests.PurgeExpiredSessions_keeps_an_expired_rotated_session_while_its_successor_is_usable`, `UserTests.PurgeExpiredSessions_removes_an_expired_rotated_session_once_its_successor_is_revoked`, `UserTests.PurgeExpiredSessions_removes_a_rotated_session_together_with_its_expired_successor`, `UserTests.PurgeExpiredSessions_does_not_remove_a_rotated_session_that_has_not_expired` |

## 5. Application / Infrastructure

- **Unicité de l'email** : port `IUserEmailUniquenessChecker`, réponse passée en `bool isEmailUnique` ; l'**index
  unique en base** sur l'email normalisé est le garde-fou final (course entre deux inscriptions).
- **Invitation** (ex. invitation de staff par email pour une personne sans compte) : `RegisterInvited` (plus de faux
  hash), génération/validation du token d'invitation à usage unique et de son expiration, envoi de l'email sur
  `UserInvited`, puis `AcceptInvitation(hash)` à la validation du token. `PasswordHash` est **nullable** : colonne
  nullable en base, et l'authentification par mot de passe doit refuser un compte `HasPassword == false` avant toute
  comparaison de hash. Un « mot de passe oublié » sur un compte invité doit renvoyer vers l'invitation
  (`ChangePassword` lève `identity.user.password_not_set`).
- `ChangeEmail` / `ChangePassword` avec la valeur courante ne lèvent plus d'erreur et ne produisent aucun event :
  ne pas en déduire qu'un changement a eu lieu (pas de notification « mot de passe modifié » sans `PasswordChanged`).
- Hachage/validation du mot de passe, génération et hachage du refresh token, émission du JWT d'accès.
- Recherche de l'utilisateur par hash de token (index unique sur `TokenHash`), puis `RotateSession`.
- **Replay** : sauvegarder l'agrégat **puis** répondre avec `SessionRotationResult.Error`.
- Deux rotations concurrentes du même token : concurrence optimiste sur `User` (version) ; la perdante rejoue et
  tombe sur le cas replay.
- Les sessions retirées par la rotation ou la purge sont des enfants supprimés de l'agrégat : l'ORM doit les
  supprimer en base (orphelins). Un token antérieur à la dernière génération tournée répond
  `identity.session.not_found` (401 simple, sans révocation).
- **Rejeu d'un token antérieur à la dernière génération tournée** : rejeté comme **introuvable**
  (`identity.session.not_found`) **sans révocation globale** — conséquence directe de la borne sur la liste des
  sessions (une seule génération tournée conservée par chaîne). Seul le rejeu du dernier token tourné déclenche la
  révocation de tout. Si une détection plus profonde est voulue, elle relève d'un journal d'Infrastructure.
- **`StatusBeforeLock`** : colonne nullable à persister (non nulle uniquement en `Locked`). L'Application ne doit
  pas déduire « email vérifié » du statut `Active` : lire `IsEmailVerified` quand la preuve de l'adresse courante
  compte (ex. envoi d'un lien de reset à la nouvelle adresse).
- `PurgeExpiredSessions` peut être planifiée sur tous les comptes sans filtrer par statut.
- Tokens de vérification d'email / reset, anti-bruteforce (qui appelle `Lock`), planification de
  `PurgeExpiredSessions` (ne sert plus qu'à retirer les chaînes mortes ; la borne par chaîne et la détection de
  replay n'en dépendent pas).

## 6. Application (`Identity.Application`)

Un cas d'usage = une commande/requête + son handler. Les mots de passe et tokens bruts s'arrêtent ici : le domaine
ne reçoit que des hashs.

| Cas d'usage | Effet | Erreurs propres à l'Application |
|---|---|---|
| `RegisterUser(email, password)` | Compte `Pending`, lien de vérification envoyé | `identity.password.*` |
| `VerifyEmail(token)` / `ResendEmailVerification(email)` | Vérifie l'email courant / renvoie un lien (silencieux) | `identity.token.invalid` |
| `InviteUser(email)` / `AcceptInvitation(token, password)` | Compte sans mot de passe, puis activation | `identity.token.invalid`, `identity.password.*` |
| `Login(email, password)` | Ouvre une session, rend access + refresh token | `identity.auth.invalid_credentials` (401) |
| `RefreshSession(refreshToken)` | Rotation ; rejeu => tout révoqué **et sauvegardé**, puis 401 | `identity.auth.invalid_refresh_token` (401) |
| `Logout(refreshToken)` | Révoque la session ; idempotent | — |
| `RevokeSession`, `RevokeAllSessions`, `GetUserSessions` | « Vos appareils » | `identity.user.not_found` |
| `ChangePassword(userId, current, new, currentSessionId?)` | Exige le mot de passe courant ; garde la session courante | `identity.auth.invalid_credentials` (401) |
| `RequestPasswordReset(email)` / `ResetPassword(token, new)` | Silencieux ; un compte invité reçoit à nouveau son invitation | `identity.token.invalid` |
| `ChangeEmail(userId, newEmail)` | Nouveau lien envoyé à la nouvelle adresse | — |
| `LockUser`, `UnlockUser`, `DisableUser`, `GetUserById` | Back-office | `identity.user.not_found` |

Ports à implémenter par l'Infrastructure : `IUserRepository`, `IIdentityUnitOfWork`, `IUserEmailUniquenessChecker`
(domaine), `IPasswordHasher`, `IRefreshTokens`, `IAccessTokenIssuer`, `IUserTokens`, `IIdentityNotifier`,
`IPasswordAttempts`, `ISessionStatus`, `ICompromisedPasswords`, `IUserTraces`.

Choix faits :

- **Une seule réponse pour un login raté** (`identity.auth.invalid_credentials`) : email inconnu, mauvais mot de
  passe, compte invité sans mot de passe. Sur un compte inconnu le handler fait quand même le travail de hachage
  (temps de réponse). L'état du compte (`email_not_verified`, `locked`, `disabled`) n'est révélé qu'à qui connaît
  le mot de passe.
- **`UnauthenticatedException`** (building block, 401) distincte de `DomainException` (422). Au refresh, toute
  règle du domaine refusée (session révoquée/expirée, compte verrouillé) est rendue en 401 avec le code du domaine.
- **Politique de mot de passe** (`PasswordPolicy`) : 8 à 128 caractères, sans règle de composition. La borne haute
  est vérifiée avant tout hachage.
- **Tokens à usage unique** (`IUserTokens`) : vérification d'email (24 h), invitation (7 j), reset (1 h), durées
  dans `IdentitySettings`. Émettre un token **remplace** le précédent du même utilisateur et du même usage : après
  `ChangeEmail`, le lien envoyé à l'ancienne adresse ne vérifie plus rien. Émission (`IssueAsync`) et consommation
  sont validées par le même `SaveChanges` que l'agrégat.
- **Emails envoyés après la sauvegarde** (`IIdentityNotifier`) : un email perdu se rattrape par un nouveau lien.
  Le token brut ne passe jamais par un event ni par l'outbox.
- **Réponses silencieuses** pour `RequestPasswordReset` et `ResendEmailVerification` : ne pas révéler l'existence
  d'un compte. `RegisterUser` la révèle (`identity.user.email_already_used`), comme le prévoit le domaine.
- `ChangeEmail` redemande le mot de passe courant quand `RequireCurrentPassword` est vrai (libre-service) : un
  jeton d'accès volé ne doit pas suffire à détourner les emails du compte puis à réinitialiser son mot de passe.
  Le back-office l'appelle sans (correction d'une adresse invitée).
- Voir §10 pour la purge planifiée, le re-hachage, les mots de passe compromis, le plafond de sessions et
  l'effacement de compte.

## 7. Infrastructure (`Identity.Infrastructure`)

- **PostgreSQL + EF Core**, schéma dédié `identity` : `users`, `sessions`, `user_tokens`, `outbox_messages`.
  Enregistrement : `services.AddIdentityInfrastructure(connectionString, options => ...)` ; restent à fournir par
  l'hôte `IClock` et `IIdentityNotifier`.
- **Agrégat chargé entier** : toute lecture d'un `User` inclut ses sessions. Les sessions retirées par la rotation
  ou la purge sont supprimées en base.
- **Concurrence optimiste sur tout l'agrégat** (`xmin` de `users`) : quand seules des sessions changent, la ligne
  du user est écrite quand même, sinon deux rotations simultanées du même token réussiraient toutes les deux. La
  perdante reçoit une `ConcurrencyException` ; en rejouant, elle tombe sur le cas replay.
- **Index uniques** : `email` (une course entre deux inscriptions devient `identity.user.email_already_used`),
  `sessions.token_hash`, `user_tokens.token_hash`, `user_tokens (user_id, purpose)`.
- **Mots de passe** : `PasswordHasher` d'ASP.NET Core, PBKDF2-HMAC-SHA512, 210 000 itérations, sel aléatoire.
- **Refresh tokens et liens emailés** : 32 octets aléatoires en Base64Url ; seul leur SHA-256 est stocké (256 bits
  d'entropie : pas besoin de hachage lent, et le hash reste indexable).
- **Jeton d'accès** : JWT signé HMAC-SHA256, 15 min par défaut, claims `sub` (user), `sid` (session), `email`.
  **Aucun rôle** : les permissions ne sont pas du ressort d'Identity. Configuration `AccessTokenOptions`
  (`Issuer`, `Audience`, `SigningKey` en base64 d'au moins 32 octets, `Lifetime`), refusée à l'enregistrement si
  elle est faible ou incomplète.
- **Emails** : voir §10 (SMTP, file d'attente en mémoire, textes en français).
- **Events** : écrits dans `identity.outbox_messages` par le socle partagé
  (`SaasRestaurant.BuildingBlocks.Infrastructure`), ids et emails en valeurs simples, jamais de hash.
- **Migrations** : `dotnet ef migrations add <Nom> --project src/Modules/Identity/Identity.Infrastructure
  --startup-project src/Modules/Identity/Identity.Infrastructure --output-dir Persistence/Migrations` ;
  `database update` lit `IDENTITY_DB`.
- **Tests** (`Identity.Infrastructure.Tests`) : sur un vrai PostgreSQL via Testcontainers, Docker requis.

## 8. API (`Identity.Api`)

Point d'entrée pour l'hôte : `services.AddIdentityModule(connectionString, options => ...)`,
`AddAuthentication().AddIdentityAccessTokens()`, `app.MapIdentityEndpoints(backOfficePolicy)` et
`services.MigrateIdentityDatabaseAsync()`. `MapIdentityEndpoints(backOfficePolicy)` rend les trois groupes de
routes pour que l'hôte y chaîne ses règles ; la policy du back-office est obligatoire.

| Groupe | Accès | Routes |
|---|---|---|
| `/auth` | Anonyme | `POST register`, `verify-email` (lien + mot de passe), `resend-verification` (202), `login`, `refresh`, `logout`, `forgot-password` (202), `reset-password`, `accept-invitation` |
| `/me` | Utilisateur connecté | `GET /me` ; `PUT /me/password`, `/me/email`, `POST /me/erase` (mot de passe courant exigé) ; `GET /me/sessions`, `DELETE /me/sessions/{id}`, `DELETE /me/sessions` |
| `/users` | Policy back-office fournie par l'hôte | `POST /users/invitations` ; `GET /users/{id}` ; `PUT /users/{id}/email` ; `POST /users/{id}/lock`, `/unlock`, `/disable` ; `DELETE /users/{id}/sessions` ; `DELETE /users/{id}` (effacement) |

- Sur `/me`, l'utilisateur est toujours celui du jeton (`sub`), la session courante celle du claim `sid` : aucun id
  ne vient de la requête. `PUT /me/password` conserve la session courante et révoque les autres.
- Le jeton d'accès porte `sub`, `sid`, `email`, `email_verified`. Qui accorde un droit d'après l'email doit
  vérifier `email_verified`.
- Un jeton d'accès cesse d'être accepté dès que sa session est révoquée ou son compte verrouillé/désactivé (voir §9).
- Erreurs : `401` pour `UnauthenticatedException` (identifiants, refresh token), `429` pour trop de tentatives ou
  trop de requêtes, `422` pour une règle métier,
  `404` pour un compte introuvable, `409` pour un conflit de concurrence.
- **Tests** (`Identity.Api.Tests`) : le module servi en mémoire sur un vrai PostgreSQL, avec la vraie validation
  des jetons ; Docker requis.

## 9. Règles ajoutées après l'audit de sécurité

- **Tout lien emailé est gardé par la version du compte** : émettre ou révoquer un token réécrit la ligne du user.
  Un lien demandé pendant qu'un changement d'email est validé est refusé (sinon le lien partait vers l'ancienne
  adresse et vérifiait la nouvelle, sans la posséder).
- **Demandes de lien simultanées** : la perdante se termine en silence (`202`), jamais en `500` ni en `409`, pour
  un compte existant comme pour un compte inconnu.
- **Un lien de réinitialisation meurt** dès que le mot de passe ou l'email du compte change.
- **Pas de lien de réinitialisation vers une adresse non prouvée** d'un compte déjà activé (email changé puis jamais
  vérifié). Un compte jamais activé en reçoit un : le vrai propriétaire de la boîte récupère ainsi un compte qu'un
  tiers aurait créé à son adresse, et la réinitialisation vérifie l'adresse (elle prouve la boîte, comme une
  invitation).
- **`ChangePassword` et l'id de session du jeton** : un jeton d'accès antérieur à un refresh désigne une session
  déjà tournée ; le handler suit la chaîne jusqu'à la session vivante, et n'en garde aucune si la chaîne a disparu.
- **Chaque connexion retire les sessions expirées** du compte.
- **Mot de passe fait d'espaces** : refusé (`identity.password.empty`).
- **Vérifier un email exige le mot de passe du compte** (`verify-email` : `token` + `password`). Seul celui qui a
  créé le compte peut l'activer : le propriétaire d'une boîte sur laquelle un tiers s'est inscrit ne le peut pas, et
  récupère le compte par « mot de passe oublié ». Un mauvais mot de passe laisse le lien utilisable.
- **Blocage temporaire par adresse** (`PasswordVerifier`, seul endroit où un mot de passe en clair est vérifié) :
  10 échecs en 15 minutes (`IdentitySettings`) bloquent toute tentative sur cette adresse jusqu'à la fin de la
  fenêtre, bon mot de passe compris : `429 identity.auth.too_many_attempts` + `Retry-After`. Même comportement
  pour une adresse sans compte. Compte aussi les mots de passe courants erronés de `/me/password`, `/me/email`
  et `verify-email`. Un login réussi ou une réinitialisation remet le compteur à zéro. Blocage temporaire et non
  `Lock` du domaine : un verrouillage réel permettrait à n'importe qui de bloquer durablement n'importe quel compte.
  Les échecs sont stockés dans `identity.password_attempts`, écrits hors unité de travail (upsert atomique).
- **Limite de débit par adresse cliente sur `/auth`** : 60 requêtes par minute par défaut (`AuthRateLimitOptions`),
  `429` au-delà. L'hôte doit appeler `app.UseRateLimiter()` et, derrière un proxy, rétablir l'adresse réelle du
  client (en-têtes transférés), sinon tous les appelants partagent le même budget.
- **Un jeton d'accès est refusé dès que sa session ne vaut plus** (`ISessionStatus`, vérifié à chaque requête
  authentifiée) : déconnexion, révocation, compte verrouillé ou désactivé. Tolérance d'un cran : un jeton émis
  juste avant un refresh reste accepté tant que la session qui a remplacé la sienne est vivante ; au refresh
  suivant il est refusé. Coût : une requête indexée par appel authentifié.
- **Reste ouvert** : le blocage par adresse permet
  à un tiers d'empêcher un utilisateur de se connecter tant qu'il entretient les échecs (la réinitialisation par
  email reste possible).

## 10. Emails, tâches de fond et durcissement

**Emails réels.** `AddIdentitySmtpEmails(liens, smtp)` envoie par SMTP (MailKit) ; `AddIdentityLogEmails(liens)`
écrit les emails entiers dans les logs (développement uniquement). Textes en français, texte brut. Les liens
pointent vers le front (`IdentityEmailOptions` : trois URL contenant `{token}`), refusés à l'enregistrement s'ils
sont invalides.

- **Hors requête** : `IIdentityNotifier` ne fait que déposer l'email dans une file en mémoire ; un service de fond
  l'envoie (3 essais). Une requête n'attend jamais le serveur de mail, donc sa durée ne dit pas si un email est
  parti. En mémoire à dessein : un lien est un secret, il n'est écrit nulle part ; perdu à un redémarrage, il se
  redemande. Écart restant entre compte existant et inconnu : les quelques millisecondes d'écriture en base.
- **Avis de sécurité** (`SecurityNotificationConsumer`, consommateur de l'outbox) : « mot de passe modifié » à
  l'adresse du compte, « adresse changée » à l'**ancienne** adresse (la seule qu'un pirate ne contrôle pas), la
  nouvelle masquée. Envoi au moins une fois : un doublon est possible après un incident.

**Outbox publiée.** `OutboxDispatcher<TContext>` (socle partagé, un par module) lit les messages en attente, dans
l'ordre, et les passe à tous les `IOutboxConsumer` ; plusieurs instances peuvent tourner (`FOR UPDATE SKIP LOCKED`).
Un consommateur en échec retient son message et les suivants jusqu'à ce qu'il réussisse (un message
empoisonné bloque donc la file du module : à surveiller dans les logs). Les messages publiés sont supprimés après
30 jours.

**Conflits rejoués côté serveur.** Deux requêtes simultanées sur un même compte : la perdante est rejouée sur des
données fraîches (jusqu'à 5 essais) au lieu de recevoir `409`. Exception voulue : le **même** refresh token envoyé
deux fois au même instant donne une paire à l'une et `409` à l'autre, sans révocation générale — ce n'est pas un
vol, c'est un double envoi. Rejoué plus tard, ce token reste traité comme un rejeu.

**Autres règles**

- **Purge planifiée** (`IdentityMaintenance`, au démarrage puis toutes les 6 h) : sessions expirées (même règle
  que le domaine : une session tournée reste tant que sa remplaçante est utilisable), liens expirés, compteurs
  d'échecs anciens.
- **Re-hachage** : à la connexion, un hash fait avec moins d'itérations que le réglage courant est refait
  (`User.UpgradePasswordHash` : ni event, ni session révoquée).
- **Normalisation Unicode** (NFKC) du mot de passe avant hachage : composé ou décomposé, il correspond.
- **Mots de passe compromis** (`ICompromisedPasswords`) : désactivé par défaut. Activé, il interroge
  api.pwnedpasswords.com en n'envoyant que les 5 premiers caractères du SHA-1 ; service injoignable = mot de
  passe accepté. Erreur `identity.password.compromised`.
- **Plafond de sessions** : 20 sessions actives par compte (`IdentitySettings.MaxActiveSessions`) ; au-delà, la
  connexion ferme les plus anciennes.
- **Effacement de compte** (`User.Erase`, `EraseUser`) : irréversible, permis dans tout statut. Le compte garde son
  id (les autres modules le référencent), passe `Disabled`, perd email (remplacé par
  `erased-<id>@erased.invalid`), mot de passe, sessions et liens ; ses events déjà écrits et ses compteurs
  d'échecs sont supprimés, seul `UserErased` (sans email) reste. L'adresse redevient libre. Libre-service :
  `POST /me/erase` avec le mot de passe ; back-office : `DELETE /users/{id}`.
- `POST /auth/register` ne renvoie plus de `Location`.

## 11. Restaurant sélectionné (résolution multi-tenant)

Identity ne connaît pas les restaurants, mais c'est lui qui signe les jetons : il y écrit le tenant **après**
vérification de l'appartenance, et toute route qui agit pour un restaurant lit ensuite le tenant **dans le jeton**
(claim `tid`), jamais dans la requête.

- **`POST /me/tenant`** `{ tenantId }` (utilisateur connecté) : si `ITenantMemberships.CanActForTenantAsync` répond
  oui, renvoie un jeton d'accès `{ tenantId, accessToken, accessTokenExpiresAtUtc }` sur **la même session**, portant
  `tid`. Rien n'est écrit en base. Tenant inconnu et tenant d'un autre : même réponse,
  `403 identity.tenant.access_denied` (on ne révèle pas quels tenants existent).
- **`POST /auth/refresh`** accepte `tenantId` (facultatif) : le client renvoie le restaurant qu'il avait choisi, et
  l'appartenance est **revérifiée à chaque refresh**. Un membre retiré perd donc l'accès au plus tard à l'expiration
  de son jeton d'accès (15 min). Refus = `403`, **sans rotation** : le refresh token présenté reste valable (le client
  peut se rabattre sur un jeton sans restaurant). Ordre : la détection de rejeu passe **avant** le contrôle
  d'appartenance, un token volé ne peut pas y échapper en demandant un tenant interdit.
- **Login** ne pose aucun tenant : l'utilisateur choisit ensuite (un compte peut appartenir à plusieurs restaurants).
- **Port `ITenantMemberships`** (Application) : « membre actif d'un tenant non fermé ». Par défaut
  `NoTenantMemberships` refuse tout (fail-closed) ; l'hôte branche la vraie réponse avec
  `AddIdentityTenantMemberships<T>()` quand Staff aura sa persistance.
- Le jeton avec `tid` reste soumis au contrôle de session (§9) : déconnexion, verrouillage ou désactivation le coupent
  aussitôt.
- Tests : `TenantSelectionTests` (Application), `TenantSelectionEndpointsTests` (API, vrai PostgreSQL).
