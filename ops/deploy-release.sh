#!/usr/bin/env bash
set -euo pipefail

readonly ARCHIVE_PATH="${1:?release archive path is required}"
readonly RELEASE_SHA="${2:?release revision is required}"
readonly APP_ROOT="/opt/tasareehapp"
readonly APP_DIR="${APP_ROOT}/app"
readonly LEGACY_DIR="${APP_ROOT}/repository"
readonly ENV_FILE="${APP_ROOT}/.env"
readonly STAGING_DIR="${APP_ROOT}/.staging-${RELEASE_SHA}"
readonly PREVIOUS_DIR="${APP_ROOT}/.previous-${RELEASE_SHA}"
readonly FAILED_DIR="${APP_ROOT}/.failed-${RELEASE_SHA}"
readonly COMPOSE_FILE="compose.production.yml"
readonly COMPOSE_PROJECT="repository"

umask 077
[[ "${RELEASE_SHA}" =~ ^[0-9a-f]{40}$ ]] || {
  printf '%s\n' "The release revision is invalid."
  exit 1
}
install -d -m 755 "${APP_ROOT}"
exec 9>"${APP_ROOT}/deploy.lock"
flock -n 9 || {
  printf '%s\n' "Another deployment is already running."
  exit 1
}

test -f "${ARCHIVE_PATH}"
test -f "${ENV_FILE}"
rm -rf -- "${STAGING_DIR}" "${PREVIOUS_DIR}" "${FAILED_DIR}"
install -d -m 755 "${STAGING_DIR}"
tar -xzf "${ARCHIVE_PATH}" -C "${STAGING_DIR}"
test -f "${STAGING_DIR}/${COMPOSE_FILE}"
test -f "${STAGING_DIR}/Dockerfile"

wait_for_health() {
  local attempt
  for attempt in {1..30}; do
    if curl --fail --silent --show-error \
      --header "Host: tasareehapp.com" \
      --header "X-Forwarded-Proto: https" \
      http://127.0.0.1:10000/healthz >/dev/null; then
      return 0
    fi
    sleep 2
  done
  return 1
}

restore_previous_release() {
  printf '%s\n' "Release health check failed; restoring previous application release."
  if [[ ! -d "${PREVIOUS_DIR}" ]]; then
    printf '%s\n' "No previous release is available for automatic rollback."
    return 1
  fi

  if [[ -d "${APP_DIR}" ]]; then
    mv "${APP_DIR}" "${FAILED_DIR}"
  fi
  mv "${PREVIOUS_DIR}" "${APP_DIR}"
  cd "${APP_DIR}"
  docker compose --project-name "${COMPOSE_PROJECT}" --env-file "${ENV_FILE}" -f "${COMPOSE_FILE}" up -d --build --remove-orphans
  wait_for_health
}

systemctl disable --now tasareehapp-deploy.timer >/dev/null 2>&1 || true

if [[ -x "${APP_DIR}/ops/backup-postgres.sh" ]]; then
  "${APP_DIR}/ops/backup-postgres.sh"
elif [[ -x "${LEGACY_DIR}/ops/backup-postgres.sh" ]]; then
  "${LEGACY_DIR}/ops/backup-postgres.sh"
fi

if [[ -d "${APP_DIR}" ]]; then
  mv "${APP_DIR}" "${PREVIOUS_DIR}"
elif [[ -d "${LEGACY_DIR}" ]]; then
  cp -a "${LEGACY_DIR}" "${PREVIOUS_DIR}"
fi
mv "${STAGING_DIR}" "${APP_DIR}"

cd "${APP_DIR}"
if ! docker compose --project-name "${COMPOSE_PROJECT}" --env-file "${ENV_FILE}" -f "${COMPOSE_FILE}" up -d --build --remove-orphans; then
  restore_previous_release
  exit 1
fi

if ! wait_for_health; then
  docker compose --project-name "${COMPOSE_PROJECT}" --env-file "${ENV_FILE}" -f "${COMPOSE_FILE}" logs --tail=150 app postgres
  restore_previous_release
  exit 1
fi

docker compose --project-name "${COMPOSE_PROJECT}" --env-file "${ENV_FILE}" -f "${COMPOSE_FILE}" restart caddy
proxy_ready=false
for _ in {1..15}; do
  if curl --fail --silent --show-error \
    --resolve tasareehapp.com:443:127.0.0.1 \
    https://tasareehapp.com/healthz >/dev/null; then
    proxy_ready=true
    break
  fi
  sleep 2
done

if [[ "${proxy_ready}" != "true" ]]; then
  docker compose --project-name "${COMPOSE_PROJECT}" --env-file "${ENV_FILE}" -f "${COMPOSE_FILE}" logs --tail=150 caddy app
  restore_previous_release
  exit 1
fi

install -m 0644 "${APP_DIR}/ops/tasareehapp-backup.service" /etc/systemd/system/tasareehapp-backup.service
install -m 0644 "${APP_DIR}/ops/tasareehapp-backup.timer" /etc/systemd/system/tasareehapp-backup.timer
install -m 0644 "${APP_DIR}/ops/tasareehapp-offsite-backup.service" /etc/systemd/system/tasareehapp-offsite-backup.service
install -m 0644 "${APP_DIR}/ops/tasareehapp-offsite-backup.timer" /etc/systemd/system/tasareehapp-offsite-backup.timer
install -m 0644 "${APP_DIR}/ops/tasareehapp-restore-check.service" /etc/systemd/system/tasareehapp-restore-check.service
install -m 0644 "${APP_DIR}/ops/tasareehapp-restore-check.timer" /etc/systemd/system/tasareehapp-restore-check.timer
install -m 0644 "${APP_DIR}/ops/tasareehapp-monitor.service" /etc/systemd/system/tasareehapp-monitor.service
install -m 0644 "${APP_DIR}/ops/tasareehapp-monitor.timer" /etc/systemd/system/tasareehapp-monitor.timer
systemctl daemon-reload
systemctl enable --now tasareehapp-backup.timer
systemctl enable --now tasareehapp-monitor.timer

if [[ -f "${APP_ROOT}/offsite-backup.env" ]] && command -v restic >/dev/null; then
  systemctl enable --now tasareehapp-offsite-backup.timer
  systemctl enable --now tasareehapp-restore-check.timer
else
  systemctl disable --now tasareehapp-offsite-backup.timer tasareehapp-restore-check.timer >/dev/null 2>&1 || true
  printf '%s\n' "Off-site backup is not configured; local verified backups remain enabled."
fi

printf '%s\n' "${RELEASE_SHA}" > "${APP_ROOT}/deployed-revision"
rm -rf -- "${PREVIOUS_DIR}" "${FAILED_DIR}"
rm -f -- "${ARCHIVE_PATH}" /tmp/deploy-release.sh
docker image prune -f
printf '%s\n' "Deployment ${RELEASE_SHA} completed and passed health checks."
