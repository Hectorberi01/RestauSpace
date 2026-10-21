# Module Reviews — Domain

But : collecter et traiter le feedback client.

## 1. Frontières d'agrégat

| Racine | Enfants | Référencé par id seulement |
|---|---|---|
| `Feedback` | `Rating` (VO) | `LocationId`, `CustomerId?`, `OrderId?`, `UserId` (kernel) ; à la soumission, établissement, client et commande arrivent sous forme de références locales (`LocationReference`, `CustomerReference`, `OrderReference`) dont seul l'id est stocké |
| `Survey` | `SurveyQuestion` (entité) | — |

- `Feedback` est un avis interne isolé : sa note et son commentaire sont la parole du client et ne sont jamais modifiés ; seul son statut de traitement évolue. Seule exception, qui n'est pas un changement métier mais un effacement : `DetachCustomer` retire le lien vers l'auteur et le commentaire quand ce client est anonymisé. Chaque avis est sa propre unité de cohérence.
- Le module Customers n'est jamais référencé : `CustomerReference` est un miroir local `(TenantId, CustomerId, IsAnonymized)` de la référence de Customers, rempli par l'Application.
- `Survey` possède ses questions : ordre, nombre et contenu n'ont de sens que dans le questionnaire, et la règle « publier exige une question » porte sur l'ensemble. Les questions ne sont créées et modifiées que par la racine (fabrique et mutateurs `internal`).
- Les réponses à un questionnaire ne font pas partie de ce périmètre (la spec ne les décrit pas).

## 2. Classes

| Classe | Rôle DDD | Responsabilité | Transitions | Events | Erreurs (`reviews.`) |
|---|---|---|---|---|---|
| `Rating` | Value Object | Note entière de 1 à 5 | — | — | `rating.out_of_range` |
| `Feedback` | Aggregate Root | Avis lié à un établissement (et éventuellement client / commande), suivi de traitement, détachement de l'auteur | `Submitted -> Reviewed`, `Submitted -> Archived`, `Reviewed -> Archived` ; `Archived` terminal (sauf `DetachCustomer`, permis dans tout statut, qui ne change pas le statut) | `FeedbackSubmitted`, `FeedbackReviewed`, `FeedbackArchived`, `FeedbackCustomerDetached` | `feedback.comment_too_long`, `.customer_anonymized`, `.not_submitted`, `.already_archived`, `common.tenant.mismatch`, `common.id.empty` |
| `CustomerReference` | Value Object (référence locale, publique) | Ce que Reviews sait de l'auteur au moment de la soumission : `TenantId`, `CustomerId`, `IsAnonymized` ; ids gardés | — | — | `common.id.empty` |
| `LocationReference` | Record (référence locale, publique) | `(TenantId, LocationId, IsOperational)` ; ids gardés. Seul le tenant est vérifié | — | — | `common.id.empty` |
| `OrderReference` | Record (référence locale, publique) | `(TenantId, OrderId)` ; ids gardés. Seul le tenant est vérifié | — | — | `common.id.empty` |
| `Survey` | Aggregate Root | Titre, questions ordonnées, cycle de vie | `Draft -> Published -> Closed` ; `Closed` terminal | `SurveyPublished`, `SurveyClosed` | `survey.title_required`, `.title_too_long`, `.not_draft`, `.not_published`, `.closed`, `.no_question`, `.too_many_questions`, `.question_not_found`, `.question_order_out_of_range` |
| `SurveyQuestion` | Entity (enfant) | Texte, type, obligatoire, position, choix | — | — | `question.text_required`, `.text_too_long`, `.type_unknown`, `.choices_required`, `.choices_not_allowed`, `.too_many_choices`, `.choice_invalid`, `.choice_duplicate` |
| `FeedbackStatus`, `SurveyStatus`, `SurveyQuestionType` | Enums | Listes fermées | — | — | — |

## 3. Choix faits là où la spec était ouverte

- **`Survey.Title`** : absent de la liste de propriétés de la spec, ajouté (1..200 car.) — un questionnaire sans nom n'est pas administrable. `Rename` ajouté, en brouillon seulement.
- **`Archive` depuis `Submitted`** : autorisé (spam, doublon : rien à traiter). `MarkReviewed` n'est possible que depuis `Submitted`.
- **Commentaire** : optionnel, trimé, vide = `null`, 2000 car. au maximum.
- **Avis anonyme** : client et `OrderId` optionnels. `FeedbackSubmitted` porte la note et un drapeau `HasComment`, pas le texte.
- **Auteur vérifié localement** : `Feedback.Submit` reçoit un `CustomerReference?` et non un `CustomerId?` nu : le client doit être du même tenant (`common.tenant.mismatch`) et ne pas être anonymisé (`reviews.feedback.customer_anonymized`). Il en va de même de l'établissement et de la commande : `Submit` reçoit une `LocationReference` (obligatoire) et une `OrderReference?` (optionnelle), toutes deux vérifiées contre le tenant de l'avis (`common.tenant.mismatch`) ; l'avis ne stocke que `LocationId` et `OrderId?`. `LocationReference.IsOperational` n'est **pas** exigé : on peut noter une expérience passée dans un établissement fermé depuis.
- **Ids vides** : `Feedback.Submit` et `Survey.Create` rejettent un `TenantId` par défaut, les trois références rejettent un id par défaut, `MarkReviewed` rejette un `UserId` par défaut (`common.id.empty`).
- **Horloge lue une fois par opération** : `MarkReviewed`, `Archive`, `DetachCustomer`, `Publish`, `Close` posent leur date d'état et l'`OccurredAtUtc` de l'événement avec le même instant.
- **Détachement à l'anonymisation** : `DetachCustomer(clock)` met `CustomerId` et `Comment` à `null` (un texte libre peut identifier son auteur), pose `CustomerDetachedAtUtc` (`IsCustomerDetached`) et lève `FeedbackCustomerDetached` — ids de l'avis et du tenant seulement, **sans** l'id du client effacé. Permis dans **tous** les statuts, `Archived` compris. Note, établissement, commande, statut et dates de traitement sont conservés : les statistiques restent justes. Idempotent : sans client (déjà détaché, ou avis anonyme dès l'origine) c'est un no-op sans événement ; un avis anonyme garde donc son commentaire et `IsCustomerDetached` reste `false`. C'est voulu : rien ne relie ce texte au client effacé (l'avis n'a jamais porté son id), il n'y a donc rien à effacer au titre de son anonymisation.
- **Traçabilité du traitement** : `MarkReviewed` prend le `UserId` du relecteur ; `ReviewedBy`, `ReviewedAtUtc`, `ArchivedAtUtc` ajoutés. Events `FeedbackReviewed` et `FeedbackArchived` ajoutés.
- **Questions** : types `Rating`, `FreeText`, `SingleChoice`, `MultipleChoice`. Les types à choix exigent 2 à 20 choix, non vides, ≤ 200 car., distincts sans tenir compte de la casse ; les autres types refusent tout choix. Texte 1..500 car. 50 questions au maximum.
- **Ordre** : `Order` est une position 1..n contiguë ; `AddQuestion` ajoute en fin, `MoveQuestion` et `RemoveQuestion` renumérotent. `Order` est la seule vérité : `Survey.Questions` est exposée triée par `Order` et la renumérotation trie d'abord, donc rien ne dépend de l'ordre dans lequel l'ORM a chargé la collection.
- **Même valeur = no-op silencieux** : `Rename` avec le même titre et `MoveQuestion` vers la position actuelle ne font rien (les contrôles d'état et d'existence restent appliqués avant). Les transitions répétées (`Publish`, `Close`, `MarkReviewed`, `Archive`) restent rejetées.
- **Édition** (`Rename`, `AddQuestion`, `ChangeQuestion`, `MoveQuestion`, `RemoveQuestion` — les trois dernières ajoutées) : uniquement en `Draft`. Une fois publié, le questionnaire est figé pour que les réponses gardent leur sens.
- **`Close`** : uniquement depuis `Published` ; un brouillon ne se ferme pas (on ne ferme que ce qui a été ouvert). `Closed` rejette tout, avec l'erreur dédiée `reviews.survey.closed`.
- `Survey.Create` ne lève pas d'événement (la spec ne liste que `SurveyPublished`).

## 4. Matrice invariant -> méthode -> test

| Invariant | Méthode | Test |
|---|---|---|
| Note entre 1 et 5 (test « rating ») | `Rating.Create` | `RatingTests.Create_accepts_ratings_from_1_to_5`, `Create_rejects_ratings_outside_1_to_5` |
| Un avis ne peut pas porter une note hors bornes | `Feedback.Submit` | `FeedbackTests.Submit_accepts_the_rating_bounds`, `Submit_cannot_receive_a_rating_outside_1_to_5` |
| `FeedbackSubmitted` à la soumission | `Feedback.Submit` | `FeedbackTests.Submit_raises_FeedbackSubmitted`, `Submit_creates_a_submitted_feedback_linked_to_its_experience` |
| Client / commande optionnels | `Feedback.Submit` | `FeedbackTests.Submit_accepts_an_anonymous_feedback_without_order` |
| Auteur du même tenant | `Feedback.Submit` | `FeedbackTests.Submit_rejects_a_customer_of_another_tenant` |
| Établissement du même tenant (opérationnel ou non) | `Feedback.Submit` | `FeedbackTests.Submit_rejects_a_location_of_another_tenant`, `Submit_accepts_a_location_that_is_no_longer_operational` |
| Commande du même tenant | `Feedback.Submit` | `FeedbackTests.Submit_rejects_an_order_of_another_tenant` |
| Tenant et ids référencés non vides | `Feedback.Submit`, `Survey.Create`, références, `MarkReviewed` | `FeedbackTests.Submit_rejects_an_empty_tenant`, `References_reject_empty_ids`, `MarkReviewed_rejects_an_empty_reviewer`, `SurveyTests.Create_rejects_an_empty_tenant` |
| Date d'état = instant de l'événement | `MarkReviewed`, `Archive`, `DetachCustomer`, `Publish`, `Close` | `FeedbackTests.State_timestamps_equal_the_event_instant`, `SurveyTests.Publish_and_Close_stamp_the_state_and_the_event_with_the_same_instant` |
| Pas d'avis au nom d'un client anonymisé | `Feedback.Submit` | `FeedbackTests.Submit_rejects_an_anonymized_customer` |
| Commentaire optionnel, trimé, borné | `Feedback.Submit` | `FeedbackTests.Submit_stores_a_blank_comment_as_no_comment`, `Submit_accepts_a_comment_at_the_limit_and_rejects_a_longer_one` |
| `Submitted -> Reviewed` | `MarkReviewed` | `FeedbackTests.MarkReviewed_moves_a_submitted_feedback_to_reviewed` |
| Pas de double relecture | `MarkReviewed` | `FeedbackTests.MarkReviewed_rejects_an_already_reviewed_feedback` |
| `Reviewed -> Archived`, `Submitted -> Archived` | `Archive` | `FeedbackTests.Archive_moves_a_reviewed_feedback_to_archived`, `Archive_is_allowed_directly_from_submitted` |
| `Archived` terminal | `MarkReviewed`, `Archive` | `FeedbackTests.MarkReviewed_rejects_an_archived_feedback`, `Archive_rejects_an_already_archived_feedback` |
| Note et commentaire immuables (hors effacement) | — (aucun mutateur) | `FeedbackTests.Rating_and_comment_never_change_through_the_lifecycle` |
| Détachement : auteur et commentaire effacés, le reste conservé | `DetachCustomer` | `FeedbackTests.DetachCustomer_erases_the_author_and_the_comment_and_keeps_the_rest` |
| `FeedbackCustomerDetached` avec ids seulement | `DetachCustomer` | `FeedbackTests.DetachCustomer_raises_FeedbackCustomerDetached_with_ids_only` |
| Détachement permis dans tout statut, `Archived` compris | `DetachCustomer` | `FeedbackTests.DetachCustomer_is_allowed_on_a_reviewed_feedback_and_keeps_its_review`, `DetachCustomer_is_allowed_on_an_archived_feedback`, `A_detached_feedback_still_follows_its_lifecycle` |
| Détachement idempotent, sans effet sur un avis anonyme | `DetachCustomer` | `FeedbackTests.DetachCustomer_twice_is_a_silent_no_op`, `DetachCustomer_leaves_an_anonymous_feedback_untouched` |
| Titre requis / borné | `Survey.Create`, `Rename` | `SurveyTests.Create_rejects_a_blank_title`, `Create_accepts_a_title_at_the_limit_and_rejects_a_longer_one`, `Rename_rejects_a_blank_or_a_too_long_title` |
| Même valeur = no-op silencieux | `Rename`, `MoveQuestion` | `SurveyTests.Rename_to_the_current_title_is_a_silent_no_op`, `MoveQuestion_to_the_current_position_is_a_silent_no_op` |
| Texte de question requis / borné, type connu | `AddQuestion`, `ChangeQuestion` | `SurveyTests.AddQuestion_rejects_a_blank_text`, `AddQuestion_accepts_a_text_at_the_limit_and_rejects_a_longer_one`, `AddQuestion_rejects_an_unknown_type` |
| Question à choix : au moins deux choix | `AddQuestion`, `ChangeQuestion` | `SurveyTests.AddQuestion_requires_at_least_two_choices_for_a_choice_question` |
| Pas de choix sur une question sans choix | `AddQuestion`, `ChangeQuestion` | `SurveyTests.AddQuestion_rejects_choices_on_a_non_choice_question`, `ChangeQuestion_to_a_non_choice_type_drops_the_choices` |
| Choix valides, distincts, en nombre borné | `AddQuestion` | `SurveyTests.AddQuestion_rejects_blank_too_long_or_duplicate_choices`, `AddQuestion_accepts_the_maximum_number_of_choices_and_rejects_more` |
| Nombre de questions borné | `AddQuestion` | `SurveyTests.AddQuestion_rejects_more_questions_than_the_maximum` |
| Ordre contigu 1..n | `AddQuestion`, `MoveQuestion`, `RemoveQuestion` | `SurveyTests.AddQuestion_appends_questions_in_order`, `MoveQuestion_reorders_the_questions_contiguously`, `RemoveQuestion_removes_the_question_and_closes_the_gap`, `MoveQuestion_rejects_a_position_outside_the_survey` |
| Modification atomique d'une question | `ChangeQuestion` | `SurveyTests.ChangeQuestion_rejects_invalid_content_and_leaves_the_question_untouched` |
| Ordre indépendant de l'ordre de chargement | `Survey.Questions`, `AddQuestion`, `MoveQuestion`, `RemoveQuestion` | `SurveyTests.Questions_are_exposed_by_order_whatever_the_load_order`, `MoveQuestion_does_not_depend_on_the_load_order`, `RemoveQuestion_does_not_depend_on_the_load_order`, `AddQuestion_appends_after_the_last_position_whatever_the_load_order` |
| Question connue du questionnaire | `ChangeQuestion`, `MoveQuestion`, `RemoveQuestion` | `SurveyTests.ChangeQuestion_rejects_an_unknown_question`, `MoveQuestion_rejects_an_unknown_question`, `RemoveQuestion_rejects_an_unknown_question` |
| Publier exige une question (test « publication ») | `Publish` | `SurveyTests.Publish_requires_at_least_one_question`, `Publish_is_rejected_once_the_only_question_has_been_removed`, `Publish_moves_a_draft_with_one_question_to_published_and_raises_SurveyPublished` |
| `Publish` depuis `Draft` seulement | `Publish` | `SurveyTests.Publish_rejects_an_already_published_survey` |
| Questions éditables en `Draft` seulement | `Rename`, `AddQuestion`, `ChangeQuestion`, `MoveQuestion`, `RemoveQuestion` | `SurveyTests.Published_survey_rejects_every_edition` |
| `Published -> Closed` seulement | `Close` | `SurveyTests.Close_moves_a_published_survey_to_closed_and_raises_SurveyClosed`, `Close_rejects_a_draft` |
| `Closed` terminal | toutes | `SurveyTests.Close_rejects_an_already_closed_survey`, `Closed_survey_rejects_every_mutation` |
| Questions non modifiables de l'extérieur | `Survey.Questions` | `SurveyTests.Questions_cannot_be_modified_from_outside` |

## 5. Ce qui relève de l'Application / Infrastructure

- **Références** : l'Application charge l'établissement (Tenants), la commande (Orders) et le client (Customers) sous isolation tenant et remplit `LocationReference(TenantId, LocationId, IsOperational)`, `OrderReference(TenantId, OrderId)` et `CustomerReference(TenantId, CustomerId, IsAnonymized)` à partir des agrégats chargés, jamais d'une saisie : c'est le domaine qui tranche sur le tenant. Reste à l'Application, si le produit le veut, de vérifier que la commande est bien celle du client et de l'établissement.
- **Un avis par commande** : non imposé par le domaine (la spec ne le demande pas) ; si la règle est retenue, port d'unicité + index unique `(TenantId, OrderId)`.
- **Anonymisation client** : sur `CustomerAnonymized` (module Customers), l'Application charge tous les avis du client (par `CustomerId`, tous statuts) et appelle `DetachCustomer` sur chacun ; l'appel est idempotent, donc rejouable. Les projections de lecture qui auraient copié le commentaire sont purgées sur `FeedbackCustomerDetached`.
- **Persistance** : aucun tri imposé au chargement de `Survey.Questions` (le domaine trie par `Order`) ; version optimiste sur `Survey` (éditions concurrentes du brouillon) et sur `Feedback`.
- **Diffusion** : exposition du questionnaire publié, collecte et stockage des réponses, notifications sur `FeedbackSubmitted` (alerte note basse).

## 6. Persistance (étape 1)

Schéma `reviews`, historique des migrations dans le même schéma, outbox du module (`reviews`).

| Table | Contenu | Gardes en base |
|---|---|---|
| `feedbacks` | id, tenant, établissement, client (null : anonyme ou détaché), commande, `rating` (entier, colonne d'un type complexe), commentaire (2000), statut, soumission, examen (par qui, quand), archivage, détachement, `xmin` | CHECK note 1-5, statut connu, examen « par qui » et « quand » ensemble, avis détaché sans client ni commentaire ; index unique PARTIEL `ix_feedbacks_tenant_id_order_id` (un avis par commande) ; index (tenant, soumission), (tenant, établissement, soumission), (tenant, client) |
| `surveys` | id, tenant, titre (200), statut, publication, clôture, `xmin` | CHECK statut connu, publié => date de publication, fermé <=> date de clôture |
| `survey_questions` | id, questionnaire (FK, suppression en cascade), texte (500), type, obligatoire, `position`, `choices` (jsonb) | CHECK type connu, position >= 1 |

Version optimiste (`xmin`) sur `Feedback` et `Survey`. Modifier une question n'écrit que sa ligne : l'unit of work
réécrit alors aussi le questionnaire, pour que sa version protège le brouillon entier (deux éditions simultanées, même
de deux questions différentes, ou une édition pendant la publication : la seconde échoue en 409).

Retouche du domaine : `SurveyQuestion._choices` n'est plus `readonly` (l'ORM remplace la liste au chargement, comme les
listes d'ids de Catalog). Aucune autre modification du domaine.

Migration à générer :

```bash
dotnet ef migrations add InitialCreate --project src/Modules/Reviews/Reviews.Infrastructure --output-dir Persistence/Migrations
```

## 7. Cas d'usage, liaisons et API (étapes 2 à 6)

### Avis

| Cas d'usage | Permission | Règle |
|---|---|---|
| `SubmitFeedback` | `reviews.submit` | Établissement, client et commande chargés dans le tenant du CONTEXTE (404 sinon) ; la commande doit être de cet établissement, du client s'il est donné et si elle en a un, et n'avoir aucun avis (vérification puis index unique, 422) ; client anonymisé refusé (domaine) |
| `MarkFeedbackReviewed` | `reviews.manage` | L'examinateur est l'utilisateur du token (`sub`) ; une clé d'API n'a pas d'utilisateur : 403 `reviews.feedback.reviewer_required` |
| `ArchiveFeedback` | `reviews.manage` | Depuis Submitted ou Reviewed |
| `DetachCustomerFeedbacks` | aucune route | Sur `CustomerAnonymized` ; tous statuts ; possible même restaurant suspendu ; idempotent |
| `GetFeedback`, `ListFeedbacks`, `GetFeedbackSummary` | `reviews.view` | Filtres : établissement, statut, archivés inclus, note min/max, période [from, to[, client, commande, avec commentaire ; pages de 100 au plus ; statistiques (nombre, moyenne à 2 décimales, répartition 1-5, avec commentaire, par statut) calculées EN BASE |

Les avis archivés (spam, doublons) sont exclus des listes et des statistiques, sauf `includeArchived=true` ou
`status=Archived`. Un restaurant suspendu lit ses avis mais n'en écrit plus (422 `reviews.tenant.not_writable`).

### Questionnaires (`reviews.manage` ; lecture `reviews.view`)

`CreateSurvey`, `RenameSurvey`, `AddSurveyQuestion`, `ChangeSurveyQuestion`, `MoveSurveyQuestion`,
`RemoveSurveyQuestion`, `PublishSurvey`, `CloseSurvey`, `GetSurvey`, `ListSurveys(status)`. Toutes les règles (brouillon
seul modifiable, 50 questions, 2 à 20 choix distincts, au moins une question pour publier) sont celles de l'agrégat.

### Routes (groupe `/reviews`, `Tenancy.TenantMemberPolicy`)

| Route | Permission |
|---|---|
| `POST /reviews/feedbacks` | `reviews.submit` |
| `GET /reviews/feedbacks`, `GET /reviews/feedbacks/summary`, `GET /reviews/feedbacks/{id}` | `reviews.view` |
| `POST /reviews/feedbacks/{id}/reviewed`, `POST /reviews/feedbacks/{id}/archive` | `reviews.manage` |
| `GET /reviews/surveys[?status=]`, `GET /reviews/surveys/{id}` | `reviews.view` |
| `POST /reviews/surveys`, `PUT /reviews/surveys/{id}/title`, `POST /reviews/surveys/{id}/questions`, `PUT /reviews/surveys/{id}/questions/{questionId}`, `PUT /reviews/surveys/{id}/questions/{questionId}/position`, `DELETE /reviews/surveys/{id}/questions/{questionId}`, `POST /reviews/surveys/{id}/publish`, `POST /reviews/surveys/{id}/close` | `reviews.manage` |

Aucune route ne prend de tenant. Les permissions `reviews.*` sont ouvertes aux clés d'API (une application du
restaurant peut enregistrer des avis avec le scope `reviews.submit`).

### Liaisons par l'outbox (hôte, `ReviewsBridges.cs`)

- Ports : `ReviewTenantsFromTenants`, `ReviewLocationsFromLocations`, `ReviewOrdersFromOrders` (tenant, id,
  établissement, client : ni montant ni lignes), `ReviewCustomersFromCustomers` (tenant, id, anonymisé).
- `ReviewsAnonymizationFlow` : `customers.CustomerAnonymized` -> `DetachCustomerFeedbacks` (auteur ET commentaire
  retirés de tous ses avis).
- `ReviewAlertFlow` : `reviews.FeedbackSubmitted` avec une note <= `Reviews:LowRatingAlertThreshold` (2 par défaut,
  0 = jamais) -> notification in-app `staff.new_review` aux membres ACTIFS ayant `reviews.manage` sur l'établissement.
  Variables : restaurant, établissement, note. JAMAIS le commentaire ni l'auteur. Idempotente par (événement,
  destinataire). Le restaurant doit avoir un template `staff.new_review` (sinon : journalisé et ignoré).
- Audit (`AuditFeed`) : `reviews.feedback.reviewed` (auteur : l'examinateur), `reviews.feedback.archived`,
  `reviews.feedback.customer_detached` (auteur : `customer-anonymization`), `reviews.survey.published`
  (nombre de questions), `reviews.survey.closed`. Un avis soumis n'est pas audité (ce n'est pas une action sensible).

### Job (étape 5)

Aucun : rien n'arrive à échéance dans ce module. L'effacement d'un client et l'alerte de note basse passent par
l'outbox, avec ses nouvelles tentatives.

## 8. Tests (étape 7)

| Projet | Contenu |
|---|---|
| `Reviews.Application.Tests` | Soumission complète ; avis anonyme sur un établissement fermé ; références d'un autre restaurant introuvables ; commande d'un autre établissement, d'un autre client, déjà évaluée ; règles du domaine ; restaurant suspendu ; examinateur = utilisateur (403 pour une clé) ; isolation ; sans restaurant choisi ; anonymisation rejouable ; listes, archivés, filtres ; filtres et pages invalides ; statistiques ; questionnaire de bout en bout, figé une fois publié, renumérotation, isolation |
| `Reviews.Infrastructure.Tests` | Modèle (schéma, noms, colonnes bornées, index unique partiel, versions, cascade, migration à jour) ; relecture complète et détachement ; isolation par tenant ; second avis pour une commande refusé EN BASE ; recherche et décomptes en base ; questionnaire relu avec ses questions dans l'ordre et leurs choix ; éditions concurrentes du brouillon en conflit ; examen et archivage simultanés en conflit |
| `Reviews.Api.Tests` | Authentification et restaurant requis ; parcours d'un avis (création, doublon de commande 422, examen, filtres, statistiques) ; permission par route (tablette `reviews.submit`, lecture seule `reviews.view`) ; 404 entre restaurants ; restaurant suspendu ; questionnaire publié puis figé |
| `SaasRestaurant.Host.Tests` | Règles d'audit de Reviews (auteur, aucune donnée de l'avis) ; routes `/reviews` câblées et protégées |

## 9. Audit (étape 8)

| Point | Application | Vérification |
|---|---|---|
| TenantId issu du contexte | Aucune route ni commande ne reçoit de tenant ; références chargées dans le tenant du contexte ; double filtre (dépôt + query filter) | `FeedbackUseCaseTests`, `ReviewsPersistenceTests`, `ReviewsEndpointsTests` |
| Établissement du même tenant | Chargé par Locations pour le tenant du contexte, puis vérifié par le domaine | `References_of_another_restaurant_are_not_found` |
| Données personnelles | Commentaire réservé à `reviews.view` ; jamais dans un événement (seulement `HasComment`), un journal, une notification ni l'audit ; retiré avec le lien à l'anonymisation | `AuditFeedTests`, `ReviewsAnonymizationFlow`, `Anonymizing_a_customer...` |
| Journaux | Ids et nombres seulement | Revue de `ReviewsBridges.cs` |
| Concurrence | `xmin` sur avis et questionnaire, questionnaire réécrit quand une question change ; un avis par commande garanti en base | `ReviewsPersistenceTests` |
| Secrets | Le module n'en manipule aucun | — |

Limites connues : les réponses aux questionnaires ne sont pas encore collectées (le domaine ne les modélise pas : un
agrégat `SurveyResponse` et une route de réponse sont le prochain lot) ; aucune route publique de dépôt d'avis sans
jeton (un client passe par une tablette du restaurant ou par une application munie d'une clé d'API) ; pas de
sollicitation d'avis sur `OrderCompleted` ; un avis n'est pas publié en webhook ; le statut de la commande évaluée
n'est pas vérifié (une commande annulée peut recevoir un avis).
