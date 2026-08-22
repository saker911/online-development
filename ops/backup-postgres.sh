#!/usr/bin/env bash
set -euo pipefail

readonly APP_ROOT="/opt/tasareehapp"
readonly APPLICATION_DIR="${APP_ROOT}/app"
readonly ENV_FILE="${APP_ROOT}/.env"
readonly COMPOSE_FILE="compose.production.yml"
readonly COMPOSE_PROJECT="repository"
readonly BACKUP_ROOT="${APP_ROOT}/backups/postgres"
readonly RETENTION_DAYS="${BACKUP_RETENTION_DAYS:-30}"
readonly CREATED_AT="$(date -u +%Y%m%d-%H%M%S)"
readonly FINAL_PATH="${BACKUP_ROOT}/tasareehapp-${CREATED_AT}.dump"
readonly TEMP_PATH="${FINAL_PATH}.partial"

umask 077
mkdir -p "${BACKUP_ROOT}"

exec 9>"${APP_ROOT}/backup.lock"
flock -n 9 || exit 0

cd "${APPLICATION_DIR}"

cleanup() {
  rm -f "${TEMP_PATH}"
}
trap cleanup EXIT

docker compose \
  --project-name "${COMPOSE_PROJECT}" \
  --env-file "${ENV_FILE}" \
  -f "${COMPOSE_FILE}" \
  exec -T postgres \
  sh -c 'exec pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" --format=custom --no-owner --no-privileges' \
  > "${TEMP_PATH}"

test -s "${TEMP_PATH}"

docker compose \
  --project-name "${COMPOSE_PROJECT}" \
  --env-file "${ENV_FILE}" \
  -f "${COMPOSE_FILE}" \
  exec -T postgres pg_restore --list \
  < "${TEMP_PATH}" \
  > /dev/null

mv "${TEMP_PATH}" "${FINAL_PATH}"
(
  cd "${BACKUP_ROOT}"
  sha256sum "$(basename "${FINAL_PATH}")" > "$(basename "${FINAL_PATH}").sha256"
)

find "${BACKUP_ROOT}" -type f \
  \( -name 'tasareehapp-*.dump' -o -name 'tasareehapp-*.dump.sha256' \) \
  -mtime "+${RETENTION_DAYS}" -delete

printf '%s\n' "${FINAL_PATH}"
