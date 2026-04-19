# PathoNet HDMI Kiosk

Ten katalog przygotowuje kiosk fullscreen dla monitora HDMI na Raspberry / Ubuntu Server.

## Co instaluje kiosk

- `Xorg`
- `xinit`
- `openbox`
- `unclutter`
- `dbus-x11`
- `Chromium` lub `chromium-browser` (installer wykrywa dostepny wariant)

## Szybka instalacja na urzadzeniu

```bash
sudo chmod +x /opt/pathonet/current/kiosk/*.sh
sudo /opt/pathonet/current/kiosk/install-pathonet-hdmi-kiosk.sh
```

## Co robi installer

1. Instaluje lekkie pakiety wymagane przez kiosk.
2. Tworzy uzytkownika `pathonet`, jesli go jeszcze nie ma.
3. Tworzy katalog domu kiosku `/var/lib/pathonet-kiosk`.
4. Wlacza `pathonet-hdmi-kiosk.service`.

## Konfiguracja

Kiosk czyta zmienne z `/etc/pathonet/pathonet.env`:

- `PATHONET_KIOSK_URL`
- `PATHONET_KIOSK_WIDTH`
- `PATHONET_KIOSK_HEIGHT`
- `PATHONET_KIOSK_BROWSER_FLAGS`

Domyslny adres to:

```text
http://127.0.0.1:5000/hdmi
```

## Przydatne komendy

```bash
sudo systemctl status pathonet-hdmi-kiosk.service
sudo journalctl -u pathonet-hdmi-kiosk.service -f
sudo systemctl restart pathonet-hdmi-kiosk.service
```
