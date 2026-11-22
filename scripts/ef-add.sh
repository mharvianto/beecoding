#!/usr/bin/env bash
# Adds a migration for BOTH providers after a model change:  scripts/ef-add.sh SomeName
#   SQLite   -> BeeCoding.Core/Migrations           (what production runs today)
#   Postgres -> BeeCoding.Core.Postgres/Migrations  (BeeCoding.Core.Postgres, loaded when Database:Provider=postgres)
# Needs `dotnet tool install --global dotnet-ef`. No database server is contacted.
set -euo pipefail
name="${1:?usage: scripts/ef-add.sh MigrationName}"
cd "$(dirname "$0")/../BeeCoding"
dotnet build BeeCoding.csproj -v q >/dev/null
dotnet ef migrations add "$name" --project ../BeeCoding.Core --startup-project . --no-build
Database__Provider=postgres ConnectionStrings__Default="Host=localhost;Database=design" \
  dotnet ef migrations add "$name" --project ../BeeCoding.Core.Postgres --startup-project . \
  --output-dir Migrations --namespace BeeCoding.Migrations.Postgres --no-build
