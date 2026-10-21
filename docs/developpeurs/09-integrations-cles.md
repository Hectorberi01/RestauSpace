# Clés d'API et autorisations

> Authentifier un serveur auprès de l'API d'intégration avec une clé secrète `srk_`, limitée aux droits dont il a
> besoin.

## La clé

- Format : `srk_` suivi de 43 caractères (47 au total). Elle s'affiche **une seule fois** à la création ou à la
  régénération ; la plateforme n'en garde qu'une empreinte.
- Elle vaut pour **tout le restaurant** (tous ses établissements).
- À envoyer dans l'en-tête `X-Api-Key` (pas dans `Authorization`) :

```bash
curl -s "{{API_URL}}/v1/orders?status=Placed&pageSize=20" -H "X-Api-Key: $RESTAUSPACE_API_KEY" -H "Accept: application/json"
```

```ts
// Node.js 20+ : la clé vient de l'environnement du serveur, jamais du navigateur.
const restauspace = async (path: string, init: RequestInit = {}) => {
  const response = await fetch(`{{API_URL}}/v1${path}`, {
    ...init,
    headers: {'X-Api-Key': process.env.RESTAUSPACE_API_KEY!, Accept: 'application/json', 'Content-Type': 'application/json', ...init.headers},
  });
  if (response.status === 429) throw new RetryLater(Number(response.headers.get('Retry-After') ?? 1));
  if (!response.ok) throw new ApiError(response.status, (await response.json().catch(() => null))?.code);
  return response.status === 204 ? null : response.json();
};
```

```kotlin
// Serveur JVM (Ktor, Spring…) : la clé vient d'un coffre de secrets.
val api = OkHttpClient()
fun get(path: String): String =
    api.newCall(
        Request.Builder().url("{{API_URL}}/v1$path")
            .header("X-Api-Key", System.getenv("RESTAUSPACE_API_KEY"))
            .header("Accept", "application/json")
            .build()
    ).execute().use { response ->
        check(response.isSuccessful) { "RestauSpace ${response.code}" }
        response.body!!.string()
    }
```

```dart
// Serveur Dart (shelf, dart_frog…).
Future<dynamic> restauspace(String path) async {
  final response = await http.get(Uri.parse('{{API_URL}}/v1$path'),
      headers: {'X-Api-Key': Platform.environment['RESTAUSPACE_API_KEY']!, 'Accept': 'application/json'});
  if (response.statusCode != 200) throw Exception('RestauSpace ${response.statusCode}');
  return jsonDecode(utf8.decode(response.bodyBytes));
}
```

```swift
// Serveur Swift (Vapor…).
func restauspace(_ path: String) async throws -> Data {
    var request = URLRequest(url: URL(string: "{{API_URL}}/v1\(path)")!)
    request.setValue(ProcessInfo.processInfo.environment["RESTAUSPACE_API_KEY"], forHTTPHeaderField: "X-Api-Key")
    request.setValue("application/json", forHTTPHeaderField: "Accept")
    let (data, response) = try await URLSession.shared.data(for: request)
    guard (response as? HTTPURLResponse)?.statusCode == 200 else { throw URLError(.badServerResponse) }
    return data
}
```

## Cycle de vie

| Action | Où | Effet |
|---|---|---|
| Créer | Console › Intégrations › Clés d'API | Nom, droits (scopes), expiration facultative. |
| Régénérer | Même écran | Même nom, mêmes droits ; **l'ancienne clé cesse immédiatement** de fonctionner. Déployez la nouvelle clé d'abord dans votre coffre, puis régénérez au moment du basculement. |
| Révoquer | Même écran | Définitif, effet immédiat. |
| Expirer | Automatique à la date choisie | La clé est refusée (`401`) ; elle reste affichée « Expirée ». |
| Modifier les droits | Impossible | Créez une nouvelle clé, basculez, révoquez l'ancienne. |

Une clé inutilisée depuis 90 jours est signalée « dormante » dans la console : révoquez-la si elle ne sert plus.

Une clé ne peut **ni** gérer les clés **ni** gérer les webhooks : ces réglages se font dans la console par une personne.

## Droits (scopes)

Une clé n'atteint une route que si elle porte le droit exigé par cette route. Choisissez le minimum.

| Scope | Ce qu'il permet |
|---|---|
| `orders.take` | Lire les commandes et la carte ; créer une commande, la composer, la valider ; imprimer un reçu |
| `orders.process` | Lire les commandes ; les accepter, lancer la préparation, marquer prêtes, terminer, annuler |
| `orders.discount` | Appliquer ou retirer une remise manuelle sur une commande en cours |
| `catalog.manage` | Lire et modifier la carte : produits, prix, disponibilité, compléments, menus, catégories de taxe |
| `locations.manage` | Lire et modifier les établissements : informations, horaires, fermetures |
| `floor.serve` / `floor.manage` | Lire la salle et les tables ; installer et libérer des clients / modifier le plan |
| `reservations.manage` | Lire et gérer les réservations (créer, confirmer, déplacer, installer, annuler) |
| `reservations.configure` | Lire et régler la politique de réservation d'un établissement |
| `kitchen.operate` / `kitchen.configure` | Lire et faire avancer les tickets de cuisine / régler postes et routage |
| `payments.collect` | Lire les paiements, le journal et la caisse ; encaisser (espèces, terminal) ou lancer un paiement en ligne |
| `payments.refund` | Lire les paiements ; rembourser, totalement ou en partie, ou en avoir |
| `payments.configure` | Moyens de paiement et identifiants des prestataires (**sensible**) |
| `inventory.manage` / `inventory.operate` | Lire les stocks ; gérer articles, recettes, fournisseurs / saisir les mouvements et commandes fournisseurs |
| `customers.view` | Lire et chercher les fiches clients et leurs consentements (**données personnelles**) |
| `customers.manage` | Créer, modifier, importer, fusionner, exporter les fiches clients |
| `customers.anonymize` | Effacer un client (droit à l'oubli) (**irréversible**) |
| `customers.configure` | Réglages du module clients |
| `loyalty.view` / `operate` / `manage` / `configure` | Fidélité : lire les comptes / ouvrir un compte et utiliser des points / ajuster, suspendre, clôturer / régler le programme et les récompenses |
| `promotions.view` / `apply` / `manage` | Promotions : lire et simuler / appliquer à une commande / créer et modifier |
| `reporting.view` / `manage` / `export` | Rapports : tableau de bord, exécuter, télécharger un export / gérer les rapports enregistrés et leurs envois planifiés / demander un export |
| `delivery.operate` / `delivery.configure` | Livraisons et retraits / réglages et zones de livraison |
| `reviews.view` / `submit` / `manage` | Avis : lire / enregistrer l'avis d'un client / traiter les avis et questionnaires |
| `notifications.view` / `send` / `configure` / `manage_preferences` | Historique / envoyer / prestataires et modèles (**sensible**) / préférences des clients |
| `files.view` / `upload` / `manage` | Fichiers : lire / déposer / supprimer |
| `printing.view` / `printing.manage` | Imprimantes et travaux d'impression / déclarer, tester, réimprimer |
| `fiscal.view` / `fiscal.configure` | Facture normalisée : profil et factures / IFU, jeton e-MECeF, mode |

Jamais disponibles pour une clé : la gestion du personnel et des rôles, des intégrations, de l'abonnement, du journal
d'audit, des réglages du restaurant et de la vente en ligne.

### Lectures

Les routes de lecture (`GET`) d'un module s'ouvrent avec n'importe lequel des scopes du module listés ci-dessous :

| Lectures | Scopes qui les ouvrent |
|---|---|
| `/v1/orders/**` | `orders.take`, `orders.process` |
| `/v1/catalog/**` | `catalog.manage`, `orders.take` |
| `/v1/locations/**` | `locations.manage`, `catalog.manage`, `orders.take` |
| `/v1/reservations/**` | `reservations.manage`, `reservations.configure` |
| `/v1/kitchen/**` | `kitchen.operate`, `kitchen.configure` |
| `/v1/floor/**` | `floor.serve`, `floor.manage` |
| `/v1/inventory/**` | `inventory.manage`, `inventory.operate` |
| `/v1/payments/**` (sauf `/v1/payments/configurations`) | `payments.collect`, `payments.refund` |
| `/v1/payments/configurations/**` | `payments.configure` seulement |

Les autres modules (clients, fidélité, promotions, rapports, livraison, avis, fichiers, impression, facture
normalisée…) déclarent un scope sur chaque route, lectures comprises.

Une route sans scope adapté répond `403` (sans corps). Les routes du compte personnel (`/v1/me`), de la plateforme et
de la console de gestion des intégrations sont toujours fermées aux clés.

## Limite de débit

600 requêtes par minute et par clé (fenêtre glissante), puis `429` avec `Retry-After`. Pour une synchronisation, préférez
les [webhooks](11-webhooks.md) à l'interrogation répétée.

## Bonnes pratiques

- Une clé par usage (une pour la comptabilité, une pour le tableau de bord), chacune avec ses seuls droits.
- Une date d'expiration (6 à 12 mois) et un rappel pour la régénérer.
- Jamais dans le code source, un dépôt, une application mobile, un site ou un journal.
- En cas de fuite : **révoquez d'abord**, puis créez-en une nouvelle.
