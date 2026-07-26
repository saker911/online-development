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
  && docker compose --env-file "${ENV_FILE}" -f "${COMPOSE_FILE}" ps --status running --quiet \
    | grep -q .; then
  exit 0
fi

git reset --hard "${TARGET_REVISION}"

docker compose \
  --env-file "${ENV_FILE}" \
  -f "${COMPOSE_FILE}" \
  up -d --build --remove-orphans

docker image prune -f

printf '%s\n' "${TARGET_REVISION}" > "${APP_ROOT}/deployed-revision"
