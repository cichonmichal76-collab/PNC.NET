#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SESSION_SCRIPT="${SCRIPT_DIR}/pathonet-hdmi-session.sh"
XORG_BIN="$(command -v Xorg || true)"

export HOME="${PATHONET_KIOSK_HOME:-/var/lib/pathonet-kiosk}"
mkdir -p "${HOME}"

if [[ ! -x "${SESSION_SCRIPT}" ]]; then
  echo "Missing session script: ${SESSION_SCRIPT}" >&2
  exit 1
fi

if [[ -z "${XORG_BIN}" ]]; then
  XORG_BIN="/usr/lib/xorg/Xorg"
fi

exec xinit "${SESSION_SCRIPT}" -- "${XORG_BIN}" :0 vt7 -keeptty -nolisten tcp
