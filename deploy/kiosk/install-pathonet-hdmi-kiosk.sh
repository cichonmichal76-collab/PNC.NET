#!/usr/bin/env bash
set -euo pipefail

KIOSK_USER="${PATHONET_KIOSK_USER:-pathonet}"
KIOSK_HOME="${PATHONET_KIOSK_HOME:-/var/lib/pathonet-kiosk}"

apt-get update

BASE_PACKAGES=(
  xserver-xorg
  xinit
  x11-xserver-utils
  openbox
  unclutter
  dbus-x11
  fonts-dejavu-core
  curl
)

apt-get install -y --no-install-recommends "${BASE_PACKAGES[@]}"

if apt-cache show chromium-browser >/dev/null 2>&1; then
  apt-get install -y --no-install-recommends chromium-browser
elif apt-cache show chromium >/dev/null 2>&1; then
  apt-get install -y --no-install-recommends chromium
elif command -v snap >/dev/null 2>&1; then
  snap install chromium
else
  echo "Chromium package not found. Install a supported browser manually." >&2
  exit 1
fi

if ! id -u "${KIOSK_USER}" >/dev/null 2>&1; then
  useradd --system --create-home --home-dir "${KIOSK_HOME}" --shell /bin/bash "${KIOSK_USER}"
fi

install -d -m 0755 -o "${KIOSK_USER}" -g "${KIOSK_USER}" "${KIOSK_HOME}"

chmod +x /opt/pathonet/current/kiosk/*.sh
systemctl daemon-reload
systemctl enable --now pathonet-hdmi-kiosk.service

echo "PathoNet HDMI kiosk installed and enabled."
