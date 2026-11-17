#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

command -v docker >/dev/null 2>&1 || { echo "ERRO: Docker CLI não encontrado." >&2; exit 1; }
command -v openssl >/dev/null 2>&1 || { echo "ERRO: openssl não encontrado para gerar senha temporária." >&2; exit 1; }
docker compose version >/dev/null 2>&1 || { echo "ERRO: Docker Compose v2 não encontrado." >&2; exit 1; }

ENGINE_OS="$(docker info --format '{{.OSType}}')"
if [[ "$ENGINE_OS" != "linux" ]]; then
  echo "ERRO: este teste exige Docker Engine em modo Linux; modo detectado: $ENGINE_OS" >&2
  echo "Não altere o modo do Docker no servidor de produção. Execute em um host Linux isolado ou CI." >&2
  exit 2
fi

export POSTGRES_PASSWORD="$(openssl rand -hex 32)"
PROJECT="atendee-rls-poc-$$"
COMPOSE=(docker compose --project-directory "$ROOT" --project-name "$PROJECT" -f "$ROOT/compose.rls.yaml")

cleanup() {
  "${COMPOSE[@]}" down --volumes --remove-orphans >/dev/null 2>&1 || true
  unset POSTGRES_PASSWORD
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM

echo "Iniciando PostgreSQL 18.7 isolado, sem portas publicadas..."
"${COMPOSE[@]}" up -d --wait --wait-timeout 90

SERVER_VERSION_NUM="$("${COMPOSE[@]}" exec -T postgres psql -U postgres -d postgres -Atqc 'SHOW server_version_num;')"
SERVER_VERSION="$("${COMPOSE[@]}" exec -T postgres psql -U postgres -d postgres -Atqc 'SHOW server_version;')"
if [[ "$SERVER_VERSION_NUM" -lt 180000 || "$SERVER_VERSION_NUM" -ge 190000 ]]; then
  echo "ERRO: esperado PostgreSQL 18.x; detectado $SERVER_VERSION" >&2
  exit 3
fi
printf 'Servidor de teste: PostgreSQL %s (server_version_num=%s)\n' "$SERVER_VERSION" "$SERVER_VERSION_NUM"

"${COMPOSE[@]}" exec -T postgres createdb -U postgres atendee_rls_lab
"${COMPOSE[@]}" exec -T postgres psql -X -v ON_ERROR_STOP=1 -U postgres -d atendee_rls_lab -f /poc/001_create_rls_poc.sql
"${COMPOSE[@]}" exec -T postgres psql -X -v ON_ERROR_STOP=1 -U postgres -d atendee_rls_lab -f /poc/002_test_rls_isolation.sql

echo "PASS: suíte SQL RLS concluída em PostgreSQL $SERVER_VERSION."
echo "O container e seu volume temporário serão removidos agora."
