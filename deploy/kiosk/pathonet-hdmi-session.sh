#!/usr/bin/env bash
set -euo pipefail

resolve_browser() {
  if [[ -n "${PATHONET_KIOSK_BROWSER:-}" ]]; then
    printf '%s\n' "${PATHONET_KIOSK_BROWSER}"
    return 0
  fi

  local candidates=(
    /usr/bin/chromium-browser
    /usr/bin/chromium
    /snap/bin/chromium
    /usr/bin/google-chrome
  )

  for candidate in "${candidates[@]}"; do
    if [[ -x "${candidate}" ]]; then
      printf '%s\n' "${candidate}"
      return 0
    fi
  done

  return 1
}

BROWSER_BIN="$(resolve_browser)" || {
  echo "No supported browser found for PathoNet HDMI kiosk." >&2
  exit 1
}

URL="${PATHONET_KIOSK_URL:-http://127.0.0.1:5000/hdmi}"
WIDTH="${PATHONET_KIOSK_WIDTH:-1920}"
HEIGHT="${PATHONET_KIOSK_HEIGHT:-1080}"
EXTRA_FLAGS="${PATHONET_KIOSK_BROWSER_FLAGS:-}"

export DISPLAY="${DISPLAY:-:0}"
export XDG_RUNTIME_DIR="${XDG_RUNTIME_DIR:-/run/user/$(id -u)}"

mkdir -p "${HOME}/.config/openbox"
mkdir -p "${XDG_RUNTIME_DIR}"

xset s off
xset -dpms
xset s noblank

if command -v unclutter >/dev/null 2>&1; then
  unclutter --timeout 0.5 --jitter 8 --root >/dev/null 2>&1 &
fi

openbox-session >/tmp/pathonet-openbox.log 2>&1 &

while true; do
  "${BROWSER_BIN}" \
    --kiosk \
    --app="${URL}" \
    --start-fullscreen \
    --window-size="${WIDTH},${HEIGHT}" \
    --window-position=0,0 \
    --incognito \
    --no-first-run \
    --disable-session-crashed-bubble \
    --disable-infobars \
    --check-for-update-interval=31536000 \
    --overscroll-history-navigation=0 \
    --disable-features=Translate,TranslateUI \
    ${EXTRA_FLAGS}

  sleep 2
done
