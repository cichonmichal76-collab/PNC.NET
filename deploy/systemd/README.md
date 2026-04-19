# PathoNet i systemd

Ten katalog zawiera gotowe unity `systemd` dla lokalnego mock stacku:

- `pathonet-api.service`
- `pathonet-hub.service`
- `pathonet-collector.service`
- `pathonet-apisender.service`
- `pathonet-hdmi-kiosk.service`
- `pathonet.target`

## Zalecany layout na Linuxie

Publikacja zaklada domyslny katalog:

```text
/opt/pathonet/current/
  api/
  hub/
  collector/
  apisender/
  kiosk/
```

## Szybkie wdrozenie

1. Opublikuj artefakty:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Publish-PathoNet-Linux.ps1
```

Domyslnie skrypt publikuje teraz pod `linux-arm64`, czyli bezposrednio pod Raspberry Pi CM4 / ARM64. Jesli chcesz zbudowac artefakty dla innej architektury, nadpisz `-Runtime`.

Mozesz tez uzyc skrotu dedykowanego pod CM4:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Publish-PathoNet-CM4.ps1
```

2. Skopiuj katalog `publish/linux-x64` na serwer do `/opt/pathonet/current`.

3. Skopiuj `deploy/systemd/pathonet.env.example` do `/etc/pathonet/pathonet.env` i dopasuj wartosci.

   Dla Raspberry Pi Compute Module 4 z `2 GB RAM` uzyj zamiast tego gotowego profilu:

```bash
sudo cp deploy/systemd/pathonet.env.cm4-2gb.example /etc/pathonet/pathonet.env
```

4. Skopiuj unity:

```bash
sudo cp deploy/systemd/pathonet-*.service /etc/systemd/system/
sudo cp deploy/systemd/pathonet.target /etc/systemd/system/
```

4a. Skopiuj katalog kiosku:

```bash
sudo cp -R deploy/kiosk /opt/pathonet/current/kiosk
sudo chmod +x /opt/pathonet/current/kiosk/*.sh
```

5. Przeladuj `systemd` i uruchom target:

```bash
sudo systemctl daemon-reload
sudo systemctl enable --now pathonet.target
```

## Przydatne komendy

```bash
sudo systemctl status pathonet.target
sudo systemctl status pathonet-api.service
sudo journalctl -u pathonet-api.service -f
sudo journalctl -u pathonet-hub.service -f
sudo journalctl -u pathonet-collector.service -f
sudo journalctl -u pathonet-apisender.service -f
```

## Uwagi

- `PathoNet.Api` slucha na adresach z `PATHONET_HTTP_URLS`, domyslnie `http://0.0.0.0:5000;http://0.0.0.0:5080`.
- Port `5000` jest przeznaczony dla lokalnej maszyny PNC i kiosk/HDMI, a port `5080` dla portalu hosta i zdalnej pracy operatorskiej.
- `pathonet-hdmi-kiosk.service` uruchamia lokalny fullscreen Chromium/Openbox pod adresem z `PATHONET_KIOSK_URL`, domyslnie `http://127.0.0.1:5000/hdmi`.
- `PathoNet.Api`, `Collector`, `Hub` i `ApiSender` maja podlaczony oficjalny provider `Microsoft.Extensions.Hosting.Systemd`, wiec host sam raportuje gotowosc i zamkniecie do `systemd`.
- Unity uzywaja `SIGTERM`, `TimeoutStopSec=20` i osobnych `WorkingDirectory`, zeby publikowane `appsettings.json` byly czytane stabilnie tak samo lokalnie i na Linuxie.
- Unity pracuja jako `Type=notify`, wiec `systemctl` dostaje sygnal `READY=1` dopiero po starcie hosta `.NET`.
- Wszystkie unity maja `WatchdogSec=30`, a warstwa `PathoNet.Infrastructure` wysyla heartbeat `WATCHDOG=1` mniej wiecej co polowe tego czasu.
- Aktualne mockowe adresy i heartbeat pozostaja sterowane przez `appsettings.json` publikowane razem z kazda usluga.
- Kiosk HDMI jest przygotowany pod Raspberry/CM4 i zaklada lekkie pakiety `Xorg + Openbox + Chromium`; gotowy installer jest w `deploy/kiosk/install-pathonet-hdmi-kiosk.sh`.

## Profil CM4 2 GB

Dla `Raspberry Pi Compute Module 4, 2 GB RAM, 16 GB eMMC` zalecany jest nastepujacy tryb pracy:

- lokalnie uruchamiaj:
  - `PathoNet.Api`
  - `PathoNet.Collector`
  - `PathoNet.Hub`
  - `PathoNet.ApiSender`
  - lokalny panel `:5000`
- na serwer wynies:
  - repo OTA
  - ciezka analityke
  - dluga historie zdarzen
  - portal operatorski obslugujacy wiele urzadzen

Jesli zabraknie zapasu RAM, ogranicz lokalnie w tej kolejnosci:

1. Wylacz kiosk HDMI, jesli nie jest potrzebny caly czas:

```bash
sudo systemctl disable --now pathonet-hdmi-kiosk.service
```

2. Uzywaj lokalnego panelu glownie na `:5000`, a ciezsza prace operatorska przenies na host.
3. Nie trzymaj lokalnie repo OTA ani duzych archiwow logow.
4. Zostaw `5000` i `5080`, ale traktuj `5080` jako portal pomocniczy, nie stale otwarty kiosk.
