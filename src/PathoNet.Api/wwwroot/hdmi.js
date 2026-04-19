const hdmiUrl = "/api/portal/hdmi";
const refreshIntervalMs = 2000;
const maxRetryIntervalMs = 30000;
const hardReloadIntervalMs = 30 * 60 * 1000;

let refreshTimer;
let clockTimer;
let reloadTimer;
let lastSuccessAt = 0;
let failureCount = 0;
let nextRefreshDueAt = Date.now() + refreshIntervalMs;

async function refreshHdmi() {
    try {
        const response = await fetch(hdmiUrl, { cache: "no-store" });
        if (!response.ok) {
            throw new Error(`HTTP ${response.status}`);
        }

        const state = await response.json();
        renderHdmi(state);
        lastSuccessAt = Date.now();
        failureCount = 0;
        setConnectionState(true, "Polaczono z lokalnym API", "Widok HDMI odswieza dane bez ingerencji operatora.");
        scheduleRefresh(refreshIntervalMs);
    } catch (error) {
        console.error(error);
        failureCount += 1;
        const delay = Math.min(maxRetryIntervalMs, refreshIntervalMs * 2 ** Math.min(failureCount, 4));
        const staleSeconds = lastSuccessAt === 0 ? 0 : Math.round((Date.now() - lastSuccessAt) / 1000);
        const staleLabel = staleSeconds <= 0 ? "brak poprzedniego odczytu" : `${staleSeconds}s od ostatniej odpowiedzi`;
        setConnectionState(false, "Brak polaczenia z lokalnym API", `Auto-reconnect aktywny. Nastepna proba za ${Math.round(delay / 1000)} s (${staleLabel}).`);
        scheduleRefresh(delay);
    }
}

function renderHdmi(state) {
    document.getElementById("hdmi-title").textContent = `${state.clientName} - widok HDMI`;
    document.getElementById("hdmi-headline").textContent = state.headline;
    document.getElementById("hdmi-notify").textContent = state.notificationCount;
    document.getElementById("hdmi-heartbeat").textContent = state.heartbeatCount;
    document.getElementById("hdmi-last-heartbeat").textContent = relativeTime(state.lastHeartbeatAtUtc);
    document.getElementById("hdmi-last-refresh").textContent = new Date().toLocaleTimeString("pl-PL");

    renderHdmiAlerts(state.alerts ?? []);
    renderHdmiTiles(state.tiles ?? []);
}

function setConnectionState(online, title, detail) {
    const banner = document.getElementById("hdmi-connection-banner");
    const overlay = document.getElementById("hdmi-overlay");
    banner.classList.toggle("online", online);
    banner.classList.toggle("offline", !online);
    document.getElementById("hdmi-connection-state").textContent = title;
    document.getElementById("hdmi-connection-meta").textContent = detail;
    document.getElementById("hdmi-overlay-message").textContent = detail;
    overlay.classList.toggle("hidden", online);
}

function scheduleRefresh(delayMs) {
    clearTimeout(refreshTimer);
    nextRefreshDueAt = Date.now() + delayMs;
    updateCountdown();
    refreshTimer = window.setTimeout(refreshHdmi, delayMs);
}

function renderHdmiAlerts(alerts) {
    const container = document.getElementById("hdmi-alerts");
    if (!alerts.length) {
        container.innerHTML = '<div class="alert-card"><strong>Brak aktywnych alertow</strong><p>Ekran oczekuje na ruch ostrzezen lub alarmow.</p></div>';
        return;
    }

    container.innerHTML = alerts
        .map((alert) => `
            <article class="alert-card">
                <div class="status-row">
                    <span class="pill ${escapeHtml(alert.level)}">${escapeHtml(translateLevel(alert.level))}</span>
                    <span class="pill online">${escapeHtml(alert.groupName)}</span>
                </div>
                <strong>${escapeHtml(alert.displayName)} / ${escapeHtml(alert.port)}</strong>
                <p>${escapeHtml(alert.alias)}</p>
                <p>${escapeHtml(alert.summary)}</p>
            </article>
        `)
        .join("");
}

function renderHdmiTiles(tiles) {
    const container = document.getElementById("hdmi-tiles");
    if (!tiles.length) {
        container.innerHTML = '<div class="tile"><strong>Brak urzadzen</strong><p>Brak dostepnego stanu symulowanych urzadzen.</p></div>';
        return;
    }

    container.innerHTML = tiles
        .map((tile) => `
            <article class="tile">
                <div class="status-row">
                    <span class="pill ${escapeHtml(tile.status)}">${escapeHtml(translateStatus(tile.status))}</span>
                    <span class="pill ${escapeHtml(tile.currentLevel)}">${escapeHtml(translateLevel(tile.currentLevel))}</span>
                </div>
                <strong>${escapeHtml(tile.displayName)}</strong>
                <p>${escapeHtml(tile.alias)}</p>
                <p>${escapeHtml(tile.lastMessage)}</p>
                <div class="status-row">
                    <span class="pill online">${escapeHtml(tile.groupName)}</span>
                    <span class="pill online">${escapeHtml(tile.port)}</span>
                    <span class="pill online">${escapeHtml(translateTrend(tile.trend))}</span>
                </div>
                <div class="health">
                    <span style="width:${tile.healthScore}%"></span>
                </div>
            </article>
        `)
        .join("");
}

function relativeTime(value) {
    if (!value) {
        return "jeszcze nie";
    }

    const deltaSeconds = Math.max(0, Math.round((Date.now() - new Date(value).getTime()) / 1000));
    if (deltaSeconds < 5) {
        return "teraz";
    }
    if (deltaSeconds < 60) {
        return `${deltaSeconds}s temu`;
    }

    const deltaMinutes = Math.floor(deltaSeconds / 60);
    if (deltaMinutes < 60) {
        return `${deltaMinutes} min temu`;
    }

    return `${Math.floor(deltaMinutes / 60)} h temu`;
}

function updateClock() {
    document.getElementById("hdmi-clock").textContent = new Date().toLocaleTimeString("pl-PL");
    updateCountdown();
}

function updateCountdown() {
    const remainingMs = Math.max(0, nextRefreshDueAt - Date.now());
    document.getElementById("hdmi-next-refresh").textContent = `${Math.max(1, Math.ceil(remainingMs / 1000))} s`;
}

function translateLevel(level) {
    switch (String(level ?? "").toLowerCase()) {
        case "alarm":
            return "Alarm";
        case "warn":
            return "Ostrzezenie";
        case "error":
            return "Blad";
        default:
            return "Info";
    }
}

function translateStatus(status) {
    switch (String(status ?? "").toLowerCase()) {
        case "critical":
            return "Krytyczny";
        case "attention":
            return "Uwaga";
        default:
            return "Online";
    }
}

function translateTrend(trend) {
    switch (String(trend ?? "").toLowerCase()) {
        case "rising":
            return "rosnacy";
        case "recovering":
            return "stabilizacja";
        default:
            return "staly";
    }
}

function escapeHtml(value) {
    return String(value)
        .replaceAll("&", "&amp;")
        .replaceAll("<", "&lt;")
        .replaceAll(">", "&gt;")
        .replaceAll('"', "&quot;")
        .replaceAll("'", "&#39;");
}

function hardReload() {
    window.location.reload();
}

window.addEventListener("online", () => {
    failureCount = 0;
    setConnectionState(true, "Polaczenie sieciowe wrocilo", "Uruchamianie natychmiastowego odswiezenia danych.");
    scheduleRefresh(250);
});

window.addEventListener("offline", () => {
    setConnectionState(false, "Przegladarka stracila lacznosc", "Tryb kiosku czeka na powrot sieci i ponowi polaczenie.");
});

document.addEventListener("dblclick", async () => {
    if (document.fullscreenElement || !document.documentElement.requestFullscreen) {
        return;
    }

    try {
        await document.documentElement.requestFullscreen();
    } catch (error) {
        console.warn("Nie udalo sie wlaczyc fullscreen.", error);
    }
});

updateClock();
clockTimer = window.setInterval(updateClock, 1000);
reloadTimer = window.setInterval(hardReload, hardReloadIntervalMs);
scheduleRefresh(10);
