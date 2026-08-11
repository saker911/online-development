#!/usr/bin/env bash
set -euo pipefail

readonly APP_ROOT="/opt/tasareehapp"
readonly REPOSITORY_DIR="${APP_ROOT}/repository"
readonly REPOSITORY_URL="https://github.com/saker911/online-development.git"
readonly BRANCH="online-development"
readonly COMPOSE_FILE="compose.production.yml"
readonly ENV_FILE="${APP_ROOT}/.env"

exec 9>"${APP_ROOT}/deploy.lock"
flock -n 9 || exit 0

if [[ ! -d "${REPOSITORY_DIR}/.git" ]]; then
  git clone \
    --branch "${BRANCH}" \
    --single-branch \
    "${REPOSITORY_URL}" \
    "${REPOSITORY_DIR}"
fi

cd "${REPOSITORY_DIR}"
git fetch --prune origin "${BRANCH}"

readonly TARGET_REVISION="$(git rev-parse "origin/${BRANCH}")"
readonly CURRENT_REVISION="$(git rev-parse HEAD)"

if [[ "${CURRENT_REVISION}" == "${TARGET_REVISION}" ]] \
  && docker compose --env-file "${ENV_FILE}" -f "${COMPOSE_FILE}" ps --status running --quiet app \
    | grep -q . \
  && docker compose --env-file "${ENV_FILE}" -f "${COMPOSE_FILE}" ps --status running --quiet caddy \
    | grep -q .; then
  exit 0
fi

git reset --hard "${TARGET_REVISION}"

docker compose \
  --env-file "${ENV_FILE}" \
  -f "${COMPOSE_FILE}" \
  up -d --build --remove-orphans

app_ready=false
for _ in {1..30}; do
  if curl --fail --silent --show-error \
    --header "Host: tasareehapp.com" \
    --header "X-Forwarded-Proto: https" \
    http://127.0.0.1:10000/healthz >/dev/null; then
    app_ready=true
    break
  fi
  sleep 2
done

if [[ "${app_ready}" != "true" ]]; then
  docker compose \
    --env-file "${ENV_FILE}" \
    -f "${COMPOSE_FILE}" \
    logs --tail=100 app
  exit 1
fi

# Caddy resolves the app container when its configuration loads. Refresh it
# after recreating the app so the proxy never keeps the previous container IP.
docker compose \
  --env-file "${ENV_FILE}" \
  -f "${COMPOSE_FILE}" \
  restart caddy

for _ in {1..15}; do
  if curl --fail --silent --show-error \
    --resolve tasareehapp.com:443:127.0.0.1 \
    https://tasareehapp.com/healthz >/dev/null; then
    proxy_ready=true
    break
  fi
  sleep 2
done

if [[ "${proxy_ready:-false}" != "true" ]]; then
  docker compose \
    --env-file "${ENV_FILE}" \
    -f "${COMPOSE_FILE}" \
    logs --tail=100 caddy app
  exit 1
fi

install -m 0644 \
  "${REPOSITORY_DIR}/ops/tasareehapp-backup.service" \
  /etc/systemd/system/tasareehapp-backup.service
install -m 0644 \
  "${REPOSITORY_DIR}/ops/tasareehapp-backup.timer" \
  /etc/systemd/system/tasareehapp-backup.timer
systemctl daemon-reload
systemctl enable --now tasareehapp-backup.timer

docker image prune -f

printf '%s\n' "${TARGET_REVISION}" > "${APP_ROOT}/deployed-revision"
