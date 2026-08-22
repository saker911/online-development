#!/usr/bin/env bash
set -euo pipefail

readonly APP_ROOT="/opt/tasareehapp"
readonly APP_DIR="${APP_ROOT}/app"
readonly ENV_FILE="${APP_ROOT}/.env"
readonly COMPOSE_FILE="compose.production.yml"
readonly COMPOSE_PROJECT="repository"
readonly STATUS_FILE="${APP_ROOT}/monitoring-status.json"
readonly TEMP_STATUS_FILE="${STATUS_FILE}.partial"
readonly MAX_DISK_PERCENT="${MONITORING_MAX_DISK_PERCENT:-85}"
readonly MAX_BACKUP_AGE_HOURS="${MONITORING_MAX_BACKUP_AGE_HOURS:-30}"
readonly GATE_OFFLINE_MINUTES="${MONITORING_GATE_OFFLINE_MINUTES:-3}"

umask 077
test -f "${ENV_FILE}"
cd "${APP_DIR}"

status="healthy"
issues=()

mark_issue() {
  status="degraded"
  issues+=("$1")
}

container_health() {
  local service="$1"
  local container_id
  container_id="$(docker compose --project-name "${COMPOSE_PROJECT}" --env-file "${ENV_FILE}" -f "${COMPOSE_FILE}" ps -q "${service}")"
  if [[ -z "${container_id}" ]]; then
    printf '%s' "missing"
    return
  fi
  docker inspect --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}' "${container_id}"
}

app_health="unavailable"
if curl --fail --silent --max-time 10 \
  --header "Host: tasareehapp.com" \
  --header "X-Forwarded-Proto: https" \
  http://127.0.0.1:10000/healthz >/dev/null; then
  app_health="ready"
else
  mark_issue "app_unavailable"
fi

postgres_health="$(container_health postgres)"
[[ "${postgres_health}" == "healthy" ]] || mark_issue "postgres_${postgres_health}"

clamav_health="$(container_health clamav)"
[[ "${clamav_health}" == "healthy" ]] || mark_issue "clamav_${clamav_health}"

disk_percent="$(df -P "${APP_ROOT}" | awk 'NR==2 {gsub(/%/, "", $5); print $5}')"
[[ "${disk_percent}" =~ ^[0-9]+$ ]] || disk_percent=100
(( disk_percent < MAX_DISK_PERCENT )) || mark_issue "disk_usage_high"

latest_backup="$(find "${APP_ROOT}/backups/postgres" -maxdepth 1 -type f -name 'tasareehapp-*.dump' -printf '%T@ %p\n' 2>/dev/null | sort -nr | head -n 1 | cut -d' ' -f2-)"
backup_age_hours=-1
if [[ -n "${latest_backup}" ]]; then
  backup_age_hours="$(( ($(date +%s) - $(stat -c %Y "${latest_backup}")) / 3600 ))"
fi
(( backup_age_hours >= 0 && backup_age_hours <= MAX_BACKUP_AGE_HOURS )) || mark_issue "backup_stale_or_missing"

health_counts="$(
  docker compose --project-name "${COMPOSE_PROJECT}" --env-file "${ENV_FILE}" -f "${COMPOSE_FILE}" exec -T postgres \
    sh -c 'psql -At -F " " -U "$POSTGRES_USER" -d "$POSTGRES_DB"' <<SQL
SELECT
  (SELECT count(*) FROM "DisplayDevices"
   WHERE "Status" = 'Approved'
     AND ("LastSeenUtc" IS NULL OR "LastSeenUtc" < (CURRENT_TIMESTAMP AT TIME ZONE 'UTC') - interval '${GATE_OFFLINE_MINUTES} minutes')),
  (SELECT count(*) FROM "AuditLogs"
   WHERE NOT "Success"
     AND "ActionType" IN ('SystemError', 'UnhandledException')
     AND "OccurredAt" >= (CURRENT_TIMESTAMP AT TIME ZONE 'UTC') - interval '24 hours');
SQL
)"
read -r offline_gate_count system_error_count <<< "${health_counts}"

[[ "${offline_gate_count}" =~ ^[0-9]+$ ]] || offline_gate_count=0
[[ "${system_error_count}" =~ ^[0-9]+$ ]] || system_error_count=0
(( offline_gate_count == 0 )) || mark_issue "gate_devices_offline"
(( system_error_count == 0 )) || mark_issue "recent_system_errors"

checked_at="$(date -u +%FT%TZ)"
issues_csv="$(IFS=,; printf '%s' "${issues[*]:-}")"
cat > "${TEMP_STATUS_FILE}" <<JSON
{"status":"${status}","checkedAtUtc":"${checked_at}","app":"${app_health}","postgres":"${postgres_health}","clamav":"${clamav_health}","diskUsedPercent":${disk_percent},"backupAgeHours":${backup_age_hours},"offlineGateCount":${offline_gate_count},"recentSystemErrorCount":${system_error_count},"issues":"${issues_csv}"}
JSON
mv "${TEMP_STATUS_FILE}" "${STATUS_FILE}"

if [[ "${status}" != "healthy" ]]; then
  if [[ -n "${MONITORING_WEBHOOK_URL:-}" ]]; then
    curl --fail --silent --show-error --max-time 15 \
      --header 'Content-Type: application/json' \
      --data-binary "@${STATUS_FILE}" \
      "${MONITORING_WEBHOOK_URL}" >/dev/null || true
  fi
  printf '%s\n' "Production health is degraded: ${issues_csv}"
  exit 1
fi

printf '%s\n' "Production health is healthy."
