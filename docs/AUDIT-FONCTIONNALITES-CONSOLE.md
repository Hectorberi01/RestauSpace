# Audit des fonctionnalités manquantes — console restaurateur

Date : 13 octobre 2026. Périmètre : `restaurantos-app` et le backend qui la sert.

Deux références :

- **Projet** : la documentation (`docs/*.md`, README des modules) et les routes du backend, comparées au code actuel de la console.
- **Marché** : Toast, Square, Lightspeed, Zelty, Innovorder, Popina, Tiller/SumUp, Sunday et Deliverect, plus des acteurs africains (Restafy au Bénin, Orda au Nigeria), et les obligations du Bénin et de l'UEMOA.

Les audits du 11 octobre (`AUDIT-RESTAURATEUR.md`, `AUDIT-ROUTES-RESTAURATEUR.md`) sont presque entièrement soldés. Les dix modules sans écran en ont maintenant un. Sur 396 routes du backend, il n'en reste que quelques-unes de non branchées, sans compter les lectures de détail.

Priorités :

- **P0** : bloquant (parcours cassé, ou vente impossible à un restaurant déclaré au Bénin) ;
- **P1** : attendu pour être compétitif ;
- **P2** : différenciant.

---

## 1. P0 — bloquant

### 1.1 Liens des e-mails de compte : aucune page d'arrivée

Identity envoie des liens d'invitation, de réinitialisation et de vérification d'adresse (`appsettings.Development.json:12-14` → `localhost:3000/accept-invitation|reset-password|verify-email`). Aucune des trois applications n'a ces pages, et aucune n'appelle `forgot-password`, `reset-password`, `accept-invitation`, `verify-email` ou `resend-verification`.

Conséquences :

- **un employé invité sans compte ne peut pas l'activer**, alors que la page de connexion lui dit d'ouvrir le lien (`connexion/page.tsx:54`) ;
- il n'y a pas de « Mot de passe oublié » ;
- le changement d'e-mail dans Mon profil (`PUT /me/email`) n'aboutit jamais.

À faire :

- dans la console, les pages `/activer`, `/mot-de-passe-oublie`, `/reinitialiser` et `/verifier-email` ;
- un lien « Mot de passe oublié » sur la page de connexion ;
- des URL d'e-mail pointant vers la console (port 3001 en dev), avec une variable de déploiement.

### 1.2 Facture normalisée e-MECeF (DGI Bénin)

Ce que dit la loi :

- **Qui** : les assujettis à la TVA depuis 2019-2020, et ceux à la TPS depuis juillet 2021, c'est-à-dire la plupart des restaurants déclarés.
- **Validité** : une facture n'est valable qu'après validation par la DGI, qui renvoie un code MECeF et un QR code.
- **Avoir** : type FA, qui reprend la référence de la facture d'origine.
- **Sanction** : une amende de 10 fois le montant de la facture (1 000 000 FCFA au minimum), puis la fermeture administrative en cas de récidive.

Ce que fait la console aujourd'hui :

- aucune trace de MECeF ni d'IFU dans le code ;
- le reçu (`commandes/[id]/recu`) ne porte aucune mention légale ;
- le restaurant (`TenantDto`) n'a que son nom, son logo et sa couleur.

À faire :

- **Backend**
  - un module « Fiscalité » par pays, derrière un port (`IFiscalInvoicing`) : e-MECeF au Bénin, FNE plus tard en Côte d'Ivoire ;
  - IFU, RCCM et jeton DGI dans les réglages du restaurant ; le jeton va au coffre, il n'est jamais renvoyé ni journalisé ;
  - à chaque vente terminée, une facture FV ; à chaque remboursement, une facture d'avoir FA ;
  - une file de reprise si la DGI ne répond pas, avec une facture « en attente de normalisation ».
- **Console**
  - les champs légaux dans Paramètres ;
  - le code MECeF et le QR code sur le reçu ;
  - un écran des factures en échec avec relance.

### 1.3 Session de caisse et clôture de journée

Il n'existe ni fond de caisse, ni comptage, ni écart, ni ticket Z. Le backend non plus : `GET /payments` exige un `orderId` (`PaymentsEndpoints.cs:56`) et ne sait pas lister les paiements d'une journée.

Dans un marché où l'on paie surtout en espèces, c'est le premier outil de contrôle du gérant.

À faire :

- **Backend**
  - un agrégat `CashSession` par établissement et par poste : ouverture avec un fond, entrées et sorties d'espèces, fermeture avec un comptage par moyen de paiement (espèces avec billets et pièces en FCFA, Mobile Money, carte), puis l'écart ;
  - une route de liste des paiements par période ;
  - le ticket Z.
- **Console** : une page Caisse avec l'ouverture, les mouvements, la clôture et l'historique des sessions et de leurs écarts.

### 1.4 Impression : bon cuisine et imprimante Bluetooth / USB

Aujourd'hui :

- `PrintJobKind` ne connaît que `CustomerTicket` (bornes) et `TestPage` (`PrintJob.cs:20-24`) : pas de bon cuisine, pas de reçu d'une commande prise au comptoir ;
- le reçu de la console passe par `window.print()`.

Pourquoi c'est bloquant : beaucoup de cuisines n'auront pas d'écran, et le matériel le plus répandu au Bénin est l'imprimante thermique Bluetooth de 58 ou 80 mm, reliée à un téléphone Android.

À faire :

- les types `KitchenTicket` et `OrderReceipt` dans Printing ;
- l'impression automatique du bon à l'acceptation de la commande (pont Orders → Printing) ;
- l'impression ESC/POS par Web Bluetooth ou Web Serial depuis la console sous Chrome Android, ou un petit relais Android.

### 1.5 Fonctionnement hors ligne

Il n'y a ni service worker, ni manifeste, ni gestion de `navigator.onLine` (`public/` ne contient que `favicon.svg`). Les coupures de réseau et d'électricité sont fréquentes, et c'est l'argument principal du concurrent local (Restafy, « offline-first »).

À faire, par étapes :

1. une application installable (manifeste, service worker, cache de l'interface et de la carte) ;
2. la prise de commande et l'encaissement en espèces hors ligne, mis en file locale (IndexedDB) puis envoyés avec la clé d'idempotence déjà exigée par Orders ;
3. une numérotation provisoire, puis le numéro définitif à la synchronisation ;
4. un indicateur de connexion et la liste des envois en attente.

---

## 2. P1 — attendu pour être compétitif

### 2.1 Caisse et service

| Manque | État | À faire |
|---|---|---|
| Partage d'addition par article ou en N parts | On encaisse par montants seulement (`commandes/[id]/payments.tsx`) | Répartition par article ou par convive, et parts égales |
| Interface de caisse tactile | La prise de commande existe, mais dans un écran de back-office | Mode « Caisse » plein écran : tuiles de produits par catégorie, panier, encaissement en 3 touches |
| Code PIN pour changer de serveur | Absent | PIN par membre sur une caisse partagée, verrouillage automatique |
| Transfert ou fusion de tables | Absent | Déplacer une session ou fusionner deux additions |
| Commande et paiement à table par QR | Le QR n'ouvre qu'un menu en lecture seule (`salle/qr/page.tsx:15`) ; le canal `QrCode` d'Orders n'est jamais produit ; le nom de la table est dans l'URL en clair | QR signé par table (jeton, pas de nom), panier vers la session de table, paiement Mobile Money |
| Modifier les options d'une ligne | Il faut retirer la ligne puis la rajouter (`Orders/README.md:321`) | Route de modification de ligne en brouillon |

### 2.2 Commandes et encaissements

| Manque | État | À faire |
|---|---|---|
| Recherche et filtres de l'historique | `GET /orders` ne filtre que par établissement, statut et session ; la recherche par numéro parcourt les 500 dernières commandes ; le filtre de type s'applique côté navigateur, sur la page chargée (`commandes/page.tsx:205-214, 307`) | Filtres serveur : dates, numéro, type, canal, client, montant |
| Journal des encaissements | Pas de page dédiée | Page Encaissements avec filtres et export (dépend de 1.3) |
| Reçu envoyé au client | Impression seulement | Envoi par WhatsApp, SMS ou e-mail ; PDF |
| Paiement à la livraison | Pas de suivi de l'argent détenu par le livreur | Montant à encaisser par course, remise en fin de journée, écart |

### 2.3 Livraison

| Manque | État | À faire |
|---|---|---|
| Fiche livreur | Nom et téléphone saisis à la main (`assignCourier`) | Livreurs enregistrés (internes, zémidjans indépendants), tarif par course, règlement du jour, lien de suivi WhatsApp |

### 2.4 Stock et achats

| Manque | État | À faire |
|---|---|---|
| Coût matière et marge par plat | Aucun coût unitaire (`lib/inventory.ts`) | Coût à la réception (coût moyen pondéré), coût de recette, marge par produit dans Menu et Rapports |
| Fournisseurs et bons de commande | Le fournisseur n'est qu'un texte « Référence » | Fiches fournisseurs, bons de commande, réception rapprochée du bon, suggestion d'après les seuils |
| Sortie manuelle (repas du personnel, etc.) | La route `POST /inventory/items/{id}/consumptions` existe, sans écran | Bouton « Sortie » sur l'article |

### 2.5 Clients, marketing et avis

| Manque | État | À faire |
|---|---|---|
| Campagnes groupées | Modèles `marketing.*` prévus, mais envoi client par client (`messages/send.tsx`) ; pas d'envoi groupé côté backend | Segments (tags, dernière visite, points), envoi groupé WhatsApp/SMS/e-mail dans la limite des consentements, règles de fréquence (`NOTIFICATIONS-CONFORMITE.md` §21) |
| Réponses aux questionnaires | Pas d'agrégat `SurveyResponse` (`Reviews/README.md:194`) ; on publie des questionnaires que personne ne peut remplir | Collecte publique par lien ou QR, sollicitation après une commande terminée, résultats dans Avis |
| Notes, doublons et RGPD dans le CRM | Pas de notes, pas de fusion, pas d'export du droit d'accès, pas d'import (`Customers/README.md:167`) | Notes horodatées, fusion de fiches, export JSON/CSV d'un client, import CSV |
| Commandes reçues par WhatsApp | Absent | Saisie rapide depuis un message, numéro reconnu, rattachement au client |

### 2.6 Réservations

Les clients ne peuvent pas réserver en ligne : il n'y a aucune route publique (`Reservations/README.md:181-183`) ni de page sur le site.

À faire :

- des routes Storefront : disponibilités, création, annulation par lien signé ;
- une page Réserver sur le site ;
- la confirmation par WhatsApp ou SMS.

### 2.7 Personnel

| Manque | À faire |
|---|---|
| Renvoyer une invitation | Route et bouton (dépend de 1.1) |
| Supprimer un rôle | Seul le retrait d'une permission existe |
| Pointage | Arrivée et départ par PIN sur la caisse, export des heures |

### 2.8 Rapports

| Manque | État | À faire |
|---|---|---|
| Ventes par produit, catégories, carte de chaleur | Calculées sur un **échantillon** de 40 commandes (`rapports/page.tsx:117-125`), et pour un seul établissement à la fois | Lire `sales.items_sold` et `sales.product_revenue`, comme le tableau de bord ; ajouter une mesure horaire |
| Rapprochement des encaissements | Seulement un rapport par moyen de paiement | Comparer le relevé des agrégateurs (FedaPay, KKiaPay, CinetPay) avec les paiements, et la caisse avec le comptage |

### 2.9 Abonnement

- Pas de paiement en ligne de l'abonnement (« règlement enregistré par l'équipe », `abonnement/page.tsx:321`).
- Pas de reçu ni de facture à télécharger : le backend n'a pas de route restaurant pour cela.

À faire : régler l'abonnement par Mobile Money via un agrégateur, et produire les factures de la plateforme (qui devront elles aussi être normalisées, voir 1.2).

---

## 3. P2 — différenciant

- **Menu**
  - prix par établissement ou par canal (non modélisé, `Catalog/README.md:214`) ;
  - cartes par créneau (midi, soir, happy hour) ;
  - miniatures des photos : elles sont servies telles qu'envoyées, jusqu'à 5 Mo (`VENTE-EN-LIGNE.md:156`, `Files/README.md:221`), ce qui pèse lourd sur les données mobiles.
- **Salle**
  - plan dessiné (positions, formes et rotation des tables) ;
  - envoi en cuisine par service (entrée, plat, dessert) ;
  - renommage d'une table, suppression d'une salle.
- **Cuisine** : écran d'appel des clients (« commande prête »).
- **Paramètres**
  - envoi du logo en fichier par Files, au lieu d'une URL saisie (`parametres/restaurant.tsx:98`) ;
  - Turnstile activable par application (`API-COMMANDE-DECISIONS.md` T14, non fait).
- **Sécurité** : 2FA (TOTP) pour les propriétaires et les gérants (non modélisée dans Identity).
- **Tableau de bord**
  - alerte de stock bas : aujourd'hui seulement dans la cloche ;
  - mise en avant des avis négatifs.
- **Notifications du personnel** : préférences par type d'alerte (aujourd'hui par finalité et par canal).
- **Paiement** : moyens disponibles selon la commande (`PAYMENTS-CONFORMITE.md` §7) ; Wave en direct si l'activité s'étend au Sénégal ou en Côte d'Ivoire.
- **Exports** : PDF et Excel en plus du CSV ; écritures comptables.
- **Plateformes de livraison** : Glovo, Gozem et autres (P1 en cas d'extension en Côte d'Ivoire).
- **Fidélité** : cartes cadeaux et avoirs.
- **Langues** : anglais (Nigeria, Ghana) ; fon de préférence en pictogrammes ou en audio.
- **Reprise manuelle** après un échec de pont : relancer le déstockage d'une commande, recréer un ticket cuisine (les routes existent).
- **Allergènes** : lire la liste par `GET /catalog/allergens` au lieu de la liste en dur (`lib/menu.ts:75`).

---

## 4. Documentation à corriger

- `restaurantos-app/README.md`, « Pas encore couvert » : tout y est fait.
- `AUDIT-RESTAURATEUR.md` §1, §3 et §4 : soldés (encaissement, paramètres, pages des 10 modules, établissements complets, client, promotion et récompense sur une commande, rapports planifiés, frais de livraison).
- `MODULES.md` §5 et `Catalog/README.md:211-214` : les allergènes sont exposés.
- `Inventory/README.md:186` : l'alerte de seuil existe (`staff.low_stock`).

---

## 5. Ordre conseillé

1. **Pages de compte** (1.1) : petit chantier, débloque l'invitation du personnel.
2. **Session de caisse** et liste des paiements par période (1.3).
3. **Bon cuisine imprimé et impression Bluetooth** (1.4).
4. **e-MECeF** (1.2) : chantier le plus lourd ; commencer par l'IFU et les mentions sur le reçu, puis l'appel à la DGI en environnement de test.
5. **Application installable**, puis commandes et espèces hors ligne (1.5).
6. **Mode caisse tactile**, PIN, partage d'addition, filtres de l'historique (2.1, 2.2).
7. **Coût matière et fournisseurs**, puis **campagnes groupées**, **QR de table**, **réservation en ligne**, **livreurs**.

Sources web (marché et fiscalité) :
- [gouv.bj : factures normalisées](https://www.gouv.bj/article/522/tout-savoir-sur-les-factures-normalisees-au-benin)
- [Edicom : facture normalisée au Bénin](https://edicomgroup.com/blog/electronic-invoicing-benin-facture-normalisee)
- [Square for Restaurants](https://squareup.com/us/en/point-of-sale/restaurants/features)
- [Lightspeed Restaurant](https://www.lightspeedhq.com/pos/restaurant/features/)
- [Zelty](https://tool-advisor.fr/logiciel-de-caisse/comparatif/zelty/)
- [Innovorder](https://tool-advisor.fr/logiciel-de-caisse/comparatif/innovorder/)
- [Restafy (Cotonou)](https://vc4a.com/ventures/restafy/?lang=fr)
- [Orda](https://techcabal.com/2023/12/05/heroes-of-the-african-food-industry-how-orda-is-helping-restaurants-grow/)

---

## 6. Avancement (13 octobre 2026) : P0 réalisé

### 6.1 Pages de compte

Pages publiques de la console : `/activer` (invitation), `/mot-de-passe-oublie`, `/reinitialiser` et `/verifier-email` ; lien « Mot de passe oublié ? » sur la connexion.

- Le jeton du lien est lu une fois puis retiré de l'adresse (historique, en-tête Referer).
- Les liens des e-mails pointent vers la console (`Identity:Emails:Links`, port 3001 en développement).
- **En production**, renseigner les trois URL de la console.

### 6.2 Caisse

**Backend (Payments)**
- Agrégat `CashSession` : ouverture avec un fond, entrées et sorties d'espèces avec motif, puis clôture.
- La clôture se fait par comptage, en montant ou billet par billet (FCFA). Les moyens remis en main propre sont comptés : espèces, terminal, mobile money reçu, titres.
- Ticket Z figé : attendu, compté, écart par moyen.
- Une seule session ouverte par établissement : verrou consultatif et index unique partiel. Numéros de session par établissement.
- Journal des encaissements par période (`GET /payments/journal`, 93 jours au plus), lu dans les paiements (captures et remboursements réussis).
- Routes `/payments/cash-sessions` (`payments.collect`).

**Console** : page **Caisse** (session en cours, tickets Z, journal avec export CSV), ticket Z imprimable.

**Tests** : domaine, cas d'usage, et PostgreSQL réel (jsonb, index, requêtes du journal).

### 6.3 Impression

**Backend (Printing)**
- **Bons de cuisine automatiques** à la création du ticket de cuisine (`KitchenPrintFlow`) : quantités, options, notes, urgence ; ni prix ni client.
- Une imprimante peut être réservée à un poste (bar, grill) : elle n'imprime que les lignes de ce poste.
- **Reçu d'une commande** envoyé à l'imprimante du restaurant (`POST /printing/receipts/{orderId}`, `orders.take`).
- Nouveau type de ligne **QR code** (StarPRNT, ESC/POS, texte).

**Console**
- **Imprimante de l'appareil** : impression ESC/POS en Bluetooth (BLE) ou USB (WebUSB) depuis Chrome, en 58 ou 80 mm.
- Disponible pour le reçu, le bon de cuisine (fiche commande) et le ticket Z. Les octets sont produits dans le navigateur.
- Réglage des imprimantes : rôle « bons de cuisine » et poste servi.
- `Permissions-Policy` ouvre `usb` et `bluetooth` à l'origine de la console.

### 6.4 Facture normalisée e-MECeF (nouveau module Fiscal, schéma `fiscal`)

**Profil fiscal** : IFU, RCCM, régime (TVA → groupe B, taux nul → A ; TPS → E), AIB, environnement (désactivé, bac à sable, production). Réservé aux restaurants en XOF.

**Jeton DGI**
- Chiffré en AES-256-GCM (`Fiscal:TokenKey`, le tenant servant de donnée authentifiée).
- Jamais renvoyé, journalisé ni placé dans un événement.

**Flux**
- Une commande terminée produit une facture de vente (FV).
- Un remboursement réussi produit une facture d'avoir (FA), qui attend la certification de sa facture d'origine.
- La file du module (`FiscalInvoiceWorker`) envoie puis confirme. Elle renvoie une demande expirée et reprend les erreurs passagères (1, 2, 4… minutes, 12 essais). Une facture refusée par la DGI passe en échec, relançable depuis la console.

**Reçus** (écran, imprimante de l'appareil, imprimante du restaurant) : IFU, RCCM, code MECeF/DGI, NIM, compteurs, heure et QR code.

**Console** : onglet **Paramètres › Facture normalisée** (identité, jeton, environnement, test de connexion, suivi et relance des factures). Permissions `fiscal.view` et `fiscal.configure`, données automatiquement aux rôles Owner et Manager au démarrage.

**Tests** : domaine, cas d'usage, PostgreSQL réel (coffre, index, file) et client HTTP contre un faux service e-MECeF.

### 6.5 Hors ligne

- **Application installable** : manifeste, icônes. Le service worker (en production seulement) garde l'interface et rouvre la vente rapide sans réseau.
- La console repart du dernier état connu du restaurant si l'API est injoignable.
- **Vente rapide** (`/caisse/vente`) : à emporter, en espèces, avec tuiles produits et rendu monnaie.
  - La carte est gardée sur l'appareil (IndexedDB). Chaque vente est notée localement avec un numéro provisoire (ex. `H2125-632`).
  - Au retour du réseau, chaque vente est rejouée étape par étape : création, lignes, validation, acceptation (le bon part en cuisine), encaissement. Les clés d'idempotence sont dérivées de la vente : pas de doublon.
  - Les produits à options obligatoires passent par l'écran Commandes.
- Indicateur de connexion et de ventes à envoyer dans la barre du haut.

### 6.6 À faire au déploiement

1. `./scripts/migrations-p0.sh` : migrations `CashSessions` (Payments), `KitchenTickets` (Printing), `InitialCreate` (Fiscal), et ajout des projets Fiscal à la solution.
2. Configurer `Fiscal:TokenKey` (32 octets, base64, secret) et, si besoin, `Fiscal:SandboxBaseUrl` / `Fiscal:ProductionBaseUrl`.
3. Configurer `Identity:Emails:Links` vers la console publique.
4. Servir la console en HTTPS : le service worker, Web Bluetooth et WebUSB l'exigent (sauf localhost).

### 6.7 Points e-MECeF à vérifier en bac à sable DGI

Ces points n'ont pas pu être confirmés : le site de la DGI n'était pas accessible depuis l'environnement de développement.

- URL de production (`https://sygmef.impots.bj/emcf/api/` d'après une bibliothèque tierce).
- Format de la référence d'un avoir : 24 caractères sans tirets attendus, mais un code de test commence par `TEST`.
- Sens exact des totaux renvoyés (`ta`… `taf`, `hab`, `vab`…) ; non utilisés ici, la DGI calcule.
- Règles d'application de l'AIB pour un restaurant.

Vérifier aussi le contenu imprimé du QR code (la chaîne `qrCode` est imprimée telle quelle).

---

## 7. Avancement (14 octobre 2026) : P1 réalisé

Hors périmètre de ce lot : commandes et livraison (2.2 et 2.3).

### 7.1 Caisse tactile, PIN, addition et tables, QR de table

- **Caisse partagée** (Staff) : caisses déclarées par établissement ; un membre déverrouille avec son PIN (choisi dans Mon profil, effaçable par le gérant). Le bouton « Verrouiller » rend la main au membre suivant. Le pointage (arrivée, départ) se fait par le même PIN, avec l'export des heures dans Personnel › Pointages.
- **Mode caisse plein écran** : tuiles par catégorie, panier, encaissement en quelques touches.
- **Addition partagée** (Payments) : paiement par article (`items`) ou en parts égales.
- **Tables** (Floor) : déplacement et fusion de sessions de table.
- **QR de table** : jeton signé par table, sans le nom de la table dans l'adresse. Il ouvre sur le site la commande à table, rattachée à la session.

### 7.2 Coût matière, marge et fournisseurs (Inventory)

- Coût unitaire (coût moyen pondéré à la réception, saisie directe possible), coût des recettes et marge par produit, dans Stock et dans la fiche produit du Menu.
- Bouton « Sortie » sur l'article (repas du personnel, casse).
- Fiches fournisseurs, bons de commande (composition, envoi par WhatsApp ou copie, réception rapprochée, annulation), suggestions de réapprovisionnement d'après les seuils.

### 7.3 Campagnes et questionnaires

- **Campagnes** (Notifications) : audience selon les consentements marketing (Customers), envoi programmé par lots, idempotent par client. Règle de fréquence : 3 jours par défaut entre deux messages marketing, 30 au plus.
- **Questionnaires** (Reviews) : réponses publiques par lien ou QR, sollicitation sur la page de suivi d'une commande terminée, une réponse par commande, résultats dans Avis.

### 7.4 CRM et commandes WhatsApp (Customers, Orders)

- Notes horodatées par fiche, fusion de doublons, export des données d'un client, import CSV (500 lignes au plus, consentement attesté).
- La fusion reporte les commandes, avis, réservations et points de fidélité sur la fiche gardée ; la fiche fusionnée ne garde aucune donnée personnelle.
- **Commande reçue par téléphone ou WhatsApp** : numéro reconnu ou client créé, texte du message analysé en lignes. Canaux `Phone` et `WhatsApp`. Les produits à variantes ou options obligatoires sont signalés « à compléter ».

### 7.5 Réservation en ligne (Reservations, Storefront, site)

- Réglage par établissement : « Réservation en ligne » dans les règles de réservation de la console (désactivée par défaut).
- Routes publiques :
  - `GET /storefront/locations/{id}/booking?date&partySize` : heures proposées par pas de 30 minutes, dans les horaires et le délai minimum ;
  - `POST /storefront/reservations` (clé d'idempotence) ;
  - `GET` et `POST …/cancel` sur `/storefront/reservations/{jeton}` (lien signé).
- La réservation en ligne est confirmée tout de suite. Le téléphone est exigé.
- **Site** : pages `/reserver` et `/reservation`, et un lien « Réserver une table » dans l'en-tête de la carte (absent à table).

### 7.6 Personnel (Staff)

- **Renvoyer l'invitation** d'un membre invité (`POST /staff/members/{id}/invitation`). Événement `StaffInvitationResent` : Identity envoie un nouveau lien si le compte attend encore son mot de passe.
- **Supprimer un rôle** personnalisé (`DELETE /staff/roles/{id}`). C'est refusé tant qu'un membre invité, actif ou suspendu le détient (`staff.role.in_use`) ; les rôles système restent protégés. Une affectation concurrente et la suppression ne peuvent pas passer toutes les deux.

### 7.7 Rapports (Reporting)

- Produits, catégories et carte de chaleur lus dans les compteurs du reporting : **toutes** les commandes terminées, un ou tous les établissements.
- Nouvelles mesures horaires `sales.hourly_orders` et `sales.hourly_revenue`. Leur dimension est l'heure locale de l'événement, fixée à l'enregistrement.
- **Onglet Rapprochement** (`payments.collect`) :
  - **caisse** : sessions clôturées de la période, espèces comptées, écart par session et écart cumulé ;
  - **relevé de l'agrégateur** : CSV exporté de FedaPay, CinetPay, KKiaPay…, colonnes reconnues ou choisies, lignes refusées écartées par le statut ;
  - comparaison par référence avec les paiements en ligne de la période : rapprochés, montant différent, absents de nos paiements, absents du relevé ;
  - export CSV des écarts.

### 7.8 Abonnement (Billing, Payments)

**Paiement en ligne**
- « Payer en ligne » pour la période due : fin d'essai, échéance, impayé ou suspension. Ce n'est pas possible pendant une période payée, ni si une résiliation est programmée.
- L'encaissement se fait sur le compte marchand de la **plateforme**, chez le prestataire configuré.
- La page du prestataire s'ouvre dans un nouvel onglet, ou la demande arrive sur le téléphone (MTN).
- La console relit l'état toutes les 4 s. Le job d'échéance relit aussi les règlements en attente. Un règlement expire après 30 minutes sans réponse.
- Le prestataire fait foi : relecture de la transaction (FedaPay, CinetPay, MTN MoMo ; KKiaPay ne se relit pas par notre référence et n'est donc pas proposé pour les abonnements).
- Un règlement payé enregistre le paiement d'abonnement. Si la période a été réglée entre-temps, ou si le montant reçu est inférieur, il reste sans paiement : à rembourser par la plateforme (événement `billing.checkout.paid` sans `PaymentId` dans le journal d'audit).

**Factures**
- Chaque paiement d'abonnement reçoit un numéro `F{année}-{rang}` dans la transaction qui l'enregistre. La suite est sans trou, par année (table `invoice_sequences` verrouillée jusqu'au commit).
- La console ouvre la facture imprimable (ou un reçu pour les paiements antérieurs) : émetteur, client, lignes, total, mode de paiement.
- Routes :
  - `GET /billing/online-payment` ;
  - `POST /billing/checkouts` ;
  - `POST /billing/checkouts/{id}/refresh` ;
  - `GET /billing/payments/{id}/invoice`.

### 7.9 À faire au déploiement

1. `./scripts/migrations-p1.sh` génère ces migrations : `Staff TillsAndTimeClock`, `Payments PaymentCoveredItems`, `Floor TableQrTokens`, `Inventory CostsAndPurchasing`, `Notifications CampaignsAndMarketingRules`, `Reviews SurveyResponses`, `Customers NotesAndMerge`, `Reservations OnlineBooking`, `Billing OnlinePaymentAndInvoices`.
2. Abonnement en ligne, à configurer :
   - `Billing:OnlinePayment:Provider` (`fedapay`, `cinetpay` ou `mtn_momo`) et `Billing:OnlinePayment:ReturnUrl` (page Abonnement de la console) ;
   - dans le super-admin, le prestataire doit accepter le mode plateforme, avec les identifiants de la plateforme rangés au coffre.
3. Émetteur des factures : `Billing:InvoiceIssuer:Name`, `Address`, `TaxId`, `RegistrationNumber`, `Email`, `Footer` (mention de TVA).

### 7.10 Limites connues

- Réservation en ligne :
  - les heures proposées ignorent les fermetures exceptionnelles ; la réservation les revérifie et refuse ;
  - la confirmation envoyée au client ne contient pas encore le lien de gestion.
- Factures de la plateforme : pas encore normalisées (e-MECeF, voir 1.2) ; pas de TVA ventilée.
- Règlement en ligne : pas de paiement anticipé avant l'échéance (le domaine ne renouvelle qu'une période échue).
- Rapprochement :
  - limité aux 1 000 mouvements les plus récents de la période (93 jours au plus) ;
  - une transaction payée en fin de période peut figurer sur le relevé du jour suivant.

## 8. Avancement (14 octobre 2026) : P2 réalisé

### 8.1 Menu et vente

- **Allergènes** : la console lit la liste de l'API (`GET /catalog/allergens`) au lieu d'une liste en dur.
- **Prix par établissement et par canal** (Catalog) : onglet « Prix » de la fiche produit. Un prix peut viser un établissement, un canal (comptoir, site, application, borne) ou les deux ; le plus précis l'emporte. Le devis et la commande passée utilisent le même canal (QR de table compté comme le site).
- **Cartes par créneau** : jours et heures d'une carte (midi, soir, happy hour), en heure locale de l'établissement ; un créneau peut passer minuit. Sans créneau, la carte est proposée à toute heure d'ouverture.
- **Miniatures** : le navigateur réduit la photo (1 600 px) et prépare une miniature (480 px, WebP ou JPEG) ; le site et la borne affichent la miniature.

### 8.2 Salle et cuisine

- **Plan dessiné** : tables placées par glisser-déposer sur une grille, formes ronde, carrée ou rectangulaire, renommage, suppression d'une salle sans session ouverte.
- **Envoi par service** : chaque produit a un service (direct, entrée, plat, dessert). Les services suivants restent en attente sur le ticket d'une commande sur place jusqu'à « Envoyer » en cuisine.
- **Écran d'appel** (`/cuisine/appel`) : numéros en préparation et prêts, plein écran, signal sonore à chaque commande prête. Jamais le nom du client.
- **Reprise manuelle** : relancer le déstockage d'une commande et recréer son ticket cuisine depuis la commande.

### 8.3 Sécurité et site

- **Validation en deux étapes (TOTP)** dans Mon profil : QR code, 10 codes de secours (empreintes seulement), secret chiffré (AES-GCM). Connexion en deux temps sur la console et le super-admin ; un même pas de 30 s ne sert qu'une fois.
- **Logo par fichier** (Files) et **anti-robot Turnstile** activable par application de vente.

### 8.4 Pilotage

- **Tableau de bord** : alertes stock bas et avis négatifs (2 étoiles ou moins).
- **Alertes de l'équipe** : chaque membre coupe ou rétablit un type d'alerte dans Notifications.
- **Exports** des rapports en **Excel (.xlsx)**, **PDF** ou CSV, avec les noms des établissements et des mesures.
- **Export comptable** (Rapports › Vue d'ensemble) : journal CSV par jour, au format du fichier des écritures comptables :
  - ventes (VT) : clients au débit (TTC), ventes HT et TVA au crédit ;
  - trésorerie (TR) : un compte par moyen de paiement pour les encaissements, l'inverse pour les remboursements.

  Plan SYSCOHADA par défaut, modifiable et retenu sur le poste. La TVA vient de la nouvelle mesure `sales.revenue_tax` (commandes terminées).

### 8.5 Encaissement

- **Moyens selon la commande** : la caisse ne propose que les moyens permis pour l'établissement, le montant et le canal (`GET /payments/payment-methods/available`). Le site fait de même.
- **Wave** (Sénégal, Côte d'Ivoire…) : session de paiement, webhook signé (`Wave-Signature`), relecture de la session, remboursement total.
- **Cartes cadeaux et avoirs** (Loyalty, onglet Marketing › Cartes cadeaux) :
  - émission (`loyalty.manage`) : le code de 16 caractères n'est montré qu'une fois ; seule son empreinte SHA-256 et ses 4 derniers caractères sont gardés ;
  - vérification d'un solde par code ; annulation (le solde restant est perdu) ;
  - en caisse, moyen « Carte cadeau ou avoir » : la carte est débitée par son code. Le paiement garde l'id de la carte et du mouvement, jamais le code. Si l'enregistrement échoue, le débit est annulé ;
  - un remboursement recrédite la carte ; un autre paiement peut être remboursé **en avoir** (code remis au client) ;
  - non compté à la clôture de caisse.
- **Plateformes de livraison** (Glovo, Gozem, Yango, Jumia Food…) :
  - saisie dans « Commande reçue à distance » : plateforme et numéro de commande chez elle, mode Livraison par défaut ;
  - canal `DeliveryPlatform` : la même commande d'une plateforme n'est saisie qu'une fois (index unique) ;
  - un intégrateur peut aussi pousser la commande par l'API (clé d'API, `platform` et `externalReference`) ;
  - moyen « Payé à la plateforme », non compté à la clôture ;
  - chiffre d'affaires en ligne ventilé par plateforme dans les rapports.

  Glovo et Gozem ne publient pas d'API ouverte : leurs API partenaires demandent un accord. La connexion directe n'est donc pas faite.

### 8.6 Anglais

- Le site des clients est traduit en anglais : commande, borne, suivi, compte, réservation, questionnaires, désinscription. Sélecteur FR | EN dans l'en-tête ; `?lang=en` dans un lien ; choix mémorisé sur l'appareil, français par défaut.
- La langue choisie est enregistrée sur la fiche d'un nouveau client. Ses messages (codes, confirmations) partent dans cette langue si le restaurant a un modèle anglais.
- La console, le super-admin et la carte publique (`/menu`, rendue par le serveur) restent en français.

### 8.7 À faire au déploiement

1. `./scripts/migrations-p2.sh` génère ces migrations :
   - `Storefront HumanCheck`, `Notifications AlertTypePreferences`, `Floor TableLayouts`, `Kitchen Courses` ;
   - `Files Thumbnails`, `Catalog PricesAndMenuSlots`, `Identity TwoFactor` ;
   - `Loyalty StoredValueCards`, `Payments GiftCardAndPlatformMethods`, `Orders DeliveryPlatforms`.
2. Configuration :
   - `Identity:TwoFactor:SecretKey` (32 octets en base64) ;
   - `Storefront:Turnstile:SiteKey` et `Storefront:Turnstile:SecretKey` ;
   - Wave : `api_key` et `webhook_secret` saisis dans la configuration du restaurant.
3. Les moyens intégrés « Carte cadeau ou avoir » et « Payé à la plateforme de livraison » apparaissent au démarrage. Le restaurant les active dans Paramètres › Paiements.

### 8.8 Limites connues

- Une carte cadeau **vendue** s'encaisse à part, comme une vente ; elle n'apparaît pas dans le chiffre d'affaires. Pas de rechargement : on émet une nouvelle carte.
- Le plan de comptes de l'export comptable est retenu sur le poste, pas sur le compte du restaurant.
- Les exports planifiés restent en CSV.
- Le PDF utilise les polices standard : seuls les caractères d'Europe occidentale s'affichent.
