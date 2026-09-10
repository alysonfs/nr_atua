#!/usr/bin/env bash
# Lista as credenciais existentes na tabela service_credentials (Postgres real,
# via Postgres__ConnectionString). Somente leitura - nunca mostra o token em
# texto plano (isso é impossível: só o hash SHA256 é armazenado, o token
# original nunca é recuperável depois de gerado).
#
# Uso:
#   ./list-collector-credentials.sh [--tenant-id <uuid>]

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=lib-pg-conn.sh
source "$SCRIPT_DIR/lib-pg-conn.sh"

TENANT_ID=""

while [ $# -gt 0 ]; do
  case "$1" in
    --tenant-id) TENANT_ID="$2"; shift 2 ;;
    -h|--help)
      grep '^#' "$0" | sed 's/^# \{0,1\}//'
      exit 0
      ;;
    *) echo "Argumento desconhecido: $1" >&2; exit 1 ;;
  esac
done

WHERE=""
if [ -n "$TENANT_ID" ]; then
  WHERE="WHERE sc.\"TenantId\" = '$TENANT_ID'"
fi

pg_run -c "
  SELECT
    sc.\"Id\",
    t.\"Name\"        AS tenant,
    p.\"Name\"        AS provider,
    sc.\"Scope\"      AS scope,
    left(sc.\"TokenHash\", 8) || '...' AS token_hash_preview,
    CASE WHEN sc.\"RevokedAt\" IS NULL THEN 'ativa' ELSE 'revogada em ' || sc.\"RevokedAt\" END AS status
  FROM service_credentials sc
  JOIN tenants t ON t.\"Id\" = sc.\"TenantId\"
  JOIN integration_providers p ON p.\"Id\" = sc.\"ProviderId\"
  $WHERE
  ORDER BY tenant, scope, status;
"

echo
echo "Nota: o token original NUNCA é recuperável a partir do hash. Se o" \
     "Collector estiver dando 401 e nenhuma credencial 'ativa' acima tiver" \
     "um token conhecido/exportado, use seed-collector-credentials.sh (com" \
     "--revoke-existing) para gerar novas credenciais."
