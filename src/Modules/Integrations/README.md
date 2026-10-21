# Module Integrations — Domain, Application, Infrastructure, Api

But : exposer des intégrations sûres et auditables (webhooks sortants, credentials d'API).

## 1. Frontière d'agrégat

- `WebhookEndpoint` : configuration d'une destination (URL, événements souscrits, référence de secret, statut).
- `WebhookDelivery` : livraison d'**un** événement à **un** endpoint, avec ses tentatives. Racine séparée : une livraison a un
  cycle de vie court et à fort volume, la charger dans l'endpoint créerait un agrégat sans borne et de la contention.
  Elle référence l'endpoint par `WebhookEndpointId` et l'événement sortant par `EventId`, mais **ne naît que de l'endpoint
  chargé** : `WebhookDelivery.CreateFor(endpoint, ...)` lit `TenantId` et `Id` sur l'agrégat `WebhookEndpoint` (lecture seule,
  l'endpoint n'est pas modifié) et refuse un endpoint inactif ou non abonné. Il n'existe plus de fabrique par identifiants nus :
  une livraison ne peut pas porter le tenant de A et l'endpoint de B.
- `ApiCredential` : credential d'intégration, indépendant des webhooks. Ses scopes sont validés à l'émission contre un
  `ApiScopeCatalog` (value object local, fourni par l'Application) ; aucune référence au module Staff.
- Aucun secret réel dans le domaine : `SecretReference` pointe vers le coffre, `ApiKeyHash` est un hash calculé ailleurs.
  **La clé brute n'entre jamais dans le domaine** : elle est générée, affichée une seule fois et hachée par l'Application/Infrastructure.

## 2. Classes

| Classe | Rôle DDD | Responsabilité | Transitions | Events | Erreurs |
|---|---|---|---|---|---|
| `WebhookEndpoint` | AggregateRoot (tenant-owned) | URL HTTPS, souscriptions, rotation de la référence de secret | `Active -> Disabled` (`Disable`), `Disabled -> Active` (`Enable`) | `WebhookEndpointCreated`, `WebhookEndpointDisabled`, `WebhookEndpointEnabled`, `WebhookSecretReferenceRotated` | `integrations.webhook_endpoint.*` |
| `WebhookUrl` | Value Object | URL absolue, HTTPS, hôte public (ni loopback, ni IP privée/link-local/non spécifiée, ni nom sans point), port ≠ 0, aucun `@` dans l'autorité, ≤ 2048 | — | — | `integrations.webhook_url.invalid`, `.https_required`, `.credentials_forbidden`, `.host_not_public`, `.port_invalid` |
| `EventTypeCode` | Value Object | Code d'événement public `module.fait` (même grammaire que `ApiScope` / `PermissionCode` de Staff : ≥ 2 segments minuscules, majuscules rejetées, pas de joker) | — | — | `integrations.event_type.invalid` |
| `SecretReference` | Value Object | Référence opaque vers le secret de signature | — | — | `integrations.secret_reference.invalid` |
| `WebhookDelivery` | AggregateRoot (tenant-owned) | Créée depuis son endpoint (`CreateFor`), politique de retry figée, tentatives, retry borné, dead-letter | `Pending -> Succeeded` ; `Pending -> Failed` ; `Failed -> Succeeded` ; `Failed -> Failed` ; `Pending/Failed -> DeadLetter` (hors tentative en cours) ; `Succeeded` et `DeadLetter` terminaux | `WebhookDelivered`, `WebhookDeliveryFailed`, `WebhookDeadLettered` | `integrations.webhook_delivery.*` (dont `.endpoint_not_active`, `.event_not_subscribed`) |
| `WebhookRetryPolicy` | Value Object | Nombre max de tentatives (≤ 50) + backoff exponentiel plafonné (`MaxDelay` ≤ 7 jours) | — | — | `integrations.webhook_retry_policy.invalid` |
| `WebhookPayloadSnapshot` | Value Object | Type d'événement + version de schéma + corps opaque figé | — | — | `integrations.webhook_payload.*` |
| `ApiCredential` | AggregateRoot (tenant-owned) | Hash de clé, scopes du catalogue, expiration, décision d'accès | `Active -> Revoked` (terminal) ; `Rotate` reste `Active` | `ApiCredentialIssued`, `ApiCredentialRotated`, `ApiCredentialRevoked` | `integrations.api_credential.*` |
| `ApiKeyHash` | Value Object | Hash opaque (32 à 512 caractères), jamais imprimé par `ToString()` | — | — | `integrations.api_key_hash.invalid` |
| `ApiScope` | Value Object | Scope `ressource.action` (même grammaire que `PermissionCode` de Staff), correspondance exacte | — | — | `integrations.api_scope.invalid` |
| `ApiScopeCatalog` | Value Object | Liste explicite des scopes connus de la plateforme, non vide | — | — | `integrations.api_scope_catalog.empty` |
| `WebhookEndpointId`, `DeliveryId`, `EventId`, `CredentialId` | Ids typés | — | — | — | `common.id.empty` |

## 3. Choix faits là où la spec était ouverte

**WebhookEndpoint**
- « Au moins un event si actif » : `Create` en exige un, `Unsubscribe` du dernier est rejeté tant que l'endpoint est `Active`,
  `Enable` est rejeté sans souscription. Un endpoint `Disabled` peut être vidé. Limite : 100 événements.
- `Subscribe` d'un événement déjà souscrit et `Unsubscribe` d'un événement non souscrit sont rejetés (pas d'idempotence implicite).
- `RotateSecretReference` est permis dans les deux statuts ; l'event ne porte pas la référence. **Même valeur = no-op
  silencieux** : rotation vers la référence déjà portée -> ni mutation, ni `SecretRotatedAtUtc`, ni event (l'erreur
  `secret_reference_unchanged` est supprimée).
- `ShouldReceive(eventType)` = `Active` et souscrit : sert au fan-out de l'Application, et `WebhookDelivery.CreateFor` le
  revérifie lui-même.
- Pas de `ChangeUrl` (absent de la spec) : on crée un nouvel endpoint.
- **URL anti-SSRF (contrôle lexical)** : `WebhookUrl.Create` refuse en plus (`integrations.webhook_url.host_not_public`)
  les hôtes loopback (`localhost`, `*.localhost`, `Uri.IsLoopback`), les IP littérales privées, link-local, unique-local
  ou non spécifiées (`0.0.0.0/8`, `10/8`, `127/8`, `169.254/16`, `172.16/12`, `192.168/16`, `::`, `::1`, `fc00::/7`,
  `fe80::/10`, ainsi que leurs formes IPv4-mappées `::ffff:a.b.c.d` et IPv4-compatibles `::a.b.c.d`) — **y compris les
  écritures décimale, octale, hexadécimale ou abrégée** que `Uri` normalise en IPv4 (`https://2130706433/`,
  `https://0x7f.0.0.1/`, `https://10.1/`) — et les noms d'hôte sans point (`https://intranet/`). Le port 0 est refusé
  (`integrations.webhook_url.port_invalid`) et tout `@` dans l'autorité, même avec un userinfo vide
  (`integrations.webhook_url.credentials_forbidden`) ; un `@` dans le chemin ou la query reste permis. Une IP publique
  littérale et un port personnalisé restent acceptés. **Ce contrôle ne porte que sur ce que l'URL écrit** : ce vers quoi
  le nom se résout est un devoir de l'Infrastructure (§ 5).
- **`EventTypeCode`** : même grammaire que `ApiScope` et que `PermissionCode` de Staff (au moins deux segments
  minuscules séparés par des points, chaque segment `[a-z][a-z0-9_]*`, 100 caractères max, espaces de bord retirés,
  **majuscules rejetées, plus de mise en minuscules**) : un seul vocabulaire de catalogue peut servir aux permissions,
  aux scopes et aux événements.

**WebhookDelivery**
- **Création depuis l'endpoint** : `CreateFor(endpoint, eventId, payload, clock, retryPolicy = null)`. Rejets, dans l'ordre :
  `eventId` vide (`common.id.empty`), endpoint non `Active` (`endpoint_not_active`), type d'événement du payload non souscrit
  (`event_not_subscribed`). Aucun event à la création (inchangé).
- **Politique de retry figée à la création** (`RetryPolicy`) : paramètre optionnel de `CreateFor`, `WebhookRetryPolicy.Default`
  si omis (6 tentatives, 30 s doublé à chaque échec, plafond 1 h). `FailAndScheduleRetry(reason, clock)` ne la reçoit plus :
  un changement de politique ne s'applique qu'aux livraisons suivantes. `MaxAttempts` compte la première tentative.
- `WebhookRetryPolicy.MaxDelay` est borné à `MaxAllowedDelay` = 7 jours (sinon `retry_policy.invalid`) : le doublement ne
  dépasse jamais 14 jours avant plafonnement, donc aucun débordement arithmétique possible.
- Une livraison déjà créée n'est pas affectée par la désactivation ultérieure de son endpoint : c'est l'Application qui
  décide de l'abandonner (`DeadLetter`).
- **Retry borné** : si la tentative qui échoue est la dernière autorisée, la livraison passe en `DeadLetter` et lève
  `WebhookDeadLettered` au lieu de planifier ; sinon `Failed` + `NextAttemptAtUtc` + `WebhookDeliveryFailed` (event ajouté).
- Une tentative en cours est portée par `CurrentAttemptStartedAtUtc` / `IsAttemptInProgress` (le statut ne change qu'à la fin
  d'une tentative). Deux tentatives simultanées sont refusées ; `Succeed`/`FailAndScheduleRetry` exigent une tentative en cours.
- `NextAttemptAtUtc` vaut la date de création au départ (dû immédiatement), `null` pendant une tentative et en état terminal.
  `StartAttempt` avant l'échéance est rejeté (`retry_not_due`).
- `DeadLetter(reason)` permet l'abandon explicite depuis `Pending`/`Failed` (endpoint supprimé, décision d'un opérateur),
  **mais est rejeté tant qu'une tentative est en cours** (`integrations.webhook_delivery.attempt_in_progress`) : son issue
  est inconnue, et une livraison réellement reçue ne doit pas être enregistrée comme abandonnée. Worker perdu : clore
  d'abord la tentative (`FailAndScheduleRetry("timeout")`), puis `DeadLetter`.
- **Une seule lecture de l'horloge par opération** (les trois agrégats) : l'instant qui juge la règle est celui de l'état
  (`CreatedAtUtc`, `SucceededAtUtc`, `RotatedAtUtc`...) et de l'événement.
- Le snapshot de payload est figé à la création : les retries renvoient exactement le même corps.

**ApiCredential**
- `Issue` reçoit uniquement le hash, 1 à 50 scopes, un `ApiScopeCatalog`, une expiration optionnelle strictement future.
  Tout scope absent du catalogue est rejeté (`scope_unknown`).
- **Un seul vocabulaire d'autorisation** : `ApiScope` suit la grammaire de `PermissionCode` (Staff) — au moins deux segments
  minuscules séparés par des points, chaque segment `[a-z][a-z0-9_]*`, 100 caractères max, espaces de bord retirés,
  **majuscules rejetées (pas de mise en minuscules)**. Ex. `orders.read`. L'ancienne forme `orders:read` est invalide.
- `HasScope(scope, instantUtc)` intègre l'utilisabilité : faux pour un credential révoqué ou expiré, même si le scope a été
  accordé (`Scopes` reste la liste brute, pour l'affichage).
- `IsUsableAt(now)` : `Active` et non expiré (l'instant d'expiration est exclu). `EnsureCanAccess(scope, clock)` rejette dans
  l'ordre : révoqué, expiré, scope absent. Les scopes sont comparés exactement (pas de joker ni de hiérarchie).
- `Rotate` : seulement si utilisable (actif et non expiré) ; l'ancienne clé cesse immédiatement de fonctionner
  (pas de période de grâce). Un credential expiré ne se prolonge pas : on en émet un nouveau. **Même valeur = no-op
  silencieux** : rotation vers le hash déjà porté -> ni mutation, ni `RotatedAtUtc`, ni event (l'erreur `key_hash_unchanged`
  est supprimée) ; révoqué/expiré reste rejeté même avec le même hash.
- `Revoke` est terminal et reste possible sur un credential expiré. `ApiCredentialIssued`/`Rotated` ajoutés (audit).
- Les scopes ne sont pas modifiables après émission (absent de la spec).

## 4. Matrice invariant -> méthode -> test

| Invariant | Méthode | Test |
|---|---|---|
| HTTPS obligatoire (http rejeté) | `WebhookUrl.Create` | `IntegrationValueObjectsTests.WebhookUrl_rejects_http_and_any_other_non_https_scheme`, `WebhookEndpointTests.Create_cannot_receive_an_http_url` |
| URL absolue et bornée | `WebhookUrl.Create` | `IntegrationValueObjectsTests.WebhookUrl_rejects_a_value_that_is_not_an_absolute_url`, `WebhookUrl_length_is_bounded` |
| Pas d'identifiants dans l'URL, aucun `@` dans l'autorité | `WebhookUrl.Create` | `IntegrationValueObjectsTests.WebhookUrl_rejects_embedded_credentials`, `WebhookUrl_rejects_any_at_sign_in_the_authority`, `WebhookUrl_accepts_an_at_sign_after_the_authority` |
| Hôte non loopback | `WebhookUrl.Create` | `IntegrationValueObjectsTests.WebhookUrl_rejects_a_loopback_host` |
| IP littérale ni privée, ni link-local, ni unique-local, ni non spécifiée (toutes écritures) | `WebhookUrl.Create` | `IntegrationValueObjectsTests.WebhookUrl_rejects_a_private_link_local_unique_local_or_unspecified_address`, `WebhookUrl_rejects_numeric_spellings_of_a_forbidden_address` |
| Nom d'hôte avec au moins un point ; port ≠ 0 | `WebhookUrl.Create` | `IntegrationValueObjectsTests.WebhookUrl_rejects_a_host_name_without_a_dot`, `WebhookUrl_rejects_port_zero` |
| Hôte public + port personnalisé acceptés | `WebhookUrl.Create` | `IntegrationValueObjectsTests.WebhookUrl_accepts_a_public_host_with_a_custom_port` |
| Format des codes d'événement = grammaire `ApiScope` / `PermissionCode` (majuscules rejetées) | `EventTypeCode.Create` | `IntegrationValueObjectsTests.EventTypeCode_rejects_an_invalid_code`, `EventTypeCode_rejects_uppercase_instead_of_lowering_it`, `EventTypeCode_is_trimmed_and_otherwise_kept_as_is`, `EventTypeCode_and_ApiScope_share_one_grammar` |
| Référence de secret opaque non vide | `SecretReference.Create` | `IntegrationValueObjectsTests.SecretReference_rejects_an_empty_or_whitespace_value` |
| Au moins un event à la création | `WebhookEndpoint.Create` | `WebhookEndpointTests.Create_requires_at_least_one_event` |
| Au moins un event si actif (désabonnement) | `Unsubscribe` | `WebhookEndpointTests.Unsubscribe_rejects_the_last_event_of_an_active_endpoint` |
| Endpoint désactivé peut être vidé | `Unsubscribe` | `WebhookEndpointTests.Unsubscribe_allows_the_last_event_of_a_disabled_endpoint` |
| Au moins un event pour réactiver | `Enable` | `WebhookEndpointTests.Enable_rejects_an_endpoint_without_any_event`, `Enable_succeeds_once_an_event_is_subscribed_again` |
| Souscriptions uniques et bornées | `Subscribe`, `Create` | `WebhookEndpointTests.Subscribe_rejects_an_event_already_subscribed`, `Subscribe_rejects_going_over_the_limit`, `Create_rejects_more_events_than_the_limit` |
| Désabonnement d'un event souscrit seulement | `Unsubscribe` | `WebhookEndpointTests.Unsubscribe_rejects_an_event_that_is_not_subscribed` |
| Rotation : nouvelle référence, event sans secret | `RotateSecretReference` | `WebhookEndpointTests.RotateSecretReference_replaces_the_reference_and_raises_an_event_without_it` |
| Rotation vers la même référence = no-op silencieux | `RotateSecretReference` | `WebhookEndpointTests.RotateSecretReference_with_the_same_reference_is_a_silent_no_op` |
| `Active <-> Disabled` explicites | `Disable`, `Enable` | `WebhookEndpointTests.Disable_stops_deliveries_and_raises_WebhookEndpointDisabled`, `Disable_rejects_an_already_disabled_endpoint`, `Enable_reactivates_a_disabled_endpoint_and_raises_WebhookEndpointEnabled`, `Enable_rejects_an_already_active_endpoint` |
| Création -> `WebhookEndpointCreated` | `Create` | `WebhookEndpointTests.Create_builds_an_active_endpoint_and_raises_WebhookEndpointCreated` |
| Collection non modifiable de l'extérieur | `SubscribedEvents` | `WebhookEndpointTests.SubscribedEvents_cannot_be_mutated_from_outside` |
| `StartAttempt` incrémente `AttemptCount` | `StartAttempt` | `WebhookDeliveryTests.StartAttempt_increments_the_attempt_count` |
| Pas deux tentatives simultanées | `StartAttempt` | `WebhookDeliveryTests.StartAttempt_rejects_a_second_concurrent_attempt` |
| Pas de tentative avant l'échéance | `StartAttempt` | `WebhookDeliveryTests.StartAttempt_is_rejected_before_the_retry_is_due`, `StartAttempt_is_allowed_exactly_when_the_retry_is_due` |
| Échec -> `Failed` + prochaine échéance selon la politique | `FailAndScheduleRetry` | `WebhookDeliveryTests.FailAndScheduleRetry_schedules_the_next_attempt_with_the_policy_backoff`, `FailAndScheduleRetry_backoff_grows_exponentially` |
| **Retry borné** -> `DeadLetter` + `WebhookDeadLettered` | `FailAndScheduleRetry` | `WebhookDeliveryTests.Retry_is_bounded_the_last_allowed_failure_dead_letters_the_delivery`, `A_single_attempt_policy_dead_letters_on_the_first_failure` |
| Succès/échec exigent une tentative en cours | `Succeed`, `FailAndScheduleRetry` | `WebhookDeliveryTests.Succeed_requires_an_attempt_in_progress`, `FailAndScheduleRetry_requires_an_attempt_in_progress` |
| Raison d'échec requise et bornée | `FailAndScheduleRetry`, `DeadLetter` | `WebhookDeliveryTests.FailAndScheduleRetry_requires_a_reason`, `FailAndScheduleRetry_rejects_a_reason_over_the_limit`, `DeadLetter_requires_a_reason` |
| Succès -> `WebhookDelivered` (y compris après retry) | `Succeed` | `WebhookDeliveryTests.Succeed_ends_the_delivery_and_raises_WebhookDelivered`, `A_retry_can_succeed_after_a_failure` |
| Abandon explicite | `DeadLetter` | `WebhookDeliveryTests.DeadLetter_gives_up_explicitly_from_pending`, `DeadLetter_gives_up_explicitly_from_failed` |
| Pas d'abandon pendant une tentative en cours | `DeadLetter` | `WebhookDeliveryTests.DeadLetter_is_rejected_while_an_attempt_is_in_flight`, `DeadLetter_is_possible_once_the_in_flight_attempt_has_been_ended_as_failed` |
| Horloge lue une fois (état = événement) | `Create`, `RotateSecretReference`, `Issue`, `Rotate`, `Revoke`, `StartAttempt`, `Succeed`, `FailAndScheduleRetry` | `WebhookEndpointTests.Each_operation_reads_the_clock_once_so_state_and_event_carry_the_same_instant`, `ApiCredentialTests.Each_operation_reads_the_clock_once_so_state_and_event_carry_the_same_instant`, `WebhookDeliveryTests.Each_operation_reads_the_clock_once_so_state_and_event_carry_the_same_instant`, `StartAttempt_judges_and_stamps_the_attempt_at_the_same_instant` |
| `Succeeded` terminal | toutes | `WebhookDeliveryTests.Succeeded_is_terminal` |
| `DeadLetter` terminal | toutes | `WebhookDeliveryTests.DeadLetter_is_terminal` |
| Payload figé et versionné | `WebhookPayloadSnapshot.Create`, `WebhookDelivery` | `WebhookDeliveryTests.Payload_snapshot_is_unchanged_across_retries`, `IntegrationValueObjectsTests.Payload_snapshot_rejects_a_schema_version_below_one`, `Payload_snapshot_rejects_an_empty_body`, `Payload_snapshot_size_is_bounded` |
| Politique de retry valide, backoff plafonné | `WebhookRetryPolicy` | `IntegrationValueObjectsTests.Retry_policy_rejects_invalid_settings`, `Retry_policy_backoff_doubles_up_to_the_cap`, `Retry_policy_knows_when_attempts_are_exhausted` |
| `MaxDelay` ≤ 7 jours, pas de débordement | `WebhookRetryPolicy.Create`, `DelayAfter` | `IntegrationValueObjectsTests.Retry_policy_max_delay_is_capped_so_the_backoff_cannot_overflow`, `WebhookDeliveryTests.Backoff_at_the_widest_allowed_policy_does_not_overflow` |
| Livraison créée depuis l'endpoint (tenant + id de l'endpoint), en attente, sans event | `WebhookDelivery.CreateFor` | `WebhookDeliveryTests.CreateFor_builds_a_pending_delivery_due_immediately_from_its_endpoint`, `CreateFor_takes_tenant_and_endpoint_id_from_the_endpoint` |
| Pas de fabrique par identifiants nus | structure | `WebhookDeliveryTests.CreateFor_is_the_only_public_factory` |
| Endpoint inactif -> pas de livraison | `WebhookDelivery.CreateFor` | `WebhookDeliveryTests.CreateFor_rejects_a_disabled_endpoint`, `CreateFor_rejects_a_disabled_and_unsubscribed_endpoint_as_not_active`, `CreateFor_accepts_a_re_enabled_endpoint_and_a_newly_subscribed_event` |
| Événement non souscrit -> pas de livraison | `WebhookDelivery.CreateFor` | `WebhookDeliveryTests.CreateFor_rejects_an_event_type_the_endpoint_does_not_subscribe_to` |
| `EventId` obligatoire | `WebhookDelivery.CreateFor` | `WebhookDeliveryTests.CreateFor_rejects_a_default_event_id` |
| Politique de retry figée à la création (défaut si omise) | `WebhookDelivery.CreateFor`, `FailAndScheduleRetry` | `WebhookDeliveryTests.CreateFor_pins_the_default_retry_policy_when_none_is_given`, `A_single_attempt_policy_dead_letters_on_the_first_failure` |
| Livraison indépendante de l'état ultérieur de l'endpoint | `StartAttempt`, `Succeed` | `WebhookDeliveryTests.A_delivery_outlives_the_later_disabling_of_its_endpoint` |
| Le domaine ne reçoit que le hash | `ApiCredential.Issue`, `ApiKeyHash` | `ApiCredentialTests.Issue_creates_an_active_credential_holding_only_the_hash`, `IntegrationValueObjectsTests.ApiKeyHash_is_opaque_and_never_printed`, `ApiKeyHash_rejects_an_invalid_value` |
| Au moins un scope, nombre borné | `Issue` | `ApiCredentialTests.Issue_requires_at_least_one_scope`, `Issue_rejects_more_scopes_than_the_limit` |
| Grammaire de scope = grammaire `PermissionCode` (points, minuscules, ≤ 100, majuscules rejetées) | `ApiScope.Create` | `IntegrationValueObjectsTests.ApiScope_follows_the_permission_code_grammar_and_is_trimmed`, `ApiScope_rejects_uppercase_instead_of_lowering_it`, `ApiScope_rejects_an_invalid_code`, `ApiScope_length_is_bounded` |
| Catalogue de scopes explicite et non vide | `ApiScopeCatalog.Create`, `Of` | `IntegrationValueObjectsTests.ApiScopeCatalog_knows_exactly_its_scopes`, `ApiScopeCatalog_rejects_an_empty_list`, `ApiScopeCatalog_rejects_a_malformed_scope` |
| Scopes émis ⊂ catalogue | `Issue` | `ApiCredentialTests.Issue_accepts_only_scopes_of_the_catalogue`, `Issue_rejects_a_scope_that_is_not_in_the_catalogue` |
| `HasScope` faux si révoqué ou expiré | `HasScope` | `ApiCredentialTests.HasScope_is_false_once_the_credential_is_expired`, `A_revoked_credential_is_rejected_even_for_a_granted_scope` |
| Expiration dans le futur | `Issue` | `ApiCredentialTests.Issue_rejects_an_expiry_that_is_not_in_the_future` |
| **Scope** : accès refusé si scope absent | `EnsureCanAccess` | `ApiCredentialTests.EnsureCanAccess_rejects_a_scope_that_was_not_granted`, `A_scope_does_not_imply_a_broader_or_narrower_one`, `EnsureCanAccess_allows_a_granted_scope` |
| Expiré = inutilisable | `IsUsableAt`, `EnsureCanAccess` | `ApiCredentialTests.Credential_is_usable_until_its_expiry_instant_excluded`, `EnsureCanAccess_rejects_an_expired_credential` |
| **Revoke** : révoqué = inutilisable | `Revoke`, `EnsureCanAccess` | `ApiCredentialTests.Revoke_makes_the_credential_unusable_and_raises_ApiCredentialRevoked`, `A_revoked_credential_is_rejected_even_for_a_granted_scope` |
| `Revoked` terminal | `Revoke`, `Rotate` | `ApiCredentialTests.Revoked_is_terminal` |
| Rotation : actif, non expiré | `Rotate` | `ApiCredentialTests.Rotate_replaces_the_hash_and_raises_ApiCredentialRotated_without_it`, `Rotate_rejects_an_expired_credential` |
| Rotation vers le même hash = no-op silencieux (mais rejet si révoqué/expiré) | `Rotate` | `ApiCredentialTests.Rotate_with_the_same_hash_is_a_silent_no_op`, `Rotate_with_the_same_hash_is_still_rejected_when_revoked_or_expired` |
| Scopes non modifiables de l'extérieur, pas de setter public | structure | `ApiCredentialTests.Scopes_cannot_be_mutated_from_outside`, `Aggregates_expose_no_public_setter` |

## 5. Ce qui relève de l'Application / Infrastructure

- **Secrets** : génération/stockage du secret de signature (coffre), signature HMAC des requêtes, génération de la clé d'API,
  affichage unique, hachage, comparaison en temps constant. Le domaine ne peut pas vérifier qu'une valeur est réellement un hash
  ou une référence : c'est la discipline de l'Application.
- **Anti-SSRF au moment de la résolution DNS (devoir de l'Infrastructure, non couvert par le domaine)** : `WebhookUrl`
  écarte les URLs qui *écrivent* une cible interne, mais un nom public peut **se résoudre** vers une adresse privée,
  loopback ou de métadonnées (DNS rebinding, enregistrement `A` vers `10.x`, hôte à double pile). Le client HTTP doit
  donc résoudre le nom, refuser les mêmes plages sur **chaque** adresse obtenue, se connecter à l'adresse validée (pas
  de seconde résolution), et refaire le contrôle à chaque redirection (ou les interdire). Timeouts, taille de réponse.
  La liste de plages du domaine n'est pas exhaustive (CGNAT `100.64/10`, NAT64, multicast... restent à l'Infrastructure).
- Fan-out : charger les endpoints **du tenant de l'événement** (isolation tenant du repository : le domaine prend le tenant
  de l'endpoint, il ne peut pas savoir à quel tenant appartient le payload), filtrer avec `ShouldReceive`, puis
  `WebhookDelivery.CreateFor(endpoint, eventId, payload, clock, policy)` (outbox). Unicité `(EndpointId, EventId)` par index
  unique pour éviter les doublons. Course « endpoint désactivé entre le chargement et la création » : verrouillage optimiste
  sur l'endpoint ou tolérance explicite.
- Worker : sélection des livraisons dues (`NextAttemptAtUtc <= now`), **verrouillage optimiste** (deux workers : le second
  `StartAttempt` échoue sur `attempt_in_progress` ou sur le conflit de version), reprise des tentatives orphelines
  (`CurrentAttemptStartedAtUtc` trop ancien -> `FailAndScheduleRetry("timeout")`). `DeadLetter` étant refusé pendant une
  tentative, l'abandon d'une livraison dont le worker est perdu passe par cette clôture préalable.
- Choix de la `WebhookRetryPolicy` (par plan/tenant) **au moment de `CreateFor`** ; elle est persistée avec la livraison
  (`RetryPolicy`, value object possédé) et n'est plus fournie à `FailAndScheduleRetry`. Purge des livraisons terminées,
  rejouer un dead-letter (= nouvelle livraison), abandon (`DeadLetter`) des livraisons en attente d'un endpoint désactivé/supprimé.
- Authentification : retrouver le credential par hash (index unique sur `KeyHash`), puis `EnsureCanAccess`. Unicité du nom
  non imposée par le domaine.
- **Catalogue des scopes** : construire l'`ApiScopeCatalog` passé à `ApiCredential.Issue` à partir du même vocabulaire que le
  `PermissionCatalog` de Staff (mêmes chaînes, ex. `orders.read`), éventuellement restreint aux permissions ouvertes aux
  intégrations. Le domaine vérifie l'appartenance à l'émission seulement : un scope retiré du catalogue ensuite reste porté
  par les credentials déjà émis (à révoquer par l'Application si nécessaire).
- Migration des données existantes : scopes stockés au format `a:b` ou en majuscules à convertir en `a.b` minuscules ;
  les `EventTypeCode` saisis avec des majuscules ne sont plus abaissés : l'Application doit fournir le code du catalogue tel quel ;
  colonne(s) de politique de retry à renseigner sur les livraisons déjà persistées.
- Rotation avec la valeur déjà portée (hash ou référence de secret) = no-op sans event : l'Application ne doit pas présenter
  cela comme une rotation effectuée (comparer `RotatedAtUtc` / `SecretRotatedAtUtc` ou l'absence d'event).
- Catalogue des `EventTypeCode` réellement publiés (le domaine ne valide que le format).

## 6. Persistance (étape 1)

Schéma `integrations`, historique des migrations dans le même schéma. Aucun secret ni aucune clé en clair.

| Table | Contenu | Gardes en base |
|---|---|---|
| `webhook_endpoints` | Agrégat `WebhookEndpoint` ; événements souscrits en `jsonb` (tableau trié) ; référence du secret | Index `(tenant_id, status)` ; CHECK statut ; version `xmin` |
| `webhook_deliveries` | Agrégat `WebhookDelivery` ; corps figé et politique de retry en colonnes (types complexes) | Index unique `(endpoint_id, event_id)` filtré `status <> 4` (une seule livraison vivante par événement, renvoi possible après abandon) ; index de la file du job (`next_attempt_at_utc`, `current_attempt_started_at_utc`), de l'historique et de la purge ; CHECK statut, nombre de tentatives, dates de réussite et d'abandon ; version `xmin` |
| `api_credentials` | Agrégat `ApiCredential` ; EMPREINTE de la clé | Index unique `key_hash` ; CHECK statut, date de révocation ; version `xmin` |
| `api_credential_scopes` | Scopes d'un credential | Clé `(credential_id, scope)` |
| `webhook_secrets` | Coffre : secrets de signature CHIFFRÉS (AES-256-GCM, la référence en donnée associée) | Index `tenant_id` |
| `outbox_messages` | Événements à publier | — |

Retouche du domaine : le champ `_subscribedEvents` de `WebhookEndpoint` n'est plus `readonly` (l'ORM le réécrit à la
relecture de la colonne `jsonb`). Aucune règle ne change.

Migration à générer :

```bash
dotnet ef migrations add InitialCreate --project src/Modules/Integrations/Integrations.Infrastructure --output-dir Persistence/Migrations
```

## 7. Webhooks, clés d'API, cas d'usage et API (étapes 2 à 6)

### Webhooks sortants

- **Catalogue** (`WebhookEvents`, version de schéma 1) : `orders.order_placed`, `orders.order_completed`,
  `orders.order_cancelled`, `payments.payment_captured`, `payments.refund_succeeded`, `reservations.reservation_created`,
  `reservations.reservation_confirmed`, `reservations.reservation_cancelled`, `reservations.reservation_no_show`,
  `kitchen.ticket_ready`, `inventory.low_stock_reached`, `catalog.changed`. Un endpoint ne s'abonne qu'à eux.
- **Corps** : `{"id", "type", "timestamp", "data"}`, figé à la création de la livraison. `data` est une LISTE BLANCHE
  construite par l'hôte (`WebhookFeed`) : ids, montants, statuts, dates, quantités. Jamais un nom, un contact, un id de
  client, un motif saisi librement ni une référence de prestataire.
- **Signature** (convention Standard Webhooks, bibliothèques disponibles côté destinataire) : en-têtes `webhook-id`
  (= id de l'événement, identique à chaque tentative : le destinataire dédoublonne), `webhook-timestamp`,
  `webhook-signature` = `v1,` + base64(HMAC-SHA256(secret, « id.timestamp.corps »)). Vérifiée sur le vecteur publié.
- **Secret** : `whsec_` + base64(32 octets), montré UNE fois (création, rotation) ; ensuite seulement déchiffré par le
  job, le temps de signer. Rotation avec période de grâce (double signature, voir §10).
- **Id de l'événement** : SHA-256 de « module:id du message d'outbox » : un message rejoué ne crée rien de plus
  (vérification, puis index unique).
- **Envoi anti-SSRF** (`SafeWebhookSender`) : le nom est résolu par le module, CHAQUE adresse doit être publique (privées,
  loopback, link-local et métadonnées des clouds, CGNAT, multicast, réservées, ULA, 6to4 et Teredo refusées), la
  connexion se fait à ces adresses (pas de seconde résolution) ; ni proxy, ni redirection, ni cookies ; 10 s ; la
  réponse n'est pas lue. Issue : 2xx = livré ; sinon `http_<code>`, `timeout`, `blocked_address`, `network_error`.
- **Job** (`WebhookDeliveryWorker`, toutes les 5 s) : clôt les tentatives perdues (> 5 min, « timeout »), envoie les
  livraisons dues (tentative enregistrée AVANT l'envoi : deux instances ne livrent pas deux fois la même tentative),
  purge les livraisons terminées après 30 jours. Retry : 6 tentatives, 30 s puis doublement, plafond 1 h.
- Un endpoint désactivé (ou un secret illisible) : ses livraisons en attente sont abandonnées à leur échéance
  (`endpoint_disabled`, `secret_missing`). Une livraison abandonnée peut être renvoyée (nouvelle livraison, même id).

### Clés d'API

- Clé `srk_` + base64url(32 octets), montrée UNE fois ; seule son empreinte SHA-256 est stockée (index unique).
- Authentification : en-tête `X-Api-Key`. Le schéma par défaut de l'hôte (`BearerOrApiKey`) choisit la clé si l'en-tête
  est présent, sinon l'access token. Le tenant (`tid`) est CELUI DU CREDENTIAL ; chaque scope devient un claim `api_scope`.
- Autorisation : une clé a une permission si et seulement si elle porte le scope du même nom
  (`PermissionAuthorizationHandler`). `UseApiKeyRestrictions` lui ferme toute route SANS permission (profil, sélection
  de restaurant, routes de la plateforme, routes anonymes), sauf les lectures (`GET`) qu'un groupe ouvre par
  `AllowApiKeyRead(scopes)` : commandes, carte, établissements, réservations, cuisine, salle, stocks et paiements, chacune
  avec les scopes de son module.
- **Catalogue des scopes** (fourni par l'hôte) : les permissions des restaurants SAUF `staff.*`, `integrations.*`,
  `billing.*`, `audit.*`, `tenant.*` et `storefront.*`. Une clé ne peut donc ni gérer le personnel, ni créer d'autres clés, ni toucher à l'abonnement. Un scope
  retiré du catalogue cesse de compter pour les clés déjà émises.

### Cas d'usage

| Cas d'usage | Déclencheur | Règles appliquées ici |
|---|---|---|
| `CreateWebhookEndpoint` | Personnel (`integrations.manage`) | Restaurant accessible en écriture ; événements du catalogue ; secret créé dans le coffre |
| `Subscribe/UnsubscribeWebhookEvent` | Personnel (`integrations.manage`) | Événement du catalogue |
| `SetWebhookEndpointEnabled` | Personnel (`integrations.manage`) | Désactiver reste permis à un restaurant suspendu |
| `RotateWebhookSecret` | Personnel (`integrations.manage`) | Nouveau secret, l'ancien supprimé |
| `PublishWebhookEvent` | Hôte (`WebhookFeed`, outbox) | Endpoints actifs et abonnés DU TENANT DU MESSAGE ; pas de doublon |
| `AttemptWebhookDelivery`, `RecoverStuckDelivery` | Job | Voir ci-dessus |
| `RetryDeadLetteredDelivery` | Personnel (`integrations.manage`) | Seulement une livraison abandonnée, endpoint actif et abonné |
| `IssueApiCredential`, `RotateApiCredential` | Personnel (`integrations.manage`) | Scopes du catalogue ; clé montrée une fois |
| `RevokeApiCredential` | Personnel (`integrations.manage`) | Permis à un restaurant suspendu |
| `AuthenticateApiKey` | Schéma `ApiKey` | Clé inconnue, révoquée ou expirée : une seule réponse (401) |

### Intégration (hôte)

- `IntegrationTenantsFromTenants` ; `WebhookFeed` (outbox d'Orders, Payments, Reservations, Kitchen, Inventory et Catalog).
- `Integrations:SecretsKey` (32 octets en base64) OBLIGATOIRE ; `Integrations:RunWorkers`.
- `AddAuthentication(ApiKeyAuthentication.SelectorScheme).AddIdentityAccessTokens().AddIntegrationsApiKeys()` ;
  `app.UseApiKeyRestrictions()` après `UseAuthentication`.

### Routes (`/integrations`, tenant du jeton, `Tenancy.TenantMemberPolicy`)

| Route | Permission |
|---|---|
| `GET /integrations/catalog`, `GET /integrations/webhooks`, `GET /integrations/webhooks/{id}`, `GET /integrations/webhooks/{id}/deliveries?status&page&pageSize`, `GET /integrations/webhook-deliveries/{id}`, `GET /integrations/api-keys`, `GET /integrations/api-keys/{id}` | `integrations.view` |
| `POST /integrations/webhooks` (201 + secret), `POST .../events`, `DELETE .../events/{eventType}`, `POST .../enable`, `POST .../disable`, `POST .../rotate-secret`, `POST /integrations/webhook-deliveries/{id}/retry`, `POST /integrations/api-keys` (201 + clé), `POST .../rotate`, `POST .../revoke` | `integrations.manage` |

Migration appliquée au démarrage (`MigrateIntegrationsDatabaseAsync`) ; job coupable par `Integrations:RunWorkers=false`.

## 8. Tests (étape 7)

| Projet | Contenu |
|---|---|
| `Integrations.Domain.Tests` | Agrégats et objets-valeurs |
| `Integrations.Application.Tests` | `IntegrationsUseCaseTests` : secret montré une fois, catalogue, URL interne, tenant suspendu, isolation, rotation, publication (endpoints actifs et abonnés du tenant seulement, rejeu), envoi signé vérifié, retry puis abandon, endpoint désactivé, job limité au tenant du scope, tentative perdue, renvoi ; clés (empreinte, scopes hors catalogue, révoquée, expirée, renouvelée, malformée, scope retiré du catalogue, isolation) ; `WebhookSignatureTests` (vecteur Standard Webhooks) |
| `Integrations.Infrastructure.Tests` | `IntegrationsModelTests` (schéma, index uniques, aucune colonne en clair) ; `SafeWebhookSenderTests` (plages refusées, envoi vers une adresse interne bloqué avant connexion) ; `IntegrationsPersistenceTests` : relecture, isolation, abonnement, unicité d'une livraison vivante et renvoi, files du job, purge, recherche par empreinte, coffre chiffré et isolé, outbox sans secret |
| `Integrations.Api.Tests` | Authentification, permissions, secret jamais relu, 404 pour un autre restaurant, refus, catalogue ; clé d'API : tenant du credential, scope, routes sans permission et routes d'intégration fermées, scope hors catalogue, clés inconnue/révoquée/renouvelée |
| `SaasRestaurant.Host.Tests` | Clé inconnue refusée partout |

## 9. Audit (étape 8)

| Point | Application | Vérification |
|---|---|---|
| TenantId toujours issu du contexte | Aucune route ne reçoit de tenant ; pour une clé, le tenant est celui du credential ; pour le job et l'outbox, celui de la ligne ou du message | `IntegrationsEndpointsTests`, `IntegrationsUseCaseTests` |
| Lecture croisée impossible | Filtre EF ; 404 sur l'id d'un autre ; coffre filtré par tenant ET donnée associée du chiffrement | `IntegrationsPersistenceTests`, `IntegrationsEndpointsTests` |
| Secrets de signature | Chiffrés en base, montrés une fois, jamais relus par une route, absents des événements, de l'outbox, des journaux et des exceptions | `IntegrationsModelTests`, `IntegrationsPersistenceTests`, revue |
| Clés d'API | Empreinte seule ; jamais journalisée ni renvoyée après l'émission ; réponse unique au refus | `IntegrationsUseCaseTests`, `IntegrationsEndpointsTests` |
| Escalade par clé | Scopes limités au catalogue ; pas de `staff.*`, `integrations.*`, `billing.*` ; routes sans permission fermées | `IntegrationsEndpointsTests` |
| SSRF | Contrôle lexical (domaine) + contrôle de chaque adresse résolue à la connexion ; ni redirection ni proxy | `SafeWebhookSenderTests` |
| Données personnelles | Liste blanche des données publiées | Revue (`WebhookFeed`) |
| Journal | Ids, hôte de destination, issues ; jamais un corps, un en-tête, une URL complète ni un secret | Revue |

Limites connues (après le lot 2, §10) : limite de débit par instance (pas globale) ; une clé n'a pas d'utilisateur, les
modules qui enregistrent « qui a fait » laissent ce champ vide ; le branchement au journal d'audit attend le module 21.

## 10. Lot 2 : durcissement

### Limite de débit par clé d'API

- `AddIntegrationsApiKeyRateLimit` : limiteur GLOBAL partitionné par credential (claim `cid`), fenêtre glissante, en
  mémoire de l'instance. Défaut : 600 requêtes par minute (6 segments de 10 s). Réglages :
  `Integrations:ApiKeyRateLimit:PermitLimit`, `:WindowSeconds`. Au-delà : 429 avec `Retry-After`.
- Seules les identités « ApiKey » sont comptées ; les utilisateurs (access token) ne le sont pas. Une requête refusée par
  l'autorisation (403) n'est pas comptée (`UseRateLimiter` après `UseAuthorization`).
- Avec N instances de l'hôte, une clé dispose de N fois le débit : passer à un compteur partagé (Redis) le jour où il
  faut une limite globale exacte.

### Désactivation automatique d'un endpoint

- Domaine : `WebhookEndpoint.ConsecutiveDeadLetters` ; `RecordDeliveryAbandoned(seuil, clock)` (abandon après TOUTES les
  tentatives) ; `RecordDeliverySucceeded()` remet à zéro ; `Enable` remet à zéro. Au seuil, un endpoint actif passe
  `Disabled`, `AutoDisabledAtUtc` est posé et `WebhookEndpointAutoDisabled` (URL masquée, nombre d'échecs) est publié.
- Seuil : `Integrations:AutoDisableAfterDeadLetters` (5). Les abandons `endpoint_disabled` et `secret_missing` ne
  comptent pas.
- La santé est écrite APRÈS l'issue de la livraison (deux enregistrements) : un conflit de version sur l'endpoint ne fait
  jamais perdre l'issue d'une livraison ; le compteur est seulement approximatif dans ce cas.
- Notification : `WebhookHealthFlow` (hôte, outbox) -> type `staff.webhook_disabled` (Notifications, priorité haute,
  variables `restaurant.name`, `webhook.url` MASQUÉE, `webhook.failures`), in-app, à chaque membre ACTIF du personnel qui
  a `integrations.manage`. Templates intégrés fr/en (in-app et e-mail).

### Période de grâce à la rotation du secret

- `RotateWebhookSecretReference(nouvelle, clock, grâce)` : l'ancien secret devient `PreviousSecretReference` jusqu'à
  `PreviousSecretExpiresAtUtc`. Pendant ce temps, CHAQUE envoi porte deux signatures (`webhook-signature: v1,<nouveau>
  v1,<ancien>`), ce que la convention Standard Webhooks prévoit : le destinataire accepte l'une ou l'autre.
- `POST .../rotate-secret` : grâce de `Integrations:SecretGracePeriod` (24 h, au plus 7 jours) ;
  `?immediate=true` : l'ancien secret est détruit aussitôt (secret compromis). Une seconde rotation pendant la grâce
  supprime l'ancien-ancien : jamais plus de deux secrets par endpoint.
- Le job oublie les anciens secrets expirés et les supprime du coffre (`ExpirePreviousWebhookSecretCommand`).

### URL masquée

- `WebhookUrl.Redacted` : schéma, hôte et port ; chemin et requête remplacés par `/***`. Utilisé dans les réponses
  (`url`), les événements (`WebhookEndpointCreated.RedactedUrl`, `WebhookEndpointAutoDisabled.RedactedUrl`) et la
  notification. `urlFingerprint` (12 caractères hexadécimaux du SHA-256 de l'URL complète) distingue deux endpoints
  d'un même hôte. L'URL complète ne sort plus que vers le destinataire lui-même.

### Dernière utilisation d'une clé, clés dormantes

- `ApiCredential.LastUsedAtUtc` : posé à l'authentification par un `UPDATE` direct et conditionnel
  (`IApiCredentialUsage`), au plus une écriture par clé et par `KeyUsageResolution` (1 h), sans événement ; un échec
  est journalisé et n'empêche jamais la requête.
- `IsDormantAt` : utilisable mais inutilisée depuis `Integrations:DormantKeyAfter` (90 jours ; depuis l'émission si
  jamais utilisée). Réponses : `lastUsedAtUtc`, `isDormant` ; `GET /integrations/api-keys?dormant=true`.

### Envoi de test

- `POST /integrations/webhooks/{id}/test` (`integrations.manage`) : événement `integrations.ping`
  (`data.endpoint_id`), signé comme un vrai (deux signatures pendant une grâce), envoyé tout de suite, même protection
  réseau. Aucune livraison enregistrée, aucune nouvelle tentative. Permis sur un endpoint désactivé (vérifier avant de
  réactiver). Réponse : `eventId`, `succeeded`, `failureReason`. `integrations.ping` n'est pas abonnable.

### Contrat pour l'audit (branchement au module 21)

Les événements sensibles portent `Actor` (`ChangeActor` : `User` + id d'utilisateur, `ApiCredential` + id de
credential, ou absent pour le système), fourni par la couche Api (`HttpIntegrationsActor`). Aucun ne contient de secret,
de référence de secret, de clé, d'empreinte ni d'URL complète. Correspondance prévue vers `AuditEntry` (consommateur
d'outbox de l'hôte, écrit avec le module 21) :

| Événement (outbox `integrations`) | Action d'audit | Cible | Acteur | Métadonnées |
|---|---|---|---|---|
| `ApiCredentialIssued` | `integrations.api_key.issued` | `api_credential` / `CredentialId` | `Actor` | `name`, `scopes`, `expires_at_utc` |
| `ApiCredentialRotated` | `integrations.api_key.rotated` | `api_credential` / `CredentialId` | `Actor` | — |
| `ApiCredentialRevoked` | `integrations.api_key.revoked` | `api_credential` / `CredentialId` | `Actor` | — |
| `WebhookEndpointCreated` | `integrations.webhook_endpoint.created` | `webhook_endpoint` / `EndpointId` | `Actor` | `url` (masquée) |
| `WebhookSecretReferenceRotated` | `integrations.webhook_secret.rotated` | `webhook_endpoint` / `EndpointId` | `Actor` | `previous_valid_until_utc` (absent : immédiate) |
| `WebhookEndpointDisabled` | `integrations.webhook_endpoint.disabled` | `webhook_endpoint` / `EndpointId` | `Actor` | — |
| `WebhookEndpointEnabled` | `integrations.webhook_endpoint.enabled` | `webhook_endpoint` / `EndpointId` | `Actor` | — |
| `WebhookEndpointAutoDisabled` | `integrations.webhook_endpoint.auto_disabled` | `webhook_endpoint` / `EndpointId` | `System` (`webhook-delivery-job`) | `url` (masquée), `consecutive_dead_letters` |

`ActorType` d'Audit : `User` <- `ChangeActorKind.User`, `ApiCredential` <- `ChangeActorKind.ApiCredential`, `System`
<- `ChangeActorKind.System` ou acteur absent. Le tenant est celui de l'événement. Non audité : l'envoi de test (aucun
changement d'état) ; les livraisons (volumineuses, déjà historisées dans `webhook_deliveries`).

### Migration

```bash
dotnet ef migrations add WebhookHardening --project src/Modules/Integrations/Integrations.Infrastructure --output-dir Persistence/Migrations
```

Colonnes ajoutées : `webhook_endpoints.previous_secret_reference`, `previous_secret_expires_at_utc`,
`consecutive_dead_letters`, `auto_disabled_at_utc` (+ index et deux CHECK) ; `api_credentials.last_used_at_utc`.

### Tests ajoutés

- Domaine : URL masquée, grâce et expiration de l'ancien secret, rotation immédiate, borne de la grâce, désactivation
  automatique et remises à zéro, auteur dans les événements, clé dormante, scopes publiés sans empreinte.
- Application (`IntegrationsHardeningTests`) : double signature vérifiée, expiration, une seule ancienne clé, seuil
  d'abandons et événement, remise à zéro, URL masquée et empreinte, auteur, envoi de test (signé, sans livraison,
  échec, autre restaurant), `integrations.ping` non abonnable, dernière utilisation bornée, liste des clés dormantes.
- Infrastructure : ancien secret relu et file du job, `UPDATE` conditionnel de la dernière utilisation, auteur dans
  l'outbox sans empreinte.
- Api : 429 au-delà du débit et indépendance des clés, utilisateurs non limités, envoi de test, URL jamais complète,
  rotation avec grâce ou immédiate, dernière utilisation visible.
