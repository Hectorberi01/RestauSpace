# Réservations, avis et questionnaires

> Réserver une table en ligne, gérer sa réservation par un lien, afficher la note et les avis du restaurant, recueillir
> l'avis des clients.

Routes sous `{{API_URL}}/v1/storefront`, clé `X-Storefront-Key` d'un site ou d'une application mobile (une borne ne
réserve pas : `storefront.reservation.kiosk_not_allowed`).

## Réserver une table

### 1. Les heures possibles : `GET /locations/{id}/booking?date=2026-10-18&partySize=4`

```json
{
  "locationId": "0199…", "timeZoneId": "Africa/Porto-Novo", "date": "2026-10-18",
  "durationMinutes": 90, "maxPartySize": 8, "cancellationCutoffMinutes": 120,
  "times": [{"startAtUtc": "2026-10-18T18:00:00+00:00", "isAvailable": true}, {"startAtUtc": "2026-10-18T18:30:00+00:00", "isAvailable": false}]
}
```

- `date` : date locale de l'établissement, `AAAA-MM-JJ` (obligatoire) ; `partySize` : 2 par défaut.
- Une heure toutes les 30 minutes pendant les horaires, le repas entier (`durationMinutes`) devant finir avant la
  fermeture.
- Les heures trop proches (délai minimum de réservation) ne sont pas proposées. `isAvailable: false` : complet, hors de
  la période de réservation, ou groupe plus grand que `maxPartySize`.
- Refus : `storefront.reservation.disabled` (pas de réservation en ligne dans cet établissement),
  `storefront.reservation.date_invalid` (date passée ou trop lointaine), `storefront.location.not_found`.

### 2. Réserver : `POST /reservations`

En-têtes : `Idempotency-Key` (obligatoire), `X-Human-Check` si exigé, `X-Customer-Token` si le client est connecté
(la réservation est alors rattachée à son compte).

```json
{
  "locationId": "0199…",
  "startAt": "2026-10-18T18:00:00+00:00",
  "partySize": 4,
  "name": "Afi",
  "phone": "+2290197000000",
  "email": null,
  "notes": "Anniversaire"
}
```

- `startAt` : une des heures proposées ; `phone` obligatoire (international) ; `notes` : 500 caractères au plus.
- La réservation est confirmée tout de suite. Réponse `200` :

```json
{"token": "UmVz…", "startAtUtc": "2026-10-18T18:00:00+00:00", "partySize": 4}
```

Gardez `token` : c'est le lien de gestion de la réservation (à mettre dans l'écran de confirmation, ou dans votre lien
« Gérer ma réservation »).

Refus fréquents : `reservations.reservation.capacity_insufficient` (plus de place), `lead_time_not_respected`,
`beyond_booking_horizon`, `party_size_too_large`, `location_closed_during_slot`, `common.phone.invalid`.

### 3. Consulter : `GET /reservations/{token}`

```json
{
  "status": "Confirmed", "locationId": "0199…", "locationName": "Chez Awa Cotonou",
  "startAtUtc": "2026-10-18T18:00:00+00:00", "durationMinutes": 90, "partySize": 4, "guestName": "Afi",
  "canCancel": true, "cancelBeforeUtc": "2026-10-18T16:00:00+00:00"
}
```

`status` : `Pending`, `Confirmed`, `Seated`, `Completed`, `Cancelled`, `NoShow`.

### 4. Annuler : `POST /reservations/{token}/cancel`

Réponse `204`, tant que `canCancel` vaut `true`. Ensuite : `reservations.reservation.cancellation_cutoff_passed` ;
déjà annulée ou terminée : `reservations.reservation.already_terminal`.

## Avis clients

Le restaurant reçoit des avis (note de 1 à 5 et commentaire facultatif) : sur votre site après une commande, sur la
tablette en salle, ou saisis par l'équipe. Il **choisit** dans la console (Avis › Publier) ceux qui s'affichent en
ligne.

### Afficher la note et les avis : `GET /reviews?locationId=…&take=10`

```bash
curl -s "{{API_URL}}/v1/storefront/reviews?take=3" -H "X-Storefront-Key: $KEY"
```

```json
{
  "count": 128,
  "averageRating": 4.47,
  "countByRating": [
    {"rating": 1, "count": 3}, {"rating": 2, "count": 4}, {"rating": 3, "count": 9}, {"rating": 4, "count": 26}, {"rating": 5, "count": 86}
  ],
  "reviews": [
    {"id": "0199…", "locationId": "0199…", "rating": 5, "comment": "Le meilleur attiéké de Cotonou.", "submittedAtUtc": "2026-10-12T19:04:00+00:00"}
  ]
}
```

- `count`, `averageRating` (deux décimales, `null` sans avis) et `countByRating` portent sur **tous** les avis du
  restaurant, sauf ceux qu'il a archivés (spam, doublons), qu'ils soient publiés ou non.
- `reviews` : seulement les avis **publiés**, du plus récent au plus ancien ; `take` de 0 à 50 (10 par défaut, `0` pour
  n'avoir que la note). Le commentaire peut être `null` (note seule, ou auteur effacé à sa demande).
- L'auteur n'est **jamais** renvoyé : affichez « Client vérifié » ou rien. N'ajoutez pas de prénom inventé.
- `locationId` (facultatif) : un établissement servi par l'application (`404 storefront.location.not_found` sinon).
  Sans lui, les avis de tous les établissements que l'application sert.
- Mettez la réponse en cache quelques minutes : elle change peu.

### Laisser un avis après une commande : `POST /reviews`

Proposez-le sur l'écran de suivi quand la commande est terminée (`status: "Completed"`), et dans le message de
remerciement.

```json
{"orderToken": "pZ8xQ…", "rating": 5, "comment": "Livré chaud et à l'heure !"}
```

- `orderToken` : le jeton de suivi d'une commande **terminée** passée par l'application.
- `rating` : de 1 à 5 ; `comment` : facultatif, 2 000 caractères au plus.
- Réponse `201` avec `{"id": "…"}`. L'avis est rattaché à la commande et à son client : il est effacé avec lui si le
  client demande la suppression de ses données.
- Un seul avis par commande. Indiquez au client que son avis peut être publié sur le site, **sans son nom**.

Refus : `storefront.order.not_found` (404, jeton inconnu ou d'un établissement non servi),
`storefront.review.order_not_completed` (422), `reviews.feedback.order_already_reviewed` (409),
`reviews.rating.out_of_range`, `reviews.feedback.comment_too_long`.

## Questionnaires de satisfaction

### Le questionnaire actif : `GET /surveys/active` (ou `GET /surveys/{surveyId}`)

```json
{
  "id": "0199…", "title": "Votre avis compte", "isOpen": true,
  "questions": [
    {"id": "0199…", "order": 1, "text": "Note globale", "type": "Rating", "isRequired": true, "choices": []},
    {"id": "0199…", "order": 2, "text": "Qu'avez-vous préféré ?", "type": "MultipleChoice", "isRequired": false, "choices": ["Plats", "Service", "Cadre"]},
    {"id": "0199…", "order": 3, "text": "Un commentaire ?", "type": "FreeText", "isRequired": false, "choices": []}
  ]
}
```

`404 storefront.survey.not_found` : aucun questionnaire en cours.

### Répondre : `POST /surveys/{surveyId}/responses`

```json
{
  "answers": [
    {"questionId": "0199…", "rating": 5},
    {"questionId": "0199…", "choices": ["Plats", "Service"]},
    {"questionId": "0199…", "text": "Très bon accueil"}
  ],
  "orderToken": "pZ8xQ…",
  "locationId": null
}
```

- `Rating` : `rating` de 1 à 5 ; `FreeText` : `text` (1 000 caractères) ; `SingleChoice` : un seul élément de
  `choices` ; `MultipleChoice` : un ou plusieurs.
- `orderToken` (facultatif) : le jeton de suivi d'une commande **terminée** ; une seule réponse par commande. Sans
  commande, `locationId` rattache la réponse à un établissement servi par l'application.
- Réponse `204`. Les réponses sont anonymes.

Refus : `storefront.survey.order_not_completed`, `reviews.survey_response.already_answered` (409),
`reviews.survey_response.not_open` (409), `reviews.survey_response.required_missing`,
`reviews.survey_response.rating_out_of_range`, `reviews.survey_response.choice_unknown`.
