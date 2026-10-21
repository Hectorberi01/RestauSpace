#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

# Formules et menus composés. À lancer avant de démarrer l'API ; une migration déjà générée est sautée.

add_migration() {
  local project="src/Modules/$1/$1.Infrastructure"
  if ls "$project/Persistence/Migrations/"*"_$2.cs" >/dev/null 2>&1; then
    echo "$1 : $2 déjà générée"
    return
  fi
  dotnet restore "$project"
  dotnet ef migrations add "$2" --project "$project" --output-dir Persistence/Migrations
}

# Étapes d'une formule et produits proposés (catalog.products.formula_steps, jsonb, '[]' par défaut).
add_migration Catalog FormulaSteps

# Composants figés d'une ligne de formule (orders.order_items.components, jsonb, '[]' par défaut).
add_migration Orders FormulaComponents
