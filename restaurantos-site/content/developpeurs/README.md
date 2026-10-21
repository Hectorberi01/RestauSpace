# Documentation développeurs

> Tout ce qu'il faut à un développeur pour construire le site, l'application mobile ou la borne d'un restaurant
> sur RestauSpace, et pour relier ses propres outils (caisse, comptabilité, tableau de bord) au restaurant.

## Deux API, deux usages

| | API de vente en ligne (« Storefront ») | API d'intégration |
|---|---|---|
| Pour quoi | Une interface **client** : site de commande, application mobile en marque blanche, borne, commande à table | Un outil **interne** du restaurant : logiciel de caisse, comptabilité, tableau de bord, application du personnel, automatisations |
| Qui l'appelle | Le navigateur, l'application ou la borne du client final | Un **serveur** (jamais un navigateur ni une application publiée) |
| Clé | Clé publique `pk_live_…` d'une *application de vente*, en-tête `X-Storefront-Key` | Clé secrète `srk_…`, en-tête `X-Api-Key` |
| Où la créer | Console › Paramètres › Vente en ligne | Console › Intégrations › Clés d'API |
| Adresse | `{{API_URL}}/v1/storefront/…` | `{{API_URL}}/v1/…` |
| Événements | Suivi d'une commande par interrogation | Webhooks signés (Console › Intégrations › Webhooks) |
| Description OpenAPI | `{{API_URL}}/openapi/storefront.json` (publique) | Fournie sur demande |

Dans les exemples, `{{API_URL}}` désigne l'adresse de l'API de la plateforme ; elle est donnée dans la console, à côté
de la clé.

## Sommaire

1. [Prise en main](01-prise-en-main.md) : créer une clé, choisir le type d'application, premier appel, tests, mise en production.
2. [Conventions communes](02-conventions.md) : JSON, montants, dates, erreurs, idempotence, limites de débit, cache.
3. [Construire le site d'un restaurant](14-guide-site-restaurant.md) : les pages attendues et leurs routes,
   plan d'accès, horaires, avis, référencement, conformité, liste de contrôle.

**API de vente en ligne**

4. [Restaurant, établissements et carte](03-vente-catalogue.md)
5. [Panier, créneaux et commande](04-vente-commande.md)
6. [Paiement en ligne](05-vente-paiement.md)
7. [Comptes clients et fidélité](06-vente-comptes-clients.md)
8. [Réservations, avis et questionnaires](07-vente-reservations-questionnaires.md)
9. [Borne de commande](08-vente-borne.md)

**API d'intégration**

10. [Clés d'API et autorisations](09-integrations-cles.md)
11. [Ressources du restaurant](10-integrations-ressources.md)
12. [Webhooks](11-webhooks.md)

**Référence**

13. [Codes d'erreur](12-reference-erreurs.md)
14. [Clients prêts à l'emploi](13-exemples-clients.md) : TypeScript, Flutter (Dart), Swift, Kotlin.

## Les règles à retenir

- **Le restaurant vient toujours de la clé.** Aucune requête ne choisit le restaurant : il est impossible de lire ou
  d'écrire chez un autre restaurant, même en changeant un identifiant.
- **Une clé `srk_` ne quitte jamais un serveur.** Elle ne s'écrit ni dans une application mobile, ni dans le code d'un
  site, ni dans un dépôt Git. Une clé `pk_live_` est publique par nature, mais elle ne marche que pour son application,
  ses établissements et, depuis un navigateur, ses adresses déclarées.
- **Le serveur calcule les prix.** L'interface affiche le devis du serveur ; elle n'additionne jamais elle-même.
- **Le `code` d'une erreur est le contrat.** Le texte `detail` est en anglais et peut changer ; traduisez par `code`.
- **Tout `POST` de commande, de paiement ou de réservation porte un `Idempotency-Key`.** Un nouvel envoi après une
  coupure réseau ne crée jamais de doublon.
- **Les données personnelles restent chez vous.** Ne les écrivez pas dans vos journaux ; les webhooks n'en contiennent
  aucune.
