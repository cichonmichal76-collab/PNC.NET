const portalStateUrl = "/api/portal/state";
const portalOtaUrl = "/api/portal/ota";
const refreshPagesMs = 4000;
const localMachinePort = 5000;
const hostPortalPort = 5080;

const pageName = document.body.dataset.page;

async function fetchJson(url) {
    const response = await fetch(url, { cache: "no-store" });
    if (!response.ok) {
        throw new Error(`HTTP ${response.status}`);
    }

    return response.json();
}

async function refreshPage() {
    try {
        switch (pageName) {
            case "host": {
                const [state, ota] = await Promise.all([fetchJson(portalStateUrl), fetchJson(portalOtaUrl)]);
                renderHost(state, ota);
                break;
            }
            case "local": {
                const state = await fetchJson(portalStateUrl);
                renderLocal(state);
                break;
            }
            default:
                break;
        }
    } catch (error) {
        console.error(error);
    }
}

function renderHost(state, ota) {
    const { overview, alerts } = state;
    document.getElementById("host-title").textContent = `${overview.clientName} - portal hosta`;
    document.getElementById("host-copy").textContent =
        `Aktywne alerty: ${overview.alertCount}, wezly PNC online: ${overview.pncOnlineCount}/${overview.pncDeviceCount}, zaplanowane OTA: ${ota.summary.scheduledCount}, lokalny pulpit PNC jest odseparowany na porcie ${localMachinePort}.`;
    document.getElementById("host-version").textContent = `wersja ${overview.currentVersion}`;
    document.getElementById("host-last-event").textContent = `ostatnie zdarzenie ${relativeTime(overview.lastNotificationAtUtc)}`;
    document.getElementById("host-status-pill").textContent = translateStatus(overview.lteStatus);
    document.getElementById("host-status-pill").className = `pill ${overview.lteStatus}`;

    renderMetricGrid("host-summary", [
        { label: "Alerty", value: overview.alertCount, footnote: "Otwarte w systemie" },
        { label: "PNC online", value: `${overview.pncOnlineCount}/${overview.pncDeviceCount}`, footnote: "Wezly nadrzedne" },
        { label: "Reguly", value: overview.activeRuleCount, footnote: "Mapowania i eskalacje" },
        { label: "OTA", value: ota.summary.campaignCount, footnote: `zaplanowane ${ota.summary.scheduledCount}` },
        { label: "Lokalny port", value: localMachinePort, footnote: "Pulpit PNC / HDMI" }
    ]);

    const routes = [
        {
            title: "Start Blazor",
            href: "/blazor",
            copy: "Docelowy panel operatorski z dashboardem, rolami i wspolnym frontendem dla hosta.",
            badge: `${overview.pncDeviceCount} wezlow`
        },
        {
            title: "Panel Analizy",
            href: "/blazor/analiza",
            copy: "Historia zdarzen, aktywnosc, predykcja i heurystyki utrzymaniowe.",
            badge: `${state.predictions.length} predykcji`
        },
        {
            title: "Panel Serwisu",
            href: "/blazor/serwis",
            copy: "Kreator podlaczen i workflow wdrozeniowy dla PNC oraz konfiguracji portow komunikacyjnych.",
            badge: `${overview.activeRuleCount} aktywnych mapowan`
        },
        {
            title: "Panel Administracji",
            href: "/blazor/administracja",
            copy: "OTA, zdrowie uslug, reguly, diagnostyka sygnalow i pozostale narzedzia techniczne.",
            badge: `${ota.summary.completedCount} wdrozen OTA`
        },
        {
            title: "Lokalny PNC",
            href: buildPortUrl(localMachinePort, "/blazor"),
            copy: "Osobny pulpit lokalnej maszyny pod urzadzenie, HDMI, zdrowie uslug i konfiguracje wezla PNC.",
            badge: `port ${localMachinePort}`
        },
        {
            title: "Dashboard HDMI",
            href: buildPortUrl(localMachinePort, "/hdmi"),
            copy: "Pelnoekranowy widok stanowiska lokalnego. Domyslnie uzywany razem z portem PNC.",
            badge: `${overview.heartbeatCount} heartbeatow`
        }
    ];

    document.getElementById("host-route-grid").innerHTML = routes
        .map((route) => `
            <article class="route-card">
                <span class="chip info">${escapeHtml(route.badge)}</span>
                <h3>${escapeHtml(route.title)}</h3>
                <p>${escapeHtml(route.copy)}</p>
                <a class="route-link" href="${route.href}" ${route.href.endsWith("/hdmi") || route.href === "/hdmi" ? 'target="_blank" rel="noreferrer"' : ""}>Otworz panel</a>
            </article>
        `)
        .join("");

    renderStackList("host-alerts", alerts.slice(0, 4).map((alert) => `
        <article class="stack-item">
            <div class="pill-row">
                <span class="pill ${escapeHtml(alert.level)}">${escapeHtml(translateLevel(alert.level))}</span>
                <span class="chip info">${escapeHtml(alert.groupName)}</span>
            </div>
            <strong>${escapeHtml(alert.displayName)}</strong>
            <p>${escapeHtml(alert.summary)}</p>
            <p class="detail-copy">${escapeHtml(alert.alias)} / ${escapeHtml(alert.port)} / ${escapeHtml(alert.occurredAt)}</p>
        </article>
    `), "Brak aktywnych alertow.");

    renderStackList("host-ota", ota.campaigns.slice(0, 4).map((campaign) => `
        <article class="stack-item">
            <div class="pill-row">
                <span class="pill ${statusToPill(campaign.status)}">${escapeHtml(translateCampaignStatus(campaign.status))}</span>
                <span class="chip info">${escapeHtml(campaign.packageName)} ${escapeHtml(campaign.packageVersion)}</span>
            </div>
            <strong>${escapeHtml(campaign.title)}</strong>
            <p>${escapeHtml(campaign.summary)}</p>
            <p class="detail-copy">Termin: ${escapeHtml(formatDateTime(campaign.scheduledForUtc))} / cele: ${escapeHtml(campaign.targetLabels.join(", ") || "brak")}</p>
        </article>
    `), "Brak kampanii OTA.");
}

function renderLocal(state) {
    const { overview, pncDevices, lte } = state;
    document.getElementById("local-title").textContent = `${overview.clientName} - pulpit lokalny PNC`;
    document.getElementById("local-copy").textContent =
        `Ten widok jest wystawiony lokalnie na porcie ${localMachinePort}. Wezly PNC online: ${overview.pncOnlineCount}/${overview.pncDeviceCount}, heartbeaty: ${overview.heartbeatCount}, ostatnie zdarzenie ${relativeTime(overview.lastNotificationAtUtc)}.`;
    document.getElementById("local-version").textContent = `wersja ${overview.currentVersion}`;
    document.getElementById("local-last-heartbeat").textContent = `heartbeat ${relativeTime(overview.lastHeartbeatAtUtc)}`;
    document.getElementById("local-status-pill").textContent = translateStatus(overview.lteStatus);
    document.getElementById("local-status-pill").className = `pill ${overview.lteStatus}`;

    renderMetricGrid("local-summary", [
        { label: "PNC online", value: `${overview.pncOnlineCount}/${overview.pncDeviceCount}`, footnote: "Wezly lokalne" },
        { label: "Heartbeaty", value: overview.heartbeatCount, footnote: "Monitoring urzadzenia" },
        { label: "LTE", value: translateStatus(overview.lteStatus), footnote: lte.operatorName },
        { label: "Port lokalny", value: localMachinePort, footnote: "Lokalny pulpit PNC" }
    ]);

    const routes = [
        {
            title: "PNC .NET",
            href: "/blazor/pnc",
            copy: "Konfiguracja wezla, portow i urzadzen podpietych lokalnie do PNC.",
            badge: `${overview.pncDeviceCount} wezlow`
        },
        {
            title: "Zdrowie uslug",
            href: "/blazor/health",
            copy: "Lokalne zdrowie API, collectora, huba i sendera z restartami serwisowymi.",
            badge: `${overview.heartbeatCount} heartbeatow`
        },
        {
            title: "Kreator podlaczenia",
            href: "/blazor/serwis/hardware",
            copy: "Commissioning PNC, dobór portu, profilu komunikacji i zapis konfiguracji collectora.",
            badge: `${overview.alertCount} alertow`
        },
        {
            title: "HDMI",
            href: "/hdmi",
            copy: "Pelnoekranowy kiosk dla monitora lokalnego i sciany operatorskiej.",
            badge: `kiosk @ ${localMachinePort}`
        },
        {
            title: "Portal hosta",
            href: buildPortUrl(hostPortalPort, "/blazor"),
            copy: "Zdalny portal analityczny i serwisowy na osobnym porcie hosta.",
            badge: `port ${hostPortalPort}`
        }
    ];

    document.getElementById("local-route-grid").innerHTML = routes
        .map((route) => `
            <article class="route-card">
                <span class="chip info">${escapeHtml(route.badge)}</span>
                <h3>${escapeHtml(route.title)}</h3>
                <p>${escapeHtml(route.copy)}</p>
                <a class="route-link" href="${route.href}" ${route.href.endsWith("/hdmi") || route.href === "/hdmi" ? 'target="_blank" rel="noreferrer"' : ""}>Otworz panel</a>
            </article>
        `)
        .join("");

    const pncMarkup = (pncDevices ?? [])
        .map((device) => `
            <article class="device-lite">
                <div class="pill-row">
                    <span class="pill ${escapeHtml(device.status)}">${escapeHtml(translateStatus(device.status))}</span>
                    <span class="chip info">${escapeHtml(device.deviceCode)}</span>
                </div>
                <h4>${escapeHtml(device.name)}</h4>
                <p>${escapeHtml(device.location)}</p>
                <div class="chip-row">
                    <span class="chip info">soft ${escapeHtml(device.firmware)}</span>
                    <span class="chip info">sygnal ${escapeHtml(String(device.signalPercent))}%</span>
                    <span class="chip info">RS-232 ${escapeHtml(String(device.rs232Connected))}</span>
                    <span class="chip info">CAN ${escapeHtml(String(device.canConnected))}</span>
                </div>
            </article>
        `)
        .join("");
    document.getElementById("local-pnc").innerHTML = pncMarkup || '<p class="empty-state">Brak wezlow PNC.</p>';

    renderStackList("local-telemetry", [
        `
            <article class="stack-item">
                <strong>${escapeHtml(lte.modemName)}</strong>
                <p>${escapeHtml(lte.summary)}</p>
                <div class="chip-row">
                    <span class="chip info">${escapeHtml(lte.operatorName)}</span>
                    <span class="chip info">APN ${escapeHtml(lte.apn)}</span>
                    <span class="chip info">IP ${escapeHtml(lte.ipAddress)}</span>
                </div>
            </article>
        `,
        `
            <article class="stack-item">
                <strong>Stan radiowy</strong>
                <div class="chip-row">
                    <span class="chip info">RSRP ${escapeHtml(String(lte.rsrpDbm))} dBm</span>
                    <span class="chip info">RSRQ ${escapeHtml(String(lte.rsrqDb))} dB</span>
                    <span class="chip info">SINR ${escapeHtml(String(lte.sinrDb))} dB</span>
                    <span class="chip info">Cell ${escapeHtml(lte.cellId)}</span>
                </div>
                <p class="detail-copy">Ostatni attach ${escapeHtml(formatDateTime(lte.lastAttachAtUtc))}, roaming ${lte.roaming ? "wlaczony" : "wylaczony"}.</p>
            </article>
        `
    ], "Brak danych LTE.");
}

function renderMetricGrid(targetId, cards) {
    const target = document.getElementById(targetId);
    if (!target) {
        return;
    }

    target.innerHTML = cards
        .map((card) => `
            <article class="metric-tile">
                <span>${escapeHtml(card.label)}</span>
                <strong>${escapeHtml(String(card.value))}</strong>
                <small>${escapeHtml(card.footnote)}</small>
            </article>
        `)
        .join("");
}

function renderStackList(targetId, items, emptyMessage) {
    const target = document.getElementById(targetId);
    if (!target) {
        return;
    }

    target.innerHTML = items.length ? items.join("") : `<p class="empty-state">${escapeHtml(emptyMessage)}</p>`;
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

function translateLevel(level) {
    switch (String(level ?? "").toLowerCase()) {
        case "alarm":
            return "Alarm";
        case "warn":
            return "Ostrzezenie";
        case "error":
            return "Blad";
        default:
            return "Informacja";
    }
}

function translateRisk(riskLabel) {
    switch (String(riskLabel ?? "").toLowerCase()) {
        case "high":
            return "Wysokie";
        case "medium":
            return "Srednie";
        default:
            return "Niskie";
    }
}

function translateCampaignStatus(status) {
    switch (String(status ?? "").toLowerCase()) {
        case "completed":
            return "Zakonczona";
        case "partial":
            return "Czesciowa";
        case "failed":
            return "Nieudana";
        default:
            return "Zaplanowana";
    }
}

function statusToPill(status) {
    switch (String(status ?? "").toLowerCase()) {
        case "completed":
            return "online";
        case "partial":
            return "attention";
        case "failed":
            return "critical";
        default:
            return "info";
    }
}

function riskToPill(risk) {
    switch (String(risk ?? "").toLowerCase()) {
        case "high":
            return "alarm";
        case "medium":
            return "attention";
        default:
            return "info";
    }
}

function translateInterfaceType(interfaceType) {
    switch (String(interfaceType ?? "").toLowerCase()) {
        case "rs232":
            return "RS-232";
        case "can":
            return "CAN";
        case "ethernet":
            return "Ethernet";
        case "digital-input":
            return "Digital input";
        case "digital-output":
            return "Digital output";
        default:
            return "Port";
    }
}

function relativeTime(value) {
    if (!value) {
        return "jeszcze nie";
    }

    const deltaSeconds = Math.round((Date.now() - new Date(value).getTime()) / 1000);
    if (Math.abs(deltaSeconds) < 5) {
        return "teraz";
    }

    const suffix = deltaSeconds >= 0 ? "temu" : "od teraz";
    const absoluteSeconds = Math.abs(deltaSeconds);
    if (absoluteSeconds < 60) {
        return `${absoluteSeconds}s ${suffix}`;
    }

    const deltaMinutes = Math.floor(absoluteSeconds / 60);
    if (deltaMinutes < 60) {
        return `${deltaMinutes} min ${suffix}`;
    }

    return `${Math.floor(deltaMinutes / 60)} h ${suffix}`;
}

function formatDateTime(value) {
    if (!value) {
        return "-";
    }

    return new Date(value).toLocaleString("pl-PL", {
        year: "numeric",
        month: "2-digit",
        day: "2-digit",
        hour: "2-digit",
        minute: "2-digit"
    });
}

function escapeHtml(value) {
    return String(value)
        .replaceAll("&", "&amp;")
        .replaceAll("<", "&lt;")
        .replaceAll(">", "&gt;")
        .replaceAll('"', "&quot;")
        .replaceAll("'", "&#39;");
}

function buildPortUrl(port, path) {
    const url = new URL(path, window.location.origin);
    url.protocol = window.location.protocol;
    url.hostname = window.location.hostname;
    url.port = String(port);
    return url.toString();
}

function rewritePortAwareLinks() {
    document.querySelectorAll("[data-target-port][data-target-path]").forEach((link) => {
        const port = Number(link.getAttribute("data-target-port"));
        const path = link.getAttribute("data-target-path") || "/";
        link.setAttribute("href", buildPortUrl(port, path));
    });
}

rewritePortAwareLinks();
refreshPage();
setInterval(refreshPage, refreshPagesMs);
