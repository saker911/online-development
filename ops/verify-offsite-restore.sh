#!/usr/bin/env bash
set -euo pipefail

readonly APP_ROOT="/opt/tasareehapp"
readonly APP_DIR="${APP_ROOT}/app"
readonly ENV_FILE="${APP_ROOT}/.env"
readonly CONFIG_FILE="${OFFSITE_BACKUP_CONFIG:-${APP_ROOT}/offsite-backup.env}"
readonly RESTORE_ROOT="$(mktemp -d "${APP_ROOT}/restore-check.XXXXXX")"
readonly COMPOSE_PROJECT="repository"

umask 077
trap 'rm -rf -- "${RESTORE_ROOT}"' EXIT
test -f "${CONFIG_FILE}"
test -f "${ENV_FILE}"
command -v restic >/dev/null

set -a
source "${CONFIG_FILE}"
set +a

: "${RESTIC_REPOSITORY:?RESTIC_REPOSITORY is required}"
: "${RESTIC_PASSWORD_FILE:?RESTIC_PASSWORD_FILE is required}"
test -f "${RESTIC_PASSWORD_FILE}"

restic check
restic restore latest --tag tasareehapp --tag postgresql --target "${RESTORE_ROOT}"

readonly RESTORED_BACKUP_ROOT="${RESTORE_ROOT}/opt/tasareehapp/backups/postgres"
readonly RESTORED_DUMP="$(find "${RESTORED_BACKUP_ROOT}" -maxdepth 1 -type f -name 'tasareehapp-*.dump' -printf '%T@ %p\n' | sort -nr | head -n 1 | cut -d' ' -f2-)"
test -n "${RESTORED_DUMP}"
test -s "${RESTORED_DUMP}.sha256"

(
  cd "${RESTORED_BACKUP_ROOT}"
  sha256sum --check "$(basename "${RESTORED_DUMP}").sha256"
)

cd "${APP_DIR}"
docker compose \
  --project-name "${COMPOSE_PROJECT}" \
  --env-file "${ENV_FILE}" \
  -f compose.production.yml \
  exec -T postgres pg_restore --list \
  < "${RESTORED_DUMP}" \
  > /dev/null

date -u +%FT%TZ > "${APP_ROOT}/last-restore-check"
printf '%s\n' "Off-site restore verification completed."
