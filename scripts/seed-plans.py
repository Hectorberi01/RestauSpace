#!/usr/bin/env python3
"""Crée le catalogue des plans d'abonnement par l'API d'administration (aucune écriture SQL directe).

Passer par l'API garde les règles du domaine (validation, devise, droits), l'outbox et le journal d'audit.
Rejouable : un plan actif de même nom, même devise et même intervalle n'est pas recréé ; avec --update, son prix
et ses droits sont alignés sur le fichier.

Usage :
    python3 scripts/seed-plans.py                      # crée ce qui manque
    python3 scripts/seed-plans.py --dry-run            # montre ce qui serait fait, n'écrit rien
    python3 scripts/seed-plans.py --update             # aligne aussi les plans existants
    python3 scripts/seed-plans.py --currency XOF       # une seule devise

Mot de passe : variable SAASRESTAURANT_ADMIN_PASSWORD, sinon demandé au clavier (jamais affiché ni écrit).
"""

from __future__ import annotations

import argparse
import getpass
import json
import os
import sys
import urllib.error
import urllib.request
from pathlib import Path

INTERVALS = {"Monthly": "mois", "Yearly": "an"}


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

    def call(self, method: str, path: str, body: object | None = None):
        data = None if body is None else json.dumps(body).encode()
        request = urllib.request.Request(self.base + path, data=data, method=method)
        request.add_header("Accept", "application/json")
        if data is not None:
            request.add_header("Content-Type", "application/json")
        if self.token:
            request.add_header("Authorization", "Bearer " + self.token)
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

    def login(self, email: str, password: str) -> None:
        tokens = self.call("POST", "/auth/login", {"email": email, "password": password})
        if tokens and tokens.get("twoFactorRequired"):
            # Validation en deux étapes (obligatoire pour le back-office) : code de l'application ou code de secours.
            code = input("Code de validation en deux étapes : ").strip()
            tokens = self.call("POST", "/auth/login/two-factor", {"challenge": tokens["challenge"], "code": code})
        self.token = tokens["accessToken"]
        self.refresh = tokens["refreshToken"]

    def logout(self) -> None:
        if self.refresh:
            try:
                self.call("POST", "/auth/logout", {"refreshToken": self.refresh})
            except ApiError:
                pass


def kind(plan: dict) -> str:
    return plan.get("kind") or "Base"


def key(plan: dict) -> tuple[str, str, str, str]:
    return (plan["name"].strip().casefold(), plan["currencyCode"].upper(), plan["billingInterval"], kind(plan))


def entitlements(plan: dict) -> dict[str, tuple[bool, int | None]]:
    return {e["featureCode"]: (bool(e["enabled"]), e.get("limit")) for e in plan.get("entitlements", [])}


def label(plan: dict) -> str:
    option = "option " if kind(plan) == "AddOn" else ""
    return f'{option}{plan["name"]} ({plan["price"]:g} {plan["currencyCode"]} / {INTERVALS[plan["billingInterval"]]})'


def main() -> int:
    parser = argparse.ArgumentParser(description="Crée les plans d'abonnement par l'API d'administration.")
    parser.add_argument("--api", default=os.environ.get("SAASRESTAURANT_API", "http://localhost:5080"))
    parser.add_argument("--email", default=os.environ.get("SAASRESTAURANT_ADMIN_EMAIL", "admin@example.com"))
    parser.add_argument("--file", default=str(Path(__file__).with_name("plans") / "catalogue.json"))
    parser.add_argument("--currency", help="ne traiter qu'une devise (XOF, EUR...)")
    parser.add_argument("--update", action="store_true", help="aligner prix et droits des plans existants")
    parser.add_argument("--dry-run", action="store_true", help="afficher sans rien écrire")
    args = parser.parse_args()

    wanted = json.loads(Path(args.file).read_text(encoding="utf-8"))["plans"]
    if args.currency:
        wanted = [p for p in wanted if p["currencyCode"].upper() == args.currency.upper()]
    if not wanted:
        print("Aucun plan à traiter.")
        return 0

    password = os.environ.get("SAASRESTAURANT_ADMIN_PASSWORD") or getpass.getpass(f"Mot de passe de {args.email} : ")
    api = Api(args.api)
    try:
        api.login(args.email, password)
    except ApiError as error:
        print(f"Connexion refusée ({error.status}) : vérifiez l'email et le mot de passe.", file=sys.stderr)
        return 1
    finally:
        password = ""

    created = updated = unchanged = failed = 0
    try:
        try:
            existing = api.call("GET", "/admin/billing/plans?activeOnly=true")
        except ApiError as error:
            if error.status == 403:
                print("Accès refusé : ce compte n'est pas dans Authorization:PlatformAdminEmails.", file=sys.stderr)
                return 1
            raise
        by_key = {key(p): p for p in existing}

        for plan in wanted:
            current = by_key.get(key(plan))
            try:
                if current is None:
                    if args.dry_run:
                        print(f"  + à créer      {label(plan)}")
                    else:
                        api.call("POST", "/admin/billing/plans", plan)
                        print(f"  + créé         {label(plan)}")
                    created += 1
                    continue

                changes = []
                if float(current["price"]) != float(plan["price"]):
                    changes.append(("price", plan["price"]))
                have = entitlements(current)
                for code, value in entitlements(plan).items():
                    if have.get(code) != value:
                        changes.append(("entitlement", {"featureCode": code, "enabled": value[0], "limit": value[1]}))

                if not changes:
                    print(f"  = à jour       {label(plan)}")
                    unchanged += 1
                elif not args.update:
                    print(f"  ~ différent    {label(plan)} : {len(changes)} écart(s), relancer avec --update pour aligner")
                    unchanged += 1
                elif args.dry_run:
                    print(f"  ~ à aligner    {label(plan)} : {len(changes)} modification(s)")
                    updated += 1
                else:
                    for kind, value in changes:
                        if kind == "price":
                            api.call("PUT", f'/admin/billing/plans/{current["id"]}/price', {"price": value})
                        else:
                            api.call("PUT", f'/admin/billing/plans/{current["id"]}/entitlements', value)
                    print(f"  ~ aligné       {label(plan)} : {len(changes)} modification(s)")
                    updated += 1
            except ApiError as error:
                print(f"  ! échec        {label(plan)} : {error.code or error.status} {error}", file=sys.stderr)
                failed += 1
    finally:
        api.logout()

    mode = " (simulation, rien n'a été écrit)" if args.dry_run else ""
    print(f"\n{created} créé(s), {updated} aligné(s), {unchanged} inchangé(s), {failed} échec(s){mode}.")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
