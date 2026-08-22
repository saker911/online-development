#!/usr/bin/env bash
set -euo pipefail

readonly APP_ROOT="/opt/tasareehapp"
readonly APP_DIR="${APP_ROOT}/app"
readonly CONFIG_FILE="${OFFSITE_BACKUP_CONFIG:-${APP_ROOT}/offsite-backup.env}"

umask 077
test -f "${CONFIG_FILE}"
command -v restic >/dev/null

set -a
# This root-owned file contains the restic repository and provider credentials.
source "${CONFIG_FILE}"
set +a

: "${RESTIC_REPOSITORY:?RESTIC_REPOSITORY is required}"
: "${RESTIC_PASSWORD_FILE:?RESTIC_PASSWORD_FILE is required}"
test -f "${RESTIC_PASSWORD_FILE}"

readonly BACKUP_PATH="$("${APP_DIR}/ops/backup-postgres.sh")"
readonly CHECKSUM_PATH="${BACKUP_PATH}.sha256"
test -s "${BACKUP_PATH}"
test -s "${CHECKSUM_PATH}"

if ! restic cat config >/dev/null 2>&1; then
  restic init
fi

restic backup \
  --tag tasareehapp \
  --tag postgresql \
  "${BACKUP_PATH}" \
  "${CHECKSUM_PATH}"

restic forget \
  --tag tasareehapp \
  --keep-daily "${RESTIC_KEEP_DAILY:-14}" \
  --keep-weekly "${RESTIC_KEEP_WEEKLY:-8}" \
  --keep-monthly "${RESTIC_KEEP_MONTHLY:-12}" \
  --prune

printf '%s\n' "Encrypted off-site backup completed."
