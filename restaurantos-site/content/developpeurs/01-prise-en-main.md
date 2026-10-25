# Prise en main

> Créer la bonne clé, faire un premier appel, tester sans argent réel, puis passer en production.

## 1. Choisir l'API

- Vous construisez ce que **le client du restaurant** utilise (site de commande, application, borne, menu à table) :
  API de vente en ligne, clé publique `pk_live_`.
- Vous construisez ce que **le restaurant** utilise en interne, ou vous reliez un logiciel (caisse, comptabilité,
  ERP, tableau de bord) : API d'intégration, clé secrète `srk_`, appelée depuis votre serveur.

Une même solution utilise souvent les deux : une application mobile client (Storefront) et un petit serveur qui reçoit
les webhooks et alimente un tableau de bord (Intégrations).

## 2. Créer une application de vente (clé publique)

Le restaurateur, ou un membre du personnel qui a le droit « Gérer la vente en ligne », ouvre
**Console › Paramètres › Vente en ligne › Applications** puis **Nouvelle application**.

| Type | Pour | Ce qui change |
|---|---|---|
| Site web | Un site de commande, le site du restaurant ou d'une agence | Les adresses du site (`https://chezawa.bj`) sont obligatoires pour un appel depuis un navigateur : la clé est refusée depuis toute autre adresse. |
| Application mobile | Une application iOS / Android à la marque du restaurant | Aucune adresse pour une application native. Si l'application tourne dans une WebView (Capacitor, Ionic, Flutter web), déclarez son origine, par exemple `capacitor://localhost`. |
| Borne | Une borne de commande en libre-service | La borne doit être appairée à un établissement (voir [Borne](08-vente-borne.md)). Si c'est une page web, déclarez son adresse. |

Réglages de chaque application :

- **Établissements servis** : tous, ou une liste. Une application ne voit et ne commande que ses établissements.
- **Pages de retour après paiement en ligne** : les adresses `https` vers lesquelles le prestataire de paiement
  renvoie le client (comparées sans la partie `?…`).
- **Vérification anti-robot** (sites et applications) : si elle est cochée, les commandes, réservations et demandes de
  code de connexion exigent un jeton Cloudflare Turnstile (en-tête `X-Human-Check`). Voir
  [Conventions](02-conventions.md#vérification-anti-robot).

La clé `pk_live_…` (40 caractères) s'affiche **une seule fois**. Elle peut être régénérée : l'ancienne cesse aussitôt de
fonctionner.

Le canal dépend de l'abonnement du restaurant : le site web, l'application et la borne sont des options,
sur tous les plans. Sans ce droit, la carte reste lisible
mais `orderingEnabled` vaut `false` et une commande est refusée (`storefront.checkout.unavailable`).

## 3. Créer une clé d'API (clé secrète)

Les clés d'API sont incluses dans les plans Pro et Multi-sites, et en option (« API et webhooks ») sur Essentiel.

**Console › Intégrations › Clés d'API › Nouvelle clé** (droit « Gérer les intégrations »). Choisissez un nom, une date
d'expiration facultative et **le moins de droits possible** (voir [Clés d'API](09-integrations-cles.md)). La clé
`srk_…` s'affiche une seule fois ; rangez-la dans le gestionnaire de secrets de votre serveur.

## 4. Premier appel

```bash
curl -s {{API_URL}}/v1/storefront/ \
  -H "X-Storefront-Key: pk_live_XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX" \
  -H "Accept: application/json"
```

```ts
const API = '{{API_URL}}/v1/storefront';
const response = await fetch(`${API}/`, {
  headers: {'X-Storefront-Key': process.env.NEXT_PUBLIC_STOREFRONT_KEY!, Accept: 'application/json'},
});
const storefront = await response.json();
console.log(storefront.restaurant.name, storefront.locations.length);
```

```dart
import 'dart:convert';
import 'package:http/http.dart' as http;

const api = '{{API_URL}}/v1/storefront';
const key = String.fromEnvironment('STOREFRONT_KEY');

Future<void> main() async {
  final response = await http.get(Uri.parse('$api/'), headers: {'X-Storefront-Key': key, 'Accept': 'application/json'});
  final storefront = jsonDecode(utf8.decode(response.bodyBytes)) as Map<String, dynamic>;
  print(storefront['restaurant']['name']);
}
```

```swift
import Foundation

let api = URL(string: "{{API_URL}}/v1/storefront/")!
var request = URLRequest(url: api)
request.setValue(storefrontKey, forHTTPHeaderField: "X-Storefront-Key")
request.setValue("application/json", forHTTPHeaderField: "Accept")
let (data, _) = try await URLSession.shared.data(for: request)
let storefront = try JSONSerialization.jsonObject(with: data) as? [String: Any]
```

```kotlin
val client = OkHttpClient()
val request = Request.Builder()
    .url("{{API_URL}}/v1/storefront/")
    .header("X-Storefront-Key", BuildConfig.STOREFRONT_KEY)
    .header("Accept", "application/json")
    .build()
client.newCall(request).execute().use { response ->
    val storefront = JSONObject(response.body!!.string())
    println(storefront.getJSONObject("restaurant").getString("name"))
}
```

Réponse (abrégée) :

```json
{
  "restaurant": {"name": "Chez Awa", "logoUrl": null, "primaryColor": "#5b3df5", "currencyCode": "XOF", "pricesIncludeTax": true},
  "app": {"id": "0199a3c4-…", "name": "Site chezawa.bj", "kind": "Web", "humanCheckSiteKey": null},
  "orderingEnabled": true,
  "locations": [{"id": "0199a3c4-…", "name": "Chez Awa Cotonou", "acceptsOrdersNow": true, "serviceModes": ["Takeaway", "Delivery"], "…": "…"}]
}
```

Avec une clé d'intégration :

```bash
curl -s "{{API_URL}}/v1/orders?pageSize=5" -H "X-Api-Key: srk_XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX"
```

## 5. Tester sans argent réel

Il n'existe pas de clé « de test » : on teste avec un **restaurant de démonstration**, demandé au support, configuré
avec le prestataire de paiement `sandbox`. Tout y est réel (commandes en cuisine, tickets, webhooks) sauf l'argent.
Créez une application de vente et une clé d'API dédiées à vos tests, puis désactivez-les ou révoquez-les à la fin.

En développement local, déclarez l'adresse de votre serveur de développement parmi les adresses de l'application :
`http://localhost:<port>` et `http://127.0.0.1:<port>` sont acceptées (le `http` n'est permis que pour ces adresses
locales).

## 6. Mise en production : liste de contrôle

- [ ] La clé `pk_live_` de production est dans la configuration de l'application, pas dans le code source partagé.
- [ ] Site web : toutes les adresses du site (avec et sans `www`) sont déclarées.
- [ ] Les pages de retour après paiement sont déclarées et répondent en `https`.
- [ ] Chaque `POST /orders`, `/orders/{jeton}/payments` et `/reservations` envoie un `Idempotency-Key` unique (UUID),
      réutilisé tel quel pour un nouvel essai du même envoi.
- [ ] Le total affiché vient de `POST /cart/quote`, et la commande envoie `expectedTotal`.
- [ ] Toutes les erreurs sont traduites par leur `code` (voir [Codes d'erreur](12-reference-erreurs.md)), avec un
      message générique pour un code inconnu.
- [ ] `429` : l'interface patiente le nombre de secondes de `Retry-After`.
- [ ] Les jetons (suivi de commande, session client, appareil de borne) sont rangés comme des secrets de l'appareil
      (Keychain, Keystore, `localStorage` à défaut).
- [ ] La clé `srk_` n'est que sur votre serveur ; elle a une date d'expiration et seulement les droits nécessaires.
- [ ] Le récepteur de webhooks vérifie la signature et ignore un `webhook-id` déjà traité.
- [ ] Aucune donnée personnelle (nom, téléphone, e-mail, code reçu) n'est écrite dans vos journaux.
