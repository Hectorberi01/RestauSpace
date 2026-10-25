#!/usr/bin/env python3
"""Crée ou remet d'aplomb le restaurant de démonstration « Chez Awa », par l'API uniquement (jamais de SQL).

Il alimente la démo publique des modèles de sites (https://demo.restauspace.com/maquis/).
Rejouable : chaque étape compare l'existant à l'attendu, crée ce qui manque (+), garde ce qui est juste (=)
et aligne ce qui diffère (~). Rien n'est supprimé.

Étapes :
  1. compte propriétaire (inscription en libre-service) et restaurant (XOF, Africa/Porto-Novo, Bénin, profil public) ;
  2. plan Pro mensuel XOF offert sans limite de durée (compte administrateur) ;
  3. établissement « Chez Awa — Akpakpa », horaires et téléphone factice ;
  4. carte : taxe, options, articles, menu publié, poste de cuisine ;
  5. vente en ligne : retrait, livraison (zone Cotonou), réglages, application Web « Démo modèles » ;
  6. paiement simulé « sandbox » (catalogue de la plateforme, puis configuration du restaurant) ;
  7. réservations en ligne, sans acompte ;
  8. avis : rien (pas de faux avis) ;
  9. résumé.

Usage :
    DEMO_OWNER_EMAIL=... python3 scripts/seed-demo.py --admin-email admin@example.com
    python3 scripts/seed-demo.py --skip-admin            # sans les étapes de la plateforme
    python3 scripts/seed-demo.py --dry-run               # lit et affiche, n'écrit rien
    python3 scripts/seed-demo.py --rotate-key            # nouvelle clé publique pour l'application de démo

Mots de passe : DEMO_OWNER_PASSWORD et SAASRESTAURANT_ADMIN_PASSWORD, sinon demandés au clavier (jamais affichés).
"""

from __future__ import annotations

import argparse
import getpass
import json
import os
import secrets
import sys
import urllib.error
import urllib.parse
import urllib.request
import uuid

RESTAURANT = {
    "name": "Chez Awa",
    "country": "BJ",
    "currency": "XOF",
    "timeZone": "Africa/Porto-Novo",
    "primaryColor": "#B4441A",
    "tagline": "Maquis · Akpakpa, Cotonou",
    "description": (
        "Maquis familial d'Akpakpa : grillades au feu de bois, plats béninois et jus maison. "
        "Restaurant de démonstration RestauSpace : les commandes ne sont ni préparées ni livrées."
    ),
}

LOCATION = {
    "name": "Chez Awa — Akpakpa",
    "address": {"street": "Rue de démonstration (adresse fictive), Akpakpa", "city": "Cotonou", "postalCode": None, "countryCode": "BJ"},
    # Numéro factice : uniquement des zéros après l'indicatif du Bénin.
    "phone": "+2290100000000",
    "seats": 60,
    "hours": [
        ("Monday", "11:00", "22:00"), ("Tuesday", "11:00", "22:00"), ("Wednesday", "11:00", "22:00"),
        ("Thursday", "11:00", "22:00"), ("Friday", "11:00", "23:30"), ("Saturday", "11:00", "23:30"),
        ("Sunday", "12:00", "21:00"),
    ],
}

TAX = {"code": "TVA18", "name": "TVA 18 %", "rate": 0.18}

OPTION_GROUPS = [
    {"name": "Piment", "min": 1, "max": 1, "options": [("Doux", 0), ("Moyen", 0), ("Fort", 0)]},
    {"name": "Accompagnement", "min": 1, "max": 1, "options": [("Alloco", 0), ("Attiéké", 0), ("Riz", 0)]},
]

MENU_NAME = "Carte"
SECTIONS = ["Plats", "Grillades", "Accompagnements", "Boissons"]

PRODUCTS = [
    {"section": "Grillades", "name": "Poulet bicyclette braisé", "price": 4500, "groups": ["Piment", "Accompagnement"],
     "description": "Poulet fermier braisé au feu de bois, servi avec l'accompagnement de votre choix."},
    {"section": "Grillades", "name": "Poisson braisé", "price": 5000, "groups": ["Piment", "Accompagnement"],
     "description": "Tilapia entier braisé, oignons et tomates fraîches."},
    {"section": "Grillades", "name": "Brochettes de bœuf", "price": 3000, "groups": ["Piment"],
     "description": "Trois brochettes marinées, oignons grillés."},
    {"section": "Plats", "name": "Amiwo au poulet", "price": 3000,
     "description": "Pâte de maïs à la tomate, poulet en sauce."},
    {"section": "Plats", "name": "Pâte rouge et poisson frit", "price": 3500,
     "description": "Pâte rouge, poisson frit et sauce pimentée."},
    {"section": "Plats", "name": "Akassa et sauce gombo", "price": 2500,
     "description": "Akassa de maïs fermenté, sauce gombo."},
    {"section": "Plats", "name": "Riz gras", "price": 2500,
     "description": "Riz cuit dans une sauce tomate relevée."},
    {"section": "Accompagnements", "name": "Alloco", "price": 1000, "description": "Bananes plantain frites."},
    {"section": "Accompagnements", "name": "Attiéké", "price": 1000, "description": "Semoule de manioc."},
    {"section": "Boissons", "name": "Jus de bissap", "price": 1000, "description": "Fleurs d'hibiscus, servi frais."},
    {"section": "Boissons", "name": "Jus de gingembre", "price": 1000, "description": "Gingembre frais pressé."},
    {"section": "Boissons", "name": "Eau minérale", "price": 500, "description": "Bouteille de 50 cl."},
]

KITCHEN_STATION = "Cuisine"

DELIVERY_ZONE = {"name": "Cotonou", "fee": 500, "minimumOrder": 0, "estimatedMinutes": 30}
PICKUP_LEAD_MINUTES = 20
DELIVERY_LEAD_MINUTES = 30
SERVICE_MODES = ["DineIn", "Takeaway", "Delivery"]
WEB_POLICY = {"channel": "Web", "payOnline": True, "payAtCounter": True, "counterLimit": None, "acceptance": "Automatic"}

SALES_APP = {
    "name": "Démo modèles",
    "kind": "Web",
    "allowedOrigins": ["https://demo.restauspace.com", "http://localhost:3005"],
    "returnUrls": ["https://demo.restauspace.com/", "http://localhost:3005/"],
}

PRO_PLAN = {"name": "Pro", "currency": "XOF", "interval": "Monthly"}

SANDBOX = {
    "code": "sandbox",
    "type": "Online",
    "displayName": "Sandbox (simulation)",
    "methods": ["MobileMoney", "Card"],
    "countries": ["BJ"],
    "currencies": ["XOF"],
}

RESERVATION_POLICY = {
    "minLeadTimeMinutes": 60, "maxAdvanceDays": 30, "defaultDurationMinutes": 90, "cancellationCutoffMinutes": 120,
    "minDurationMinutes": 30, "maxDurationMinutes": 180, "maxPartySize": 12, "acceptsOnlineBookings": True,
}


class ApiError(Exception):
    def __init__(self, status: int, code: str | None, detail: str):
        super().__init__(f"{status} {code or ''} {detail}".strip())
        self.status = status
        self.code = code


class Api:
    def __init__(self, base: str):
        self.base = base.rstrip("/") + "/v1"
        self.token: str | None = None
        self.refresh: str | None = None

    def call(self, method: str, path: str, body: object | None = None, anonymous: bool = False):
        data = None if body is None else json.dumps(body).encode()
        request = urllib.request.Request(self.base + path, data=data, method=method)
        request.add_header("Accept", "application/json")
        if data is not None:
            request.add_header("Content-Type", "application/json")
        if self.token and not anonymous:
            request.add_header("Authorization", "Bearer " + self.token)
            # Comme la console : une clé neuve par POST authentifié (certaines routes l'exigent).
            if method == "POST":
                request.add_header("Idempotency-Key", str(uuid.uuid4()))
        try:
            with urllib.request.urlopen(request, timeout=30) as response:
                raw = response.read()
                return json.loads(raw) if raw else None
        except urllib.error.HTTPError as error:
            code, detail = None, error.reason
            try:
                problem = json.loads(error.read() or b"{}")
                code = problem.get("code")
                detail = problem.get("detail") or problem.get("title") or detail
            except ValueError:
                pass
            raise ApiError(error.code, code, str(detail)) from None
        except urllib.error.URLError as error:
            raise SystemExit(f"API injoignable ({self.base}) : {error.reason}") from None

    def login(self, email: str, password: str, who: str) -> None:
        tokens = self.call("POST", "/auth/login", {"email": email, "password": password}, anonymous=True)
        if tokens and tokens.get("twoFactorRequired"):
            code = input(f"Code de validation en deux étapes ({who}) : ").strip()
            tokens = self.call("POST", "/auth/login/two-factor", {"challenge": tokens["challenge"], "code": code}, anonymous=True)
        self.token = tokens["accessToken"]
        self.refresh = tokens["refreshToken"]

    def logout(self) -> None:
        if self.refresh:
            try:
                self.call("POST", "/auth/logout", {"refreshToken": self.refresh}, anonymous=True)
            except ApiError:
                pass
            self.refresh = None

    def pages(self, path: str) -> list:
        result, page = [], 1
        separator = "&" if "?" in path else "?"
        while True:
            data = self.call("GET", f"{path}{separator}page={page}&pageSize=100")
            result.extend(data["items"])
            if not data.get("hasMore") or not data["items"]:
                return result
            page += 1


class Report:
    """Affichage et compteurs ; en simulation, aucune écriture n'est faite."""

    def __init__(self, dry_run: bool):
        self.dry = dry_run
        self.counts = {"created": 0, "kept": 0, "aligned": 0, "failed": 0}
        self.todo: list[str] = []
        self.tenant_id: str | None = None
        self.demo_refused = False

    @staticmethod
    def step(text: str) -> None:
        print(f"\n== {text}")

    def created(self, text: str) -> None:
        self.counts["created"] += 1
        print(f"  + {'à créer' if self.dry else 'créé':<9} {text}")

    def kept(self, text: str) -> None:
        self.counts["kept"] += 1
        print(f"  = {'déjà là':<9} {text}")

    def aligned(self, text: str) -> None:
        self.counts["aligned"] += 1
        print(f"  ~ {'à aligner' if self.dry else 'aligné':<9} {text}")

    def failed(self, text: str, error: ApiError | None = None) -> None:
        self.counts["failed"] += 1
        suffix = f" ({error.code or error.status})" if error else ""
        print(f"  ! {'échec':<9} {text}{suffix}")

    def note(self, text: str) -> None:
        print(f"  · {text}")


def same(a, b) -> bool:
    return str(a or "").strip().casefold() == str(b or "").strip().casefold()


def by_name(rows: list, name: str, key: str = "name"):
    return next((row for row in rows if same(row.get(key), name)), None)


def money_equal(a, b) -> bool:
    return (a is None and b is None) or (a is not None and b is not None and float(a) == float(b))


# ---------- 1. Compte propriétaire et restaurant


def owner_session(api: Api, report: Report, email: str, password: str) -> bool:
    """Ouvre la session du propriétaire ; inscrit le compte s'il n'existe pas. Faux : relancer plus tard."""
    report.step("Compte propriétaire")
    try:
        api.login(email, password, "propriétaire")
        report.kept("compte propriétaire de démo")
        return True
    except ApiError as error:
        if error.status == 422 and error.code == "identity.user.email_not_verified":
            report.note("compte en attente : l'adresse email n'est pas encore confirmée")
            return confirm_email(api, report, email, password)
        if error.status != 401:
            raise

    # 401 : compte inconnu (ou mauvais mot de passe, que l'inscription révèlera).
    if report.dry:
        report.created(f'compte propriétaire et restaurant « {RESTAURANT["name"]} » (inscription)')
        return False
    options = api.call("GET", "/public/sign-up", anonymous=True)
    plan = pick_sign_up_plan(options.get("plans", []))
    if plan is None:
        print("  Aucun plan XOF proposé à l'inscription : lancer d'abord scripts/seed-plans.py.", file=sys.stderr)
        return False
    try:
        api.call("POST", "/public/sign-up", {
            "email": email, "password": password, "firstName": "Démo", "lastName": "RestauSpace",
            "restaurantName": RESTAURANT["name"], "country": RESTAURANT["country"], "planId": plan["id"],
            "acceptTerms": True, "website": None,
        }, anonymous=True)
    except ApiError as error:
        if error.code == "identity.user.email_already_used":
            print("  Ce compte existe déjà avec un autre mot de passe : vérifier DEMO_OWNER_PASSWORD.", file=sys.stderr)
            return False
        raise
    report.created(f'compte propriétaire et restaurant « {RESTAURANT["name"]} » (essai {plan["name"]})')
    report.note("un lien de confirmation a été envoyé à l'adresse du compte")
    return confirm_email(api, report, email, password)


def pick_sign_up_plan(plans: list):
    xof = [p for p in plans if p.get("currencyCode") == PRO_PLAN["currency"]]
    exact = next((p for p in xof if same(p["name"], PRO_PLAN["name"]) and p.get("billingInterval") == PRO_PLAN["interval"]), None)
    return exact or (xof[0] if xof else None)


def confirm_email(api: Api, report: Report, email: str, password: str) -> bool:
    if report.dry or not sys.stdin.isatty():
        report.todo.append("confirmer l'adresse du compte propriétaire (lien reçu par email), puis relancer le script")
        return False
    token = input("  Jeton de confirmation (token=... du lien reçu ; vide pour arrêter) : ").strip()
    if not token:
        report.todo.append("confirmer l'adresse du compte propriétaire (lien reçu par email), puis relancer le script")
        return False
    api.call("POST", "/auth/verify-email", {"token": urllib.parse.unquote(token), "password": password}, anonymous=True)
    report.aligned("adresse du compte confirmée")
    api.login(email, password, "propriétaire")
    return True


def select_restaurant(api: Api, report: Report, admin: Api | None, email: str) -> str | None:
    """Le restaurant « Chez Awa » du propriétaire, sélectionné dans son jeton (claim tid)."""
    report.step("Restaurant")
    memberships = api.call("GET", "/me/memberships")
    mine = [m for m in memberships if same(m["tenantName"], RESTAURANT["name"])]
    current = next((m for m in mine if m["status"] == "Active"), None) or (mine[0] if mine else None)
    if current is None:
        # Compte existant sans restaurant (l'inscription n'est plus possible) : création par la plateforme.
        if admin is None:
            print("  Le compte n'a pas de restaurant « Chez Awa » : relancer sans --skip-admin pour le créer.", file=sys.stderr)
            return None
        if report.dry:
            report.created(f'restaurant « {RESTAURANT["name"]} » (par la plateforme)')
            return None
        tenant_id = admin.call("POST", "/admin/tenants", {
            "name": RESTAURANT["name"], "defaultCurrencyCode": RESTAURANT["currency"],
            "timeZoneId": RESTAURANT["timeZone"], "pricesIncludeTax": True,
        })["id"]
        admin.call("POST", f"/admin/tenants/{tenant_id}/owners", {"email": email})
        report.created(f'restaurant « {RESTAURANT["name"]} », propriétaire rattaché')
        current = next(m for m in api.call("GET", "/me/memberships") if m["tenantId"] == tenant_id)
    else:
        report.kept(f'restaurant « {RESTAURANT["name"]} »')

    tenant_id = current["tenantId"]
    report.tenant_id = tenant_id
    if current["status"] == "Invited":
        if report.dry:
            report.aligned("invitation du propriétaire à accepter")
            return None
        api.call("POST", f"/me/memberships/{tenant_id}/accept")
        report.aligned("invitation du propriétaire acceptée")
    token = api.call("POST", "/me/tenant", {"tenantId": tenant_id})
    api.token = token["accessToken"]
    return tenant_id


def ensure_tenant_settings(api: Api, report: Report) -> None:
    tenant = api.call("GET", "/tenant")
    if tenant["defaultCurrencyCode"] == RESTAURANT["currency"]:
        report.kept(f'devise {RESTAURANT["currency"]}')
    else:
        write(report, f'devise {RESTAURANT["currency"]}', lambda: api.call(
            "PUT", "/tenant/default-currency", {"currencyCode": RESTAURANT["currency"]}))
    if tenant["timeZoneId"] == RESTAURANT["timeZone"]:
        report.kept(f'fuseau {RESTAURANT["timeZone"]}')
    else:
        write(report, f'fuseau {RESTAURANT["timeZone"]}', lambda: api.call(
            "PUT", "/tenant/time-zone", {"timeZoneId": RESTAURANT["timeZone"]}))
    if not tenant.get("pricesIncludeTax", True):
        write(report, "prix TTC", lambda: api.call("PUT", "/tenant/price-tax-mode", {"pricesIncludeTax": True}))

    branding = tenant.get("branding") or {}
    if same(branding.get("displayName"), RESTAURANT["name"]) and same(branding.get("primaryColor"), RESTAURANT["primaryColor"]):
        report.kept(f'identité visuelle (couleur {RESTAURANT["primaryColor"].lower()})')
    else:
        # Remplace toute l'identité : le logo existant est renvoyé tel quel.
        write(report, f'identité visuelle (couleur {RESTAURANT["primaryColor"].lower()})', lambda: api.call("PUT", "/tenant/branding", {
            "displayName": RESTAURANT["name"], "logoUrl": branding.get("logoUrl"), "primaryColor": RESTAURANT["primaryColor"],
        }))

    profile = tenant.get("publicProfile") or {}
    if profile.get("tagline") == RESTAURANT["tagline"] and profile.get("description") == RESTAURANT["description"]:
        report.kept("profil public")
    else:
        # Remplace tout le profil : couverture et liens existants conservés.
        write(report, "profil public (accroche, description)", lambda: api.call("PUT", "/tenant/public-profile", {
            "tagline": RESTAURANT["tagline"], "description": RESTAURANT["description"],
            "coverImageUrl": profile.get("coverImageUrl"),
            "links": [{"network": link["network"], "url": link["url"]} for link in profile.get("links") or []],
        }))


def write(report: Report, text: str, action, created: bool = False):
    """Une écriture : rien en simulation ; une erreur est comptée sans arrêter le script."""
    if report.dry:
        (report.created if created else report.aligned)(text)
        return None
    try:
        result = action()
    except ApiError as error:
        report.failed(text, error)
        return None
    (report.created if created else report.aligned)(text)
    return result


# ---------- 2. Plan offert


def ensure_complimentary(admin: Api, report: Report, tenant_id: str) -> None:
    report.step("Plan Pro offert (plateforme)")
    plans = admin.call("GET", f'/admin/billing/plans?activeOnly=true&currency={PRO_PLAN["currency"]}')
    plan = next((p for p in plans if same(p["name"], PRO_PLAN["name"]) and p["currencyCode"] == PRO_PLAN["currency"]
                 and p["billingInterval"] == PRO_PLAN["interval"] and (p.get("kind") or "Base") == "Base"), None)
    if plan is None:
        report.failed("plan Pro mensuel XOF introuvable : lancer scripts/seed-plans.py")
        return
    api_enabled = any(e["featureCode"] == "storefront.api" and e["enabled"] for e in plan.get("entitlements", []))
    if not api_enabled:
        report.note("le plan Pro n'a pas storefront.api : vérifier le catalogue des plans")
    subscriptions = admin.call("GET", f"/admin/billing/subscriptions?tenantId={tenant_id}&page=1&pageSize=20")["items"]
    live = next((s for s in subscriptions if s["status"] != "Cancelled"), None)
    if (live and live["planId"] == plan["id"] and live.get("isComplimentary") and live.get("complimentaryUntilUtc") is None
            and not live.get("complimentaryRenewalMonths")):
        report.kept("Pro mensuel XOF offert, sans limite de durée")
        return
    write(report, "Pro mensuel XOF offert, sans limite de durée", lambda: admin.call(
        "POST", f"/admin/billing/tenants/{tenant_id}/complimentary", {"planId": plan["id"], "untilUtc": None, "renewalMonths": None}),
        created=live is None or not live.get("isComplimentary"))


# ---------- 3. Établissement


def weekly_hours() -> list[dict]:
    return [{"day": day, "start": start, "end": end, "endsNextDay": end <= start} for day, start, end in LOCATION["hours"]]


def ensure_location(api: Api, report: Report) -> str | None:
    report.step("Établissement")
    rows = api.pages("/locations?includeArchived=false")
    summary = by_name(rows, LOCATION["name"])
    if summary is None:
        created = write(report, f'« {LOCATION["name"]} »', lambda: api.call("POST", "/locations", {
            "name": LOCATION["name"], "address": LOCATION["address"], "seats": LOCATION["seats"],
            "timeZoneId": RESTAURANT["timeZone"], "weeklyHours": weekly_hours(),
        }), created=True)
        if created is None:
            return None
        location_id = created["id"]
        write(report, "téléphone factice", lambda: api.call("PUT", f"/locations/{location_id}/contact", {
            "phone": LOCATION["phone"], "whatsApp": None, "email": None}))
        write(report, "établissement ouvert", lambda: api.call("POST", f"/locations/{location_id}/activate"))
        return location_id

    location_id = summary["id"]
    report.kept(f'« {LOCATION["name"]} »')
    location = api.call("GET", f"/locations/{location_id}")
    address = location["address"]
    wanted = LOCATION["address"]
    if same(address["street"], wanted["street"]) and same(address["city"], wanted["city"]) and address["countryCode"] == wanted["countryCode"]:
        report.kept("adresse (fictive, Akpakpa)")
    else:
        write(report, "adresse (fictive, Akpakpa)", lambda: api.call("PUT", f"/locations/{location_id}/address", wanted))
    if location["seats"] == LOCATION["seats"]:
        report.kept(f'{LOCATION["seats"]} places')
    else:
        write(report, f'{LOCATION["seats"]} places', lambda: api.call("PUT", f"/locations/{location_id}/capacity", {"seats": LOCATION["seats"]}))
    if location["timeZoneId"] != RESTAURANT["timeZone"]:
        write(report, f'fuseau {RESTAURANT["timeZone"]}', lambda: api.call(
            "PUT", f"/locations/{location_id}/time-zone", {"timeZoneId": RESTAURANT["timeZone"]}))
    have = {(s["day"], s["start"], s["end"], bool(s["endsNextDay"])) for s in location["weeklyHours"]}
    want = {(s["day"], s["start"], s["end"], s["endsNextDay"]) for s in weekly_hours()}
    if have == want:
        report.kept("horaires")
    else:
        write(report, "horaires", lambda: api.call("PUT", f"/locations/{location_id}/business-hours", {"weeklyHours": weekly_hours()}))
    if (location.get("contact") or {}).get("phone") == LOCATION["phone"]:
        report.kept("téléphone factice")
    else:
        contact = location.get("contact") or {}
        write(report, "téléphone factice", lambda: api.call("PUT", f"/locations/{location_id}/contact", {
            "phone": LOCATION["phone"], "whatsApp": contact.get("whatsApp"), "email": contact.get("email")}))
    if location["status"] == "Draft":
        write(report, "établissement ouvert", lambda: api.call("POST", f"/locations/{location_id}/activate"))
    elif location["status"] == "TemporarilyClosed":
        write(report, "établissement rouvert", lambda: api.call("POST", f"/locations/{location_id}/reopen"))
    else:
        report.kept("établissement ouvert")
    return location_id


# ---------- 4. Carte


def ensure_catalog(api: Api, report: Report) -> dict[str, str]:
    report.step("Carte : taxe et options")
    taxes = api.call("GET", "/catalog/tax-categories")
    tax = next((t for t in taxes if t["code"].upper() == TAX["code"]), None)
    if tax:
        report.kept(f'taxe {TAX["code"]}')
        tax_id = tax["id"]
    else:
        created = write(report, f'taxe {TAX["code"]} (18 %)', lambda: api.call("POST", "/catalog/tax-categories", TAX), created=True)
        tax_id = created["id"] if created else None

    groups = api.call("GET", "/catalog/modifier-groups")
    group_ids: dict[str, str] = {}
    for spec in OPTION_GROUPS:
        label = f'options « {spec["name"]} »'
        group = by_name(groups, spec["name"])
        if group is None:
            created = write(report, label, lambda: api.call(
                "POST", "/catalog/modifier-groups", {"name": spec["name"], "minSelections": spec["min"], "maxSelections": spec["max"]}),
                created=True)
            if created is None:
                continue
            group = {"id": created["id"], "minSelections": spec["min"], "maxSelections": spec["max"], "modifiers": []}
        else:
            report.kept(label)
        group_ids[spec["name"]] = group["id"]
        if (group["minSelections"], group["maxSelections"]) != (spec["min"], spec["max"]):
            write(report, f"{label} : choix {spec['min']} à {spec['max']}", lambda: api.call(
                "PUT", f'/catalog/modifier-groups/{group["id"]}/selection-rule', {"minSelections": spec["min"], "maxSelections": spec["max"]}))
        for option, price in spec["options"]:
            modifier = by_name(group["modifiers"], option)
            if modifier is None:
                write(report, f"{label} : {option}", lambda: api.call(
                    "POST", f'/catalog/modifier-groups/{group["id"]}/modifiers', {"name": option, "priceDelta": price}), created=True)
                continue
            if not money_equal(modifier["priceDelta"], price):
                write(report, f"{label} : prix de {option}", lambda: api.call(
                    "PUT", f'/catalog/modifier-groups/{group["id"]}/modifiers/{modifier["id"]}/price', {"priceDelta": price}))
            if not modifier["isAvailable"]:
                write(report, f"{label} : {option} disponible", lambda: api.call(
                    "PUT", f'/catalog/modifier-groups/{group["id"]}/modifiers/{modifier["id"]}/availability', {"isAvailable": True}))

    report.step("Carte : articles")
    products = [p for p in api.pages("/catalog/products") if p["status"] != "Archived"]
    product_ids: dict[str, str] = {}
    for spec in PRODUCTS:
        label = f'{spec["name"]} · {spec["price"]:g} XOF'
        current = by_name(products, spec["name"])
        if current is None:
            if tax_id is None:
                report.created(label) if report.dry else report.failed(f"{label} : pas de catégorie de taxe")
                continue
            product_id = write(report, label, lambda: create_product(api, spec, tax_id, group_ids), created=True)
            if product_id:
                product_ids[spec["name"]] = product_id
            continue

        product_id = current["id"]
        product_ids[spec["name"]] = product_id
        detail = api.call("GET", f"/catalog/products/{product_id}")
        changes = []
        if not money_equal(detail["basePrice"], spec["price"]):
            changes.append(("prix", lambda: api.call("PUT", f"/catalog/products/{product_id}/base-price", {"basePrice": spec["price"]})))
        if (detail.get("description") or "") != spec["description"]:
            changes.append(("description", lambda: api.call(
                "PUT", f"/catalog/products/{product_id}/description", {"description": spec["description"]})))
        for group in spec.get("groups", []):
            if group in group_ids and group_ids[group] not in detail["modifierGroupIds"]:
                gid = group_ids[group]
                changes.append((f"options {group}", lambda gid=gid: api.call(
                    "POST", f"/catalog/products/{product_id}/modifier-groups", {"modifierGroupId": gid})))
        if detail.get("hiddenChannels"):
            changes.append(("visible en ligne", lambda: api.call(
                "PUT", f"/catalog/products/{product_id}/hidden-channels", {"channels": []})))
        if detail["status"] in ("Draft", "Unavailable"):
            changes.append(("en vente", lambda: api.call("POST", f"/catalog/products/{product_id}/activate")))
        if not changes:
            report.kept(label)
            continue
        names = ", ".join(name for name, _ in changes)
        write(report, f"{label} : {names}", lambda: [action() for _, action in changes])
    return product_ids


def create_product(api: Api, spec: dict, tax_id: str, group_ids: dict[str, str]) -> str:
    product_id = api.call("POST", "/catalog/products", {
        "name": spec["name"], "basePrice": spec["price"], "taxCategoryId": tax_id, "description": spec["description"]})["id"]
    for group in spec.get("groups", []):
        if group in group_ids:
            api.call("POST", f"/catalog/products/{product_id}/modifier-groups", {"modifierGroupId": group_ids[group]})
    api.call("POST", f"/catalog/products/{product_id}/activate")
    return product_id


def ensure_menu(api: Api, report: Report, location_id: str, product_ids: dict[str, str]) -> None:
    report.step("Carte : menu publié")
    menus = api.call("GET", f"/catalog/menus?locationId={location_id}")
    current = next((m for m in menus if same(m["name"], MENU_NAME) and m["status"] != "Archived"), None)
    if current is None:
        created = write(report, f"menu « {MENU_NAME} »", lambda: api.call(
            "POST", "/catalog/menus", {"locationId": location_id, "name": MENU_NAME}), created=True)
        if created is None:
            return
        menu = {"id": created["id"], "status": "Draft", "sections": []}
    else:
        menu = api.call("GET", f'/catalog/menus/{current["id"]}')

    missing: list[tuple[str, list[str]]] = []
    for section_name in SECTIONS:
        section = by_name(menu["sections"], section_name)
        have = set(section["productIds"]) if section else set()
        wanted = [product_ids[p["name"]] for p in PRODUCTS if p["section"] == section_name and p["name"] in product_ids]
        absent = [pid for pid in wanted if pid not in have]
        if section is None or absent:
            missing.append((section_name, absent))
    if not missing and menu["status"] == "Published":
        report.kept(f"menu « {MENU_NAME} » publié ({', '.join(SECTIONS)})")
        return
    if report.dry:
        for section_name, absent in missing:
            report.aligned(f"section « {section_name} » : {len(absent)} article(s) à ajouter")
        report.aligned(f"menu « {MENU_NAME} » à publier")
        return
    try:
        menu_id = menu["id"]
        if menu["status"] == "Published":
            # Le contenu ne se modifie qu'en brouillon : dépublié le temps de l'ajout.
            api.call("POST", f"/catalog/menus/{menu_id}/unpublish")
        for section_name, absent in missing:
            section = by_name(menu["sections"], section_name)
            if section is None:
                section = {"id": api.call("POST", f"/catalog/menus/{menu_id}/sections", {"name": section_name})["id"]}
            for product_id in absent:
                api.call("POST", f'/catalog/menus/{menu_id}/sections/{section["id"]}/products', {"productId": product_id})
            report.aligned(f"section « {section_name} » : {len(absent)} article(s) ajouté(s)")
        api.call("POST", f"/catalog/menus/{menu_id}/publish")
        report.aligned(f"menu « {MENU_NAME} » publié")
    except ApiError as error:
        report.failed(f"menu « {MENU_NAME} »", error)


def ensure_kitchen(api: Api, report: Report, location_id: str) -> None:
    # Sans routage, une commande acceptée ne peut pas créer son ticket de cuisine.
    report.step("Cuisine")
    stations = api.call("GET", f"/kitchen/stations?locationId={location_id}")
    station = by_name(stations, KITCHEN_STATION)
    if station:
        report.kept(f"poste « {KITCHEN_STATION} »")
        station_id = station["id"]
    else:
        created = write(report, f"poste « {KITCHEN_STATION} »", lambda: api.call(
            "POST", "/kitchen/stations", {"locationId": location_id, "name": KITCHEN_STATION}), created=True)
        station_id = created["id"] if created else None
    try:
        api.call("GET", f"/kitchen/routings/{location_id}")
        report.kept("routage de la cuisine")
    except ApiError as error:
        if error.status != 404:
            raise
        if station_id or report.dry:
            write(report, f"routage vers « {KITCHEN_STATION} »", lambda: api.call(
                "POST", f"/kitchen/routings/{location_id}", {"defaultStationId": station_id}), created=True)


# ---------- 5. Vente en ligne


def ensure_delivery(api: Api, report: Report, location_id: str) -> None:
    report.step("Retrait et livraison")
    settings = api.call("GET", f"/delivery/settings/{location_id}")
    zone = by_name(settings["zones"], DELIVERY_ZONE["name"])
    label = f'zone « {DELIVERY_ZONE["name"]} » ({DELIVERY_ZONE["fee"]} XOF)'
    if zone is None:
        write(report, label, lambda: api.call("POST", f"/delivery/settings/{location_id}/zones", DELIVERY_ZONE), created=True)
    else:
        if (money_equal(zone["fee"], DELIVERY_ZONE["fee"]) and money_equal(zone["minimumOrder"], DELIVERY_ZONE["minimumOrder"])
                and zone["estimatedMinutes"] == DELIVERY_ZONE["estimatedMinutes"]):
            report.kept(label)
        else:
            write(report, label, lambda: api.call("PUT", f'/delivery/settings/{location_id}/zones/{zone["id"]}', DELIVERY_ZONE))
        if not zone["isActive"]:
            write(report, f"{label} active", lambda: api.call("POST", f'/delivery/settings/{location_id}/zones/{zone["id"]}/activate'))
    if settings["pickupEnabled"] and settings["pickupLeadMinutes"] == PICKUP_LEAD_MINUTES:
        report.kept("à emporter")
    else:
        write(report, "à emporter", lambda: api.call(
            "PUT", f"/delivery/settings/{location_id}/pickup", {"enabled": True, "leadMinutes": PICKUP_LEAD_MINUTES}))
    if settings["deliveryEnabled"] and settings["deliveryLeadMinutes"] == DELIVERY_LEAD_MINUTES:
        report.kept("livraison")
    else:
        # Après la zone : la livraison exige une zone active.
        write(report, "livraison", lambda: api.call(
            "PUT", f"/delivery/settings/{location_id}/delivery", {"enabled": True, "leadMinutes": DELIVERY_LEAD_MINUTES}))


def ensure_online_sales(api: Api, report: Report, location_id: str, rotate_key: bool) -> str | None:
    """Réglages de l'établissement et application Web. Renvoie l'id de l'application."""
    report.step("Vente en ligne")
    overview = api.call("GET", "/online-sales")
    settings = next((s for s in overview["locations"] if s["locationId"] == location_id), None)
    web = next((p for p in (settings or {}).get("policies", []) if p["channel"] == "Web"), None)
    policy_ok = web is not None and all(web.get(k) == v for k, v in WEB_POLICY.items())
    modes_ok = settings is not None and set(settings["serviceModes"]) == set(SERVICE_MODES)
    label = "sur place, à emporter, livraison ; acceptation automatique ; paiement au comptoir et en ligne"
    if settings and settings["isSaved"] and not settings["isPaused"] and modes_ok and policy_ok:
        report.kept(label)
    else:
        base = settings or {}
        write(report, label, lambda: api.call("PUT", f"/online-sales/locations/{location_id}", {
            "isPaused": False,
            "serviceModes": SERVICE_MODES,
            "preparationMinutes": base.get("preparationMinutes") or 20,
            "minimumOrderAmount": base.get("minimumOrderAmount"),
            "manualAcceptanceMinutes": base.get("manualAcceptanceMinutes") or 10,
            "unpaidExpiryMinutes": base.get("unpaidExpiryMinutes") or 15,
            "policies": [WEB_POLICY],
            "slotCapacity": base.get("slotCapacity"),
        }))

    apps = [a for a in overview["apps"] if a["kind"] == SALES_APP["kind"]]
    app = by_name(apps, SALES_APP["name"])
    app_label = f'application Web « {SALES_APP["name"]} »'
    if app is None:
        if report.dry:
            report.created(app_label)
            return None
        try:
            issued = api.call("POST", "/online-sales/apps", {
                "name": SALES_APP["name"], "kind": SALES_APP["kind"], "allowedOrigins": SALES_APP["allowedOrigins"],
                "locationIds": [location_id], "returnUrls": SALES_APP["returnUrls"], "requireHumanCheck": False,
            })
        except ApiError as error:
            report.failed(app_label, error)
            if error.code == "common.plan.feature_required":
                report.todo.append("donner storefront.api au restaurant (étape 2, sans --skip-admin), puis relancer")
            return None
        report.created(app_label)
        show_key(issued["key"])
        return issued["id"]

    app_id = app["id"]
    origins = sorted(o.rstrip("/").lower() for o in SALES_APP["allowedOrigins"])
    if (sorted(app["allowedOrigins"]) == origins and sorted(app["returnUrls"]) == sorted(SALES_APP["returnUrls"])
            and app["locationIds"] == [location_id]):
        report.kept(f"{app_label} (origines, pages de retour, établissement)")
    else:
        write(report, f"{app_label} (origines, pages de retour, établissement)", lambda: api.call("PUT", f"/online-sales/apps/{app_id}", {
            "name": SALES_APP["name"], "allowedOrigins": SALES_APP["allowedOrigins"], "locationIds": [location_id],
            "returnUrls": SALES_APP["returnUrls"],
        }))
    if app["status"] != "Active":
        write(report, f"{app_label} réactivée", lambda: api.call("POST", f"/online-sales/apps/{app_id}/enable"))
    if rotate_key:
        issued = write(report, f"{app_label} : nouvelle clé publique", lambda: api.call("POST", f"/online-sales/apps/{app_id}/key"))
        if issued:
            show_key(issued["key"])
    else:
        report.note(f'clé publique inchangée ({app["keyPrefix"]}…{app["keyHint"]}) ; --rotate-key pour en émettre une nouvelle')
    return app_id


def show_key(key: str) -> None:
    print("\n  Clé publique de l'application (affichée UNE seule fois) :")
    print(f"      {key}")
    print("  À mettre dans NEXT_PUBLIC_DEMO_STOREFRONT_KEY (site de démo des modèles).\n")


# ---------- 6. Paiement simulé


def ensure_sandbox_provider(admin: Api, report: Report) -> bool:
    report.step("Prestataire sandbox (plateforme)")
    integrations = admin.call("GET", "/admin/payment-providers/integrations")
    if not any(i["code"] == SANDBOX["code"] for i in integrations):
        report.failed("intégration sandbox absente du serveur")
        report.todo.append("activer Payments__EnableSandbox (et Payments__AllowSandboxOutsideDevelopment hors développement), "
                           "redéployer, relancer le script")
        return False
    providers = admin.call("GET", "/admin/payment-providers")
    provider = next((p for p in providers if p["code"] == SANDBOX["code"]), None)
    label = "sandbox au catalogue (XOF, BJ, MobileMoney et Card)"
    if provider is None:
        return write(report, label, lambda: admin.call("POST", "/admin/payment-providers", {
            "code": SANDBOX["code"], "type": SANDBOX["type"], "displayName": SANDBOX["displayName"], "capability": {
                "methods": SANDBOX["methods"], "countries": SANDBOX["countries"], "currencies": SANDBOX["currencies"],
                "supportsRefund": True, "supportsPartialRefund": True, "supportsWebhooks": True, "paymentExpirationMinutes": 30,
            }}), created=True) is not None or report.dry

    capability = provider["capability"]
    # Union avec l'existant : rien n'est retiré à d'autres usages du sandbox. Liste vide = tous.
    methods = sorted(set(capability["methods"]) | set(SANDBOX["methods"]))
    countries = capability["countries"] and sorted(set(capability["countries"]) | set(SANDBOX["countries"]))
    currencies = capability["currencies"] and sorted(set(capability["currencies"]) | set(SANDBOX["currencies"]))
    if methods == sorted(capability["methods"]) and countries == capability["countries"] and currencies == capability["currencies"]:
        report.kept(label)
    else:
        write(report, label, lambda: admin.call("PUT", f'/admin/payment-providers/{provider["id"]}', {
            "displayName": provider["displayName"], "capability": {
                "methods": methods, "countries": countries, "currencies": currencies,
                "supportsAuthorization": capability["supportsAuthorization"], "supportsRefund": capability["supportsRefund"],
                "supportsPartialRefund": capability["supportsPartialRefund"], "supportsWebhooks": capability["supportsWebhooks"],
                "supportsRecurring": capability["supportsRecurring"], "paymentExpirationMinutes": capability["paymentExpirationMinutes"],
                "amountLimits": capability.get("amountLimits") or [],
            }}))
    if not provider["isEnabled"]:
        write(report, "sandbox proposé aux restaurants", lambda: admin.call("POST", f'/admin/payment-providers/{provider["id"]}/enable'))
    return True


def demo_refusal(report: Report, tenant_id: str, error: ApiError) -> None:
    report.failed("configuration sandbox refusée par l'API", error)
    print("\n  Le paiement simulé n'est accepté que pour un restaurant de démonstration.")
    print("  Ajouter cet id à Demo__TenantIds dans api.env, redéployer, puis relancer le script :")
    print(f"      {tenant_id}\n")
    report.demo_refused = True
    report.todo.append(f"ajouter {tenant_id} à Demo__TenantIds (api.env), redéployer, relancer le script")


def ensure_sandbox_configuration(api: Api, report: Report, tenant_id: str, location_id: str) -> None:
    report.step("Paiement simulé (restaurant)")
    configurations = api.call("GET", "/payments/configurations")
    configuration = next((c for c in configurations if c["provider"] == SANDBOX["code"]), None)
    try:
        if configuration is None:
            if report.dry:
                report.created("configuration sandbox (secret de webhook généré, activée pour l'établissement)")
                return
            configuration_id = api.call("POST", "/payments/configurations", {"provider": SANDBOX["code"], "mode": "TenantManaged"})["id"]
            report.created("configuration sandbox")
            configuration = next(c for c in api.call("GET", "/payments/configurations") if c["id"] == configuration_id)
        else:
            report.kept("configuration sandbox")
        configuration_id = configuration["id"]

        if configuration["hasCredentials"]:
            report.kept("secret de webhook (conservé)")
        elif report.dry:
            report.aligned("secret de webhook à générer")
        else:
            # Secret aléatoire, envoyé au coffre et jamais affiché ni écrit.
            api.call("PUT", f"/payments/configurations/{configuration_id}/credentials",
                     {"credentials": {"webhook_secret": secrets.token_urlsafe(32)}})
            report.aligned("secret de webhook généré (non affiché)")

        for method in SANDBOX["methods"]:
            if method not in configuration["enabledMethods"]:
                write(report, f"moyen {method}", lambda method=method: api.call(
                    "PUT", f"/payments/configurations/{configuration_id}/methods", {"method": method, "isActive": True}))
            assigned = next((a for a in configuration["assignments"] if a["locationId"] == location_id and a["method"] == method), None)
            if assigned and assigned["isEnabled"]:
                report.kept(f"{method} proposé dans l'établissement")
            else:
                write(report, f"{method} proposé dans l'établissement", lambda method=method: api.call(
                    "PUT", f"/payments/configurations/{configuration_id}/locations/{location_id}", {
                        "method": method, "priority": 0, "minAmount": None, "maxAmount": None, "currency": None, "isEnabled": True}))

        if configuration["isEnabled"]:
            report.kept("sandbox activé")
        elif report.dry:
            report.aligned("sandbox à activer")
        else:
            api.call("POST", f"/payments/configurations/{configuration_id}/enable")
            report.aligned("sandbox activé")
    except ApiError as error:
        if error.code == "payments.provider_not_found" or error.status == 404:
            report.failed("sandbox absent du catalogue de la plateforme", error)
            report.todo.append("ajouter sandbox au catalogue (relancer sans --skip-admin)")
            return
        if error.status in (403, 409, 422):
            demo_refusal(report, tenant_id, error)
            return
        raise


# ---------- 7. Réservations


def ensure_reservations(api: Api, report: Report, location_id: str) -> None:
    report.step("Réservations")
    policy = api.call("GET", f"/reservations/policies/{location_id}")
    label = (f'en ligne, confirmées aussitôt, jusqu\'à {RESERVATION_POLICY["maxPartySize"]} personnes, '
             f'{RESERVATION_POLICY["maxAdvanceDays"]} jours à l\'avance')
    if all(policy.get(k) == v for k, v in RESERVATION_POLICY.items()):
        report.kept(label)
    else:
        write(report, label, lambda: api.call("PUT", f"/reservations/policies/{location_id}", RESERVATION_POLICY))
    deposit = api.call("GET", f"/reservations/deposit-rules/{location_id}")
    if deposit["mode"] == "Off":
        report.kept("sans acompte")
    else:
        write(report, "sans acompte", lambda: api.call("PUT", f"/reservations/deposit-rules/{location_id}", {"mode": "Off"}))
    report.note(f'capacité : les {LOCATION["seats"]} places de l\'établissement')


# ---------- Programme


def main() -> int:
    parser = argparse.ArgumentParser(description="Crée ou aligne le restaurant de démonstration « Chez Awa » par l'API.")
    parser.add_argument("--api", default=os.environ.get("SAASRESTAURANT_API", "http://localhost:5080"))
    parser.add_argument("--admin-email", default=os.environ.get("SAASRESTAURANT_ADMIN_EMAIL"), help="compte administrateur de la plateforme")
    parser.add_argument("--skip-admin", action="store_true", help="sauter les étapes de la plateforme (plan offert, catalogue sandbox)")
    parser.add_argument("--rotate-key", action="store_true", help="émettre une nouvelle clé publique pour l'application existante")
    parser.add_argument("--dry-run", action="store_true", help="afficher ce qui serait fait, sans rien écrire")
    args = parser.parse_args()

    if not args.skip_admin and not args.admin_email:
        parser.error("--admin-email est requis (ou --skip-admin)")
    owner_email = (os.environ.get("DEMO_OWNER_EMAIL") or input("Email du compte propriétaire de démo : ")).strip()
    owner_password = os.environ.get("DEMO_OWNER_PASSWORD") or getpass.getpass("Mot de passe du compte propriétaire de démo : ")

    report = Report(args.dry_run)
    if args.dry_run:
        print("Simulation : rien ne sera écrit.")

    admin: Api | None = None
    if not args.skip_admin:
        admin_password = os.environ.get("SAASRESTAURANT_ADMIN_PASSWORD") or getpass.getpass("Mot de passe du compte administrateur : ")
        admin = Api(args.api)
        try:
            admin.login(args.admin_email, admin_password, "administrateur")
        except ApiError as error:
            print(f"Connexion administrateur refusée ({error.status}) : vérifier l'email et le mot de passe.", file=sys.stderr)
            return 1
        finally:
            admin_password = ""

    owner = Api(args.api)
    location_id = app_id = None
    try:
        if admin is not None:
            try:
                admin.call("GET", "/admin/billing/plans?activeOnly=true")
            except ApiError as error:
                if error.status == 403:
                    print("Accès refusé : ce compte n'est pas dans Authorization:PlatformAdminEmails.", file=sys.stderr)
                    return 1
                raise

        if not owner_session(owner, report, owner_email, owner_password):
            return finish(report, location_id, app_id)
        tenant_id = select_restaurant(owner, report, admin, owner_email)
        if tenant_id is None:
            return finish(report, location_id, app_id)
        ensure_tenant_settings(owner, report)

        if admin is not None:
            ensure_complimentary(admin, report, tenant_id)
        else:
            report.step("Plan Pro offert (plateforme)")
            report.note("sauté (--skip-admin)")

        location_id = ensure_location(owner, report)
        if location_id is None:
            report.note("la suite dépend de l'établissement : relancer sans --dry-run")
            return finish(report, location_id, app_id)
        product_ids = ensure_catalog(owner, report)
        ensure_menu(owner, report, location_id, product_ids)
        ensure_kitchen(owner, report, location_id)
        ensure_delivery(owner, report, location_id)
        app_id = ensure_online_sales(owner, report, location_id, args.rotate_key)

        provider_ready = True
        if admin is not None:
            provider_ready = ensure_sandbox_provider(admin, report)
        else:
            report.step("Prestataire sandbox (plateforme)")
            report.note("sauté (--skip-admin) : sandbox doit déjà être au catalogue")
        if provider_ready:
            ensure_sandbox_configuration(owner, report, tenant_id, location_id)

        ensure_reservations(owner, report, location_id)

        report.step("Avis")
        report.note("aucun avis créé : l'API n'en accepte qu'avec une commande terminée (pas de faux avis)")
    except ApiError as error:
        sys.stdout.flush()
        print(f"\nÉchec : {error.code or error.status} {error}", file=sys.stderr)
        report.counts["failed"] += 1
    finally:
        owner_password = ""
        owner.logout()
        if admin is not None:
            admin.logout()
    return finish(report, location_id, app_id)


def finish(report: Report, location_id: str | None, app_id: str | None) -> int:
    tenant_id = report.tenant_id
    counts = report.counts
    mode = " (simulation, rien n'a été écrit)" if report.dry else ""
    print("\n== Résumé")
    print(f"  {counts['created']} créé(s), {counts['kept']} déjà là, {counts['aligned']} aligné(s), {counts['failed']} échec(s){mode}.")
    print(f"  Restaurant (pour Demo__TenantIds) : {tenant_id or '-'}")
    print(f"  Établissement                     : {location_id or '-'}")
    print(f"  Application de vente              : {app_id or '-'}")
    todo = list(dict.fromkeys(report.todo))
    if report.dry:
        todo.append("relancer sans --dry-run pour appliquer")
    if tenant_id and not report.demo_refused:
        todo.append(f"vérifier que {tenant_id} figure dans Demo__TenantIds (api.env)")
    todo.append("NEXT_PUBLIC_DEMO_STOREFRONT_KEY : la clé publique affichée à la création (ou --rotate-key)")
    print("  Reste à faire :")
    for item in todo:
        print(f"    - {item}")
    return 1 if counts["failed"] else 0


if __name__ == "__main__":
    sys.exit(main())
