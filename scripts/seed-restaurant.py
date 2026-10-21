#!/usr/bin/env python3
"""Crée un restaurant de test complet par l'API (aucune écriture SQL directe), prêt pour la console du restaurant.

Ce qui est créé, à partir de scripts/restaurants/chez-awa.json (ou --file) :
  1. le restaurant (tenant) par l'API d'administration, et son propriétaire ;
  2. un établissement ouvert, avec ses horaires ;
  3. une catégorie de taxe, des groupes d'options, des produits en vente (variantes, options) ;
  4. une carte publiée par sections ;
  5. les postes de cuisine et le routage des produits ;
  6. les salles et les tables.

Rejouable : ce qui existe déjà (même nom) est gardé tel quel, seul ce qui manque est créé.

Le propriétaire est par défaut le compte administrateur. Avec --owner-email, un autre compte est utilisé ; s'il
n'existe pas encore, il est invité par l'API puis son invitation est acceptée avec le mot de passe choisi : le lien
d'invitation est lu dans les logs de l'API en développement (`docker compose logs api`, lignes « [DEV EMAIL] »), ou
demandé au clavier si les logs ne sont pas accessibles.

Usage :
    python3 scripts/seed-restaurant.py
    python3 scripts/seed-restaurant.py --owner-email gerant@example.com     # créé s'il n'existe pas
    python3 scripts/seed-restaurant.py --file scripts/restaurants/autre.json

Mots de passe : SAASRESTAURANT_ADMIN_PASSWORD et SAASRESTAURANT_OWNER_PASSWORD, sinon demandés au clavier
(jamais affichés ni écrits).
"""

from __future__ import annotations

import argparse
import getpass
import importlib.util
import json
import os
import re
import subprocess
import sys
import time
import urllib.parse
import urllib.request
from pathlib import Path

DAYS = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"]

# Le client HTTP est celui de seed-plans.py : mêmes erreurs, même gestion de la connexion.
_spec = importlib.util.spec_from_file_location("seed_plans", Path(__file__).with_name("seed-plans.py"))
_seed_plans = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(_seed_plans)
Api = _seed_plans.Api
ApiError = _seed_plans.ApiError


def step(text: str) -> None:
    print(f"\n== {text}")


def done(text: str) -> None:
    print(f"  + {text}")


def kept(text: str) -> None:
    print(f"  = {text}")


def items(page_or_list) -> list:
    return page_or_list["items"] if isinstance(page_or_list, dict) else page_or_list


def all_pages(api: Api, path: str) -> list:
    result, page = [], 1
    separator = "&" if "?" in path else "?"
    while True:
        data = api.call("GET", f"{path}{separator}page={page}&pageSize=100")
        result.extend(data["items"])
        if not data.get("hasMore") or not data["items"]:
            return result
        page += 1


def by_name(rows: list, name: str, key: str = "name"):
    wanted = name.strip().casefold()
    return next((row for row in rows if str(row.get(key, "")).strip().casefold() == wanted), None)


def wait_for_api(base: str, timeout: float = 120.0) -> bool:
    """Attend que l'API réponde sur /health (elle applique ses migrations au démarrage)."""
    url = base.rstrip("/") + "/health"
    deadline = time.monotonic() + timeout
    announced = False
    while time.monotonic() < deadline:
        try:
            with urllib.request.urlopen(url, timeout=5) as response:
                if response.status == 200:
                    return True
        except Exception:
            pass
        if not announced:
            print(f"API pas encore prête ({url}), attente…")
            announced = True
        time.sleep(2)
    return False


def invitation_token(email: str, timeout: float = 45.0) -> str | None:
    """Le jeton du dernier email d'invitation envoyé à `email`, lu dans les logs de l'API (développement uniquement)."""
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        try:
            logs = subprocess.run(
                ["docker", "compose", "logs", "--no-log-prefix", "--since", "10m", "api"],
                capture_output=True, text=True, timeout=20, check=False, cwd=Path(__file__).resolve().parent.parent,
            ).stdout
        except (OSError, subprocess.TimeoutExpired):
            return None
        token = None
        lines = logs.splitlines()
        for index, line in enumerate(lines):
            if "[DEV EMAIL]" in line and f"to {email}".casefold() in line.casefold():
                for following in lines[index:index + 15]:
                    match = re.search(r"accept-invitation\?token=([^\s&\"']+)", following)
                    if match:
                        token = urllib.parse.unquote(match.group(1))
                        break
        if token:
            return token
        time.sleep(3)
    return None


def create_owner(admin: Api, email: str, password: str, spec: dict) -> None:
    """Restaurant créé (s'il manque) et propriétaire invité, puis invitation acceptée avec `password`."""
    step("Compte du propriétaire")
    tenants = admin.call("GET", "/admin/reporting/tenants?page=1&pageSize=100")["items"]
    tenant = by_name(tenants, spec["name"])
    if tenant is None:
        tenant_id = admin.call("POST", "/admin/tenants", {
            "name": spec["name"], "defaultCurrencyCode": spec["currency"], "timeZoneId": spec["timeZone"],
            "pricesIncludeTax": spec["pricesIncludeTax"],
        })["id"]
        done(f'restaurant « {spec["name"]} » créé en essai ({spec["currency"]}, {spec["timeZone"]})')
    else:
        tenant_id = tenant["tenantId"]
    try:
        admin.call("POST", f"/admin/tenants/{tenant_id}/owners", {"email": email})
        done(f"{email} invité comme propriétaire")
    except ApiError as error:
        if error.status not in (409, 422):
            raise
        kept(f"{email} déjà rattaché au restaurant")

    print("  … lecture du lien d'invitation dans les logs de l'API")
    token = invitation_token(email, timeout=15)
    if token is None:
        # Compte resté sans invitation (ancienne version de l'API, ou lien expiré) : la plateforme en renvoie une.
        users = admin.call("GET", f"/admin/users?search={urllib.parse.quote(email)}&page=1&pageSize=20")["items"]
        user = next((u for u in users if u["email"].casefold() == email.casefold()), None)
        if user is not None:
            try:
                admin.call("POST", f'/admin/users/{user["id"]}/invitation')
                done("nouvelle invitation envoyée")
                token = invitation_token(email, timeout=30)
            except ApiError as error:
                print(f"  ! invitation non renvoyée : {error.code or error.status}", file=sys.stderr)
    if token is None:
        print("  Lien introuvable automatiquement. Copiez le jeton (token=...) de l'email « [DEV EMAIL] » des logs de l'API.")
        token = urllib.parse.unquote(input("  Jeton d'invitation : ").strip())
    admin.call("POST", "/auth/accept-invitation", {"token": token, "password": password})
    done(f"invitation acceptée : {email} peut se connecter")


def ensure_restaurant(admin: Api, owner: Api, owner_email: str, spec: dict) -> str:
    step("Restaurant et propriétaire")
    memberships = owner.call("GET", "/me/memberships")
    existing = by_name(memberships, spec["name"], "tenantName")
    if existing:
        kept(f'restaurant « {spec["name"]} » ({existing["tenantId"]}), propriétaire déjà rattaché')
        tenant_id = existing["tenantId"]
    else:
        created = admin.call("POST", "/admin/tenants", {
            "name": spec["name"],
            "defaultCurrencyCode": spec["currency"],
            "timeZoneId": spec["timeZone"],
            "pricesIncludeTax": spec["pricesIncludeTax"],
        })
        tenant_id = created["id"]
        done(f'restaurant « {spec["name"]} » créé en essai ({spec["currency"]}, {spec["timeZone"]})')
        admin.call("POST", f"/admin/tenants/{tenant_id}/owners", {"email": owner_email})
        done(f"propriétaire : {owner_email}")
        memberships = owner.call("GET", "/me/memberships")
        existing = next((m for m in memberships if m["tenantId"] == tenant_id), None)

    if existing and existing["status"] == "Invited":
        owner.call("POST", f"/me/memberships/{tenant_id}/accept")
        done("invitation acceptée")

    # Le jeton du propriétaire porte désormais le restaurant (claim tid) : toutes les routes suivantes agissent pour lui.
    token = owner.call("POST", "/me/tenant", {"tenantId": tenant_id})
    owner.token = token["accessToken"]
    return tenant_id


def ensure_location(api: Api, spec: dict) -> str:
    step("Établissement")
    locations = all_pages(api, "/locations?includeArchived=false")
    location = by_name(locations, spec["name"])
    if location:
        kept(f'« {spec["name"]} »')
        location_id = location["id"]
        status = location["status"]
    else:
        ends_next_day = spec["closes"] <= spec["opens"]
        created = api.call("POST", "/locations", {
            "name": spec["name"],
            "address": spec["address"],
            "seats": spec["seats"],
            "timeZoneId": None,
            "weeklyHours": [{"day": day, "start": spec["opens"], "end": spec["closes"], "endsNextDay": ends_next_day} for day in DAYS],
        })
        location_id = created["id"]
        status = "Draft"
        done(f'« {spec["name"]} » ({spec["seats"]} places, {spec["opens"]}–{spec["closes"]} tous les jours)')
    if status == "Draft":
        api.call("POST", f"/locations/{location_id}/activate")
        done("établissement ouvert")
    elif status == "TemporarilyClosed":
        api.call("POST", f"/locations/{location_id}/reopen")
        done("établissement rouvert")
    return location_id


def ensure_catalog(api: Api, config: dict) -> dict[str, str]:
    step("Taxes et options")
    taxes = api.call("GET", "/catalog/tax-categories")
    tax_id = None
    for tax in config["taxes"]:
        current = next((t for t in taxes if t["code"].upper() == tax["code"].upper()), None)
        if current:
            kept(f'taxe {tax["code"]}')
        else:
            current = {"id": api.call("POST", "/catalog/tax-categories", tax)["id"]}
            done(f'taxe {tax["code"]} ({tax["rate"] * 100:g} %)')
        tax_id = tax_id or current["id"]

    groups = api.call("GET", "/catalog/modifier-groups")
    group_ids: dict[str, str] = {}
    for group in config["optionGroups"]:
        current = by_name(groups, group["name"])
        if current is None:
            group_id = api.call("POST", "/catalog/modifier-groups", {
                "name": group["name"], "minSelections": group["min"], "maxSelections": group["max"],
            })["id"]
            for option, price in group["options"]:
                api.call("POST", f"/catalog/modifier-groups/{group_id}/modifiers", {"name": option, "priceDelta": price})
            done(f'options « {group["name"]} » : {", ".join(o for o, _ in group["options"])}')
        else:
            group_id = current["id"]
            kept(f'options « {group["name"]} »')
        group_ids[group["name"]] = group_id

    step("Produits")
    products = all_pages(api, "/catalog/products")
    product_ids: dict[str, str] = {}
    for product in config["products"]:
        current = by_name(products, product["name"])
        if current is None:
            product_id = api.call("POST", "/catalog/products", {
                "name": product["name"], "basePrice": product["price"], "taxCategoryId": tax_id,
                "description": product.get("description"),
            })["id"]
            for variant, delta in product.get("variants", []):
                api.call("POST", f"/catalog/products/{product_id}/variants", {"name": variant, "priceDelta": delta, "sku": None})
            for group in product.get("groups", []):
                api.call("POST", f"/catalog/products/{product_id}/modifier-groups", {"modifierGroupId": group_ids[group]})
            api.call("POST", f"/catalog/products/{product_id}/activate")
            done(f'{product["name"]} · {product["price"]:g} {config["restaurant"]["currency"]}')
        else:
            product_id = current["id"]
            if current["status"] in ("Draft", "Unavailable"):
                api.call("POST", f"/catalog/products/{product_id}/activate")
                done(f'{product["name"]} remis en vente')
            else:
                kept(product["name"])
        product_ids[product["name"]] = product_id
    return product_ids


def ensure_menu(api: Api, location_id: str, config: dict, product_ids: dict[str, str]) -> None:
    step("Carte")
    spec = config["menu"]
    menus = api.call("GET", f"/catalog/menus?locationId={location_id}")
    current = next((m for m in menus if m["name"].casefold() == spec["name"].casefold() and m["status"] != "Archived"), None)
    if current and current["status"] == "Published":
        kept(f'« {spec["name"]} » déjà publiée')
        return
    menu_id = current["id"] if current else api.call("POST", "/catalog/menus", {"locationId": location_id, "name": spec["name"]})["id"]
    menu = api.call("GET", f"/catalog/menus/{menu_id}")
    for section_name in spec["sections"]:
        section = by_name(menu["sections"], section_name)
        if section is None:
            api.call("POST", f"/catalog/menus/{menu_id}/sections", {"name": section_name})
            menu = api.call("GET", f"/catalog/menus/{menu_id}")
            section = by_name(menu["sections"], section_name)
        for product in (p for p in config["products"] if p["section"] == section_name):
            if product_ids[product["name"]] not in section["productIds"]:
                api.call("POST", f'/catalog/menus/{menu_id}/sections/{section["id"]}/products', {"productId": product_ids[product["name"]]})
        done(f"section « {section_name} »")
    api.call("POST", f"/catalog/menus/{menu_id}/publish")
    done(f'« {spec["name"]} » publiée')


def ensure_kitchen(api: Api, location_id: str, config: dict, product_ids: dict[str, str]) -> None:
    step("Cuisine")
    stations = api.call("GET", f"/kitchen/stations?locationId={location_id}")
    station_ids: dict[str, str] = {}
    for name in config["stations"]:
        current = by_name(stations, name)
        if current is None:
            station_ids[name] = api.call("POST", "/kitchen/stations", {"locationId": location_id, "name": name})["id"]
            done(f"poste « {name} »")
        else:
            station_ids[name] = current["id"]
            kept(f"poste « {name} »")

    default = station_ids[config["defaultStation"]]
    try:
        routing = api.call("GET", f"/kitchen/routings/{location_id}")
    except ApiError as error:
        if error.status != 404:
            raise
        routing = None
    if routing is None:
        api.call("POST", f"/kitchen/routings/{location_id}", {"defaultStationId": default})
        routing = {"routes": []}
        done(f'poste par défaut : {config["defaultStation"]}')
    routed = {route["productId"]: route["stationId"] for route in routing.get("routes", [])}
    count = 0
    for product in config["products"]:
        station = station_ids.get(product.get("station", ""), default)
        product_id = product_ids[product["name"]]
        if station != default and routed.get(product_id) != station:
            api.call("PUT", f"/kitchen/routings/{location_id}/routes/{product_id}", {"stationId": station})
            count += 1
    if count:
        done(f"{count} produit(s) routé(s) vers un poste précis")


def ensure_floor(api: Api, location_id: str, config: dict) -> None:
    step("Salle")
    areas = api.call("GET", f"/floor/dining-areas?locationId={location_id}")
    for spec in config["diningAreas"]:
        area = by_name(areas, spec["name"])
        if area is None:
            area = {"id": api.call("POST", "/floor/dining-areas", {"locationId": location_id, "name": spec["name"]})["id"], "tables": []}
            done(f'salle « {spec["name"]} »')
        else:
            kept(f'salle « {spec["name"]} »')
        labels = {table["label"].casefold() for table in area["tables"]}
        added = [label for label, _ in spec["tables"] if label.casefold() not in labels]
        for label, capacity in spec["tables"]:
            if label.casefold() not in labels:
                api.call("POST", f'/floor/dining-areas/{area["id"]}/tables', {"label": label, "capacity": capacity})
        if added:
            done(f'tables {", ".join(added)}')


def main() -> int:
    parser = argparse.ArgumentParser(description="Crée un restaurant de test complet par l'API.")
    parser.add_argument("--api", default=os.environ.get("SAASRESTAURANT_API", "http://localhost:5080"))
    parser.add_argument("--email", default=os.environ.get("SAASRESTAURANT_ADMIN_EMAIL", "admin@example.com"),
                        help="compte administrateur de la plateforme")
    parser.add_argument("--owner-email", help="compte du propriétaire, créé s'il n'existe pas (par défaut : le compte administrateur)")
    parser.add_argument("--file", default=str(Path(__file__).with_name("restaurants") / "chez-awa.json"))
    args = parser.parse_args()

    config = json.loads(Path(args.file).read_text(encoding="utf-8"))
    owner_email = (args.owner_email or args.email).strip()

    admin_password = os.environ.get("SAASRESTAURANT_ADMIN_PASSWORD") or getpass.getpass(f"Mot de passe de {args.email} : ")
    if not wait_for_api(args.api):
        print(f"API injoignable sur {args.api} : vérifiez `docker compose ps` et `docker compose logs api`.", file=sys.stderr)
        return 1
    admin = Api(args.api)
    try:
        admin.login(args.email, admin_password)
    except ApiError as error:
        print(f"Connexion refusée ({error.status}) : vérifiez l'email et le mot de passe.", file=sys.stderr)
        return 1

    if owner_email.casefold() == args.email.strip().casefold():
        owner_password = admin_password
    else:
        owner_password = os.environ.get("SAASRESTAURANT_OWNER_PASSWORD") or getpass.getpass(
            f"Mot de passe de {owner_email} (choisi ici si le compte n'existe pas encore) : ")
    owner = Api(args.api)
    try:
        try:
            owner.login(owner_email, owner_password)
        except ApiError as error:
            if error.status != 401 or owner_email.casefold() == args.email.strip().casefold():
                raise
            # Compte inconnu (ou sans mot de passe) : il est invité puis activé avec le mot de passe saisi.
            create_owner(admin, owner_email, owner_password, config["restaurant"])
            owner.login(owner_email, owner_password)
    except ApiError as error:
        print(f"\nConnexion du propriétaire impossible ({error.code or error.status}) : {error}", file=sys.stderr)
        print("Si le compte existe déjà avec un autre mot de passe, relancez avec le bon mot de passe.", file=sys.stderr)
        admin.logout()
        return 1
    finally:
        admin_password = owner_password = ""

    started = time.monotonic()
    try:
        tenant_id = ensure_restaurant(admin, owner, owner_email, config["restaurant"])
        location_id = ensure_location(owner, config["location"])
        product_ids = ensure_catalog(owner, config)
        ensure_menu(owner, location_id, config, product_ids)
        ensure_kitchen(owner, location_id, config, product_ids)
        ensure_floor(owner, location_id, config)
    except ApiError as error:
        if error.status == 403 and error.code is None:
            print("\nAccès refusé : le compte administrateur doit figurer dans Authorization:PlatformAdminEmails.", file=sys.stderr)
        else:
            print(f"\nÉchec : {error.code or error.status} {error}", file=sys.stderr)
        return 1
    finally:
        owner.logout()
        admin.logout()

    print(f"\nRestaurant prêt en {time.monotonic() - started:.1f} s ({tenant_id}).")
    print(f"Console du restaurant : http://localhost:3001 — connexion avec {owner_email}, puis « {config['restaurant']['name']} ».")
    return 0


if __name__ == "__main__":
    sys.exit(main())
