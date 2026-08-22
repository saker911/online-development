#!/usr/bin/env bash
set -euo pipefail

# Legacy branch polling is intentionally disabled. Production is deployed only
# by the tested GitHub Actions release workflow.
printf '%s\n' "Legacy pull deployment is disabled; waiting for a tested release."
