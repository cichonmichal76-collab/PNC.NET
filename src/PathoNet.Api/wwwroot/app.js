const stateUrl = "/api/portal/state";
const rulebookUrl = "/api/portal/rulebook";
const fleetUrl = "/api/portal/fleet";
const otaUrl = "/api/portal/ota";
const refreshMs = 2000;

let currentView = "overview";
let rulebookState = null;
let rulebookDirty = false;
let rulebookLoading = false;
let fleetState = null;
let fleetDirty = false;
let fleetLoading = false;
let selectedPncId = "";
let otaState = null;
let otaDirty = false;
let otaLoading = false;

const navItems = [...document.querySelectorAll(".nav-item")];
const panels = [...document.querySelectorAll(".view-panel")];
const badgeTargets = {
    overview: document.querySelector('[data-badge="overview"]'),
    alerts: document.querySelector('[data-badge="alerts"]'),
    devices: document.querySelector('[data-badge="devices"]'),
    groups: document.querySelector('[data-badge="groups"]'),
    history: document.querySelector('[data-badge="history"]'),
    prediction: document.querySelector('[data-badge="prediction"]'),
    lte: document.querySelector('[data-badge="lte"]'),
    pnc: document.querySelector('[data-badge="pnc"]'),
    wizard: document.querySelector('[data-badge="wizard"]'),
    board: document.querySelector('[data-badge="board"]'),
    ota: document.querySelector('[data-badge="ota"]'),
    rules: document.querySelector('[data-badge="rules"]')
};

const ruleForm = document.getElementById("rule-form");
const ruleClearButton = document.getElementById("rule-clear");
const rulesList = document.getElementById("rules-list");
const usersList = document.getElementById("users-list");
const userForm = document.getElementById("user-form");
const userClearButton = document.getElementById("user-clear");
const pncForm = document.getElementById("pnc-form");
const pncClearButton = document.getElementById("pnc-clear");
const wizardPncList = document.getElementById("wizard-pnc-list");
const connectionForm = document.getElementById("connection-form");
const connectionClearButton = document.getElementById("connection-clear");
const connectionList = document.getElementById("wizard-connection-list");
const otaPackageForm = document.getElementById("ota-package-form");
const otaPackageClearButton = document.getElementById("ota-package-clear");
const otaCampaignForm = document.getElementById("ota-campaign-form");
const otaCampaignClearButton = document.getElementById("ota-campaign-clear");
const otaPackagesList = document.getElementById("ota-packages");
const otaCampaignsList = document.getElementById("ota-campaigns");
const otaLogList = document.getElementById("ota-log-list");
const otaEmailList = document.getElementById("ota-email-list");

navItems.forEach((item) => {
    item.addEventListener("click", () => setView(item.dataset.view));
});

ruleForm.addEventListener("submit", onRuleFormSubmit);
ruleForm.addEventListener("input", markRulebookDirty);
ruleForm.addEventListener("change", markRulebookDirty);
ruleClearButton.addEventListener("click", () => resetRuleForm());
rulesList.addEventListener("click", onRulesListClick);

userForm.addEventListener("submit", onUserFormSubmit);
userForm.addEventListener("input", markRulebookDirty);
userForm.addEventListener("change", markRulebookDirty);
userClearButton.addEventListener("click", () => resetUserForm());
usersList.addEventListener("click", onUsersListClick);

pncForm.addEventListener("submit", onPncFormSubmit);
pncForm.addEventListener("input", markFleetDirty);
pncForm.addEventListener("change", markFleetDirty);
pncClearButton.addEventListener("click", () => resetPncForm(true));
wizardPncList.addEventListener("click", onWizardPncListClick);

connectionForm.addEventListener("submit", onConnectionFormSubmit);
connectionForm.addEventListener("input", markFleetDirty);
connectionForm.addEventListener("change", markFleetDirty);
connectionClearButton.addEventListener("click", () => resetConnectionForm(true));
connectionList.addEventListener("click", onConnectionListClick);

otaPackageForm.addEventListener("submit", onOtaPackageFormSubmit);
otaPackageForm.addEventListener("input", markOtaDirty);
otaPackageForm.addEventListener("change", markOtaDirty);
otaPackageClearButton.addEventListener("click", () => resetOtaPackageForm());
otaPackagesList.addEventListener("click", onOtaPackagesListClick);

otaCampaignForm.addEventListener("submit", onOtaCampaignFormSubmit);
otaCampaignForm.addEventListener("input", markOtaDirty);
otaCampaignForm.addEventListener("change", markOtaDirty);
otaCampaignClearButton.addEventListener("click", () => resetOtaCampaignForm());
otaCampaignsList.addEventListener("click", onOtaCampaignsListClick);

async function refreshPortal() {
    try {
        const response = await fetch(stateUrl, { cache: "no-store" });
        if (!response.ok) {
            throw new Error(`HTTP ${response.status}`);
        }

        const state = await response.json();
        renderPortal(state);
        setPortalStatus("online", "Portal online", "Dane na zywo naplywaja z lokalnego pipeline'u mock.");

        if (currentView === "rules") {
            await refreshRulebook();
        }

        if (currentView === "wizard" || currentView === "board" || currentView === "ota") {
            await refreshFleet();
        }

        if (currentView === "ota") {
            await refreshOta();
        }
    } catch (error) {
        console.error(error);
        setPortalStatus("critical", "Portal offline", "Nie udalo sie pobrac stanu portalu z backendu.");
    }
}

async function refreshRulebook(force = false) {
    if (rulebookLoading || (rulebookDirty && !force)) {
        return;
    }

    rulebookLoading = true;
    try {
        const response = await fetch(rulebookUrl, { cache: "no-store" });
        if (!response.ok) {
            throw new Error(`HTTP ${response.status}`);
        }

        const state = await response.json();
        renderRulebook(state);
        setRulebookStatus("Rulebook zsynchronizowany z backendem.");
    } catch (error) {
        console.error(error);
        setRulebookStatus("Nie udalo sie pobrac definicji regul.");
    } finally {
        rulebookLoading = false;
    }
}

async function refreshFleet(force = false) {
    if (fleetLoading || (fleetDirty && !force)) {
        return;
    }

    fleetLoading = true;
    try {
        const response = await fetch(fleetUrl, { cache: "no-store" });
        if (!response.ok) {
            throw new Error(`HTTP ${response.status}`);
        }

        const state = await response.json();
        renderFleet(state);
        setFleetStatus("Kreator PNC zsynchronizowany z backendem.");
    } catch (error) {
        console.error(error);
        setFleetStatus("Nie udalo sie pobrac konfiguracji floty.");
    } finally {
        fleetLoading = false;
    }
}

async function refreshOta(force = false) {
    if (otaLoading || (otaDirty && !force)) {
        return;
    }

    otaLoading = true;
    try {
        const response = await fetch(otaUrl, { cache: "no-store" });
        if (!response.ok) {
            throw new Error(`HTTP ${response.status}`);
        }

        const state = await response.json();
        renderOta(state);
        setOtaStatus("Panel OTA zsynchronizowany z backendem.");
    } catch (error) {
        console.error(error);
        setOtaStatus("Nie udalo sie pobrac stanu OTA.");
    } finally {
        otaLoading = false;
    }
}

function setView(viewName) {
    currentView = viewName;
    navItems.forEach((item) => item.classList.toggle("active", item.dataset.view === viewName));
    panels.forEach((panel) => panel.classList.toggle("active", panel.dataset.panel === viewName));

    if (viewName === "rules") {
        refreshRulebook();
        return;
    }

    if (viewName === "wizard" || viewName === "board") {
        refreshFleet();
        return;
    }

    if (viewName === "ota") {
        refreshFleet();
        refreshOta();
    }
}

function renderPortal(state) {
    const { overview, roadmap, roles, devices, alerts, groups, activity, history, predictions, lte, pncDevices, mainboards } = state;

    document.getElementById("hero-title").textContent = `${overview.clientName} - centrum monitoringu`;
    document.getElementById("hero-copy").textContent =
        `Portal do zdalnego monitoringu, diagnostyki, grupowania i utrzymania predykcyjnego. Najwyzszy poziom: ${translateLevel(overview.worstLevel)}.`;
    document.getElementById("hero-version").textContent = overview.currentVersion;
    document.getElementById("hero-last-event").textContent = relativeTime(overview.lastNotificationAtUtc);
    document.getElementById("hero-last-heartbeat").textContent = relativeTime(overview.lastHeartbeatAtUtc);

    renderSummary(overview, mainboards);
    renderRoadmap(roadmap);
    renderRoles(roles);
    renderPipeline(overview);
    renderAlertList(alerts);
    renderDevices(devices);
    renderGroups(groups);
    renderActivity(activity);
    renderHistory(history);
    renderPredictions(predictions);
    renderOverviewPredictions(predictions);
    renderOverviewLte(lte);
    renderOverviewPnc(pncDevices);
    renderLte(lte);
    renderPncDevices(pncDevices);
    renderMainboardSummary(mainboards);
    renderBoardMonitor(mainboards);
    updateBadges(overview, devices, alerts, groups, history, predictions, lte, pncDevices, mainboards);
}

function renderSummary(overview, mainboards) {
    const summary = document.getElementById("summary-cards");
    const boardsRequiringAttention = (mainboards ?? []).filter((board) => board.status !== "online").length;
    const cards = [
        {
            label: "Powiadomienia",
            value: overview.notificationCount,
            footnote: "Zdarzenia przyjete przez lokalny backend"
        },
        {
            label: "Heartbeaty",
            value: overview.heartbeatCount,
            footnote: "Ramki rejestracyjne collectora"
        },
        {
            label: "Porty edge",
            value: overview.activeDeviceCount,
            footnote: `Grupy aktywne: ${overview.activeGroupCount}`
        },
        {
            label: "Reguly biznesowe",
            value: overview.activeRuleCount,
            footnote: "Mapowania i progi eskalacji aktywne w GUI"
        },
        {
            label: "Flota PNC",
            value: `${overview.pncOnlineCount}/${overview.pncDeviceCount}`,
            footnote: "Urzadzenia online / wszystkie zdefiniowane"
        },
        {
            label: "Lacznosc LTE",
            value: translateStatus(overview.lteStatus),
            footnote: `${overview.lteOperator} | otwarte alerty: ${overview.alertCount}`
        },
        {
            label: "Plyty glowne",
            value: `${boardsRequiringAttention}/${mainboards?.length ?? 0}`,
            footnote: "Wezly wymagajace obserwacji / wszystkie PNC"
        }
    ];

    summary.innerHTML = cards
        .map((card) => `
            <article class="metric-card">
                <span>${escapeHtml(card.label)}</span>
                <strong>${escapeHtml(String(card.value))}</strong>
                <small>${escapeHtml(card.footnote)}</small>
            </article>
        `)
        .join("");
}

function renderRoadmap(roadmap) {
    document.getElementById("roadmap-grid").innerHTML = roadmap
        .map((phase) => `
            <article class="stack-item">
                <div class="status-row">
                    <span class="chip">${escapeHtml(phase.phase)}</span>
                    <span class="level-pill ${mapRoadmapStatus(phase.status)}">${escapeHtml(translateRoadmapStatus(phase.status))}</span>
                </div>
                <strong>${escapeHtml(phase.title)}</strong>
                <p>${escapeHtml(phase.summary)}</p>
            </article>
        `)
        .join("");
}

function renderRoles(roles) {
    document.getElementById("roles-grid").innerHTML = roles
        .map((role) => `
            <article class="stack-item">
                <strong>${escapeHtml(role.name)}</strong>
                <p>${escapeHtml(role.focus)}</p>
                <div class="chip-row">
                    <span class="chip">default: ${escapeHtml(role.defaultView)}</span>
                    ${role.capabilities.map((item) => `<span class="chip">${escapeHtml(item)}</span>`).join("")}
                </div>
            </article>
        `)
        .join("");
}

function renderPipeline(overview) {
    const stages = [
        { name: "Collector", text: "Mock akwizycji danych z interfejsow szeregowych", live: overview.notificationCount > 0 },
        { name: "Hub", text: "Lokalny ingest i dystrybucja zdarzen", live: overview.notificationCount > 0 },
        { name: "ApiSender", text: "Most HTTP do backendu", live: overview.notificationCount > 0 },
        { name: "Backend", text: "Stan portalu, rulebook i diagnostyki", live: overview.heartbeatCount >= 0 }
    ];

    document.getElementById("pipeline-grid").innerHTML = stages
        .map((stage) => `
            <article class="pipeline-item ${stage.live ? "live" : ""}">
                <strong>${escapeHtml(stage.name)}</strong>
                <p>${escapeHtml(stage.text)}</p>
                <span class="status-pill ${stage.live ? "online" : "attention"}">${stage.live ? "aktywny" : "oczekiwanie"}</span>
            </article>
        `)
        .join("");
}

function renderAlertList(alerts) {
    const container = document.getElementById("alerts-list");
    if (!alerts.length) {
        container.innerHTML = '<p class="empty-state">Do portalu nie dotarly jeszcze zadne alerty.</p>';
        return;
    }

    container.innerHTML = alerts
        .map((alert) => `
            <article class="stack-item">
                <div class="status-row">
                    <span class="level-pill ${escapeHtml(alert.level)}">${escapeHtml(translateLevel(alert.level))}</span>
                    <span class="chip">${escapeHtml(alert.groupName)}</span>
                    ${alert.thresholdReached ? '<span class="level-pill alarm">Prog przekroczony</span>' : ""}
                </div>
                <strong>${escapeHtml(alert.displayName)}</strong>
                <p class="detail-copy">${escapeHtml(alert.alias)} na ${escapeHtml(alert.port)}</p>
                <p>${escapeHtml(alert.summary)}</p>
                <div class="chip-row">
                    <span class="chip">${escapeHtml(alert.occurredAt)}</span>
                    <span class="chip">${escapeHtml(alert.action)}</span>
                    ${alert.escalationSummary ? `<span class="chip">${escapeHtml(alert.escalationSummary)}</span>` : ""}
                </div>
            </article>
        `)
        .join("");
}

function renderDevices(devices) {
    const container = document.getElementById("devices-grid");
    if (!devices.length) {
        container.innerHTML = '<p class="empty-state">Brak dostepnych urzadzen.</p>';
        return;
    }

    container.innerHTML = devices
        .map((device) => `
            <article class="device-card">
                <div class="status-row">
                    <span class="status-pill ${escapeHtml(device.status)}">${escapeHtml(translateStatus(device.status))}</span>
                    <span class="level-pill ${escapeHtml(device.currentLevel)}">${escapeHtml(translateLevel(device.currentLevel))}</span>
                    ${device.thresholdReached ? '<span class="level-pill alarm">Eskalacja</span>' : ""}
                </div>
                <h4>${escapeHtml(device.displayName)}</h4>
                <p class="detail-copy">${escapeHtml(device.alias)} / ${escapeHtml(device.port)}</p>
                <p>${escapeHtml(device.lastMessage)}</p>
                <div class="device-meta">
                    <span class="chip">${escapeHtml(device.groupName)}</span>
                    <span class="chip">trend: ${escapeHtml(translateTrend(device.trend))}</span>
                    ${device.ruleName ? `<span class="chip">regula: ${escapeHtml(device.ruleName)}</span>` : ""}
                </div>
                <div class="device-meta">
                    <span class="chip">zdarzenia: ${escapeHtml(String(device.totalEvents))}</span>
                    <span class="chip">warn: ${escapeHtml(String(device.warnCount))}</span>
                    <span class="chip">alarm: ${escapeHtml(String(device.alarmCount))}</span>
                </div>
                <div class="health-bar">
                    <span style="width:${device.healthScore}%"></span>
                </div>
                <p class="muted-copy">Wynik zdrowia ${escapeHtml(String(device.healthScore))}. ${escapeHtml(device.recommendation)}</p>
                ${device.escalationSummary ? `<p class="subtle-copy">${escapeHtml(device.escalationSummary)}</p>` : ""}
            </article>
        `)
        .join("");
}

function renderGroups(groups) {
    const container = document.getElementById("groups-grid");
    if (!groups.length) {
        container.innerHTML = '<p class="empty-state">Brak dostepnych grup.</p>';
        return;
    }

    container.innerHTML = groups
        .map((group) => `
            <article class="group-card">
                <div class="status-row">
                    <span class="level-pill ${escapeHtml(group.worstLevel)}">${escapeHtml(translateLevel(group.worstLevel))}</span>
                    <span class="chip">${escapeHtml(String(group.deviceCount))} urzadzen</span>
                </div>
                <h4>${escapeHtml(group.name)}</h4>
                <p>${escapeHtml(group.summary)}</p>
                <div class="group-meta">
                    <span class="chip">zdrowie ${escapeHtml(String(group.averageHealth))}</span>
                    <span class="chip">sygnaly ${escapeHtml(String(group.alertCount))}</span>
                </div>
                <div class="member-list">
                    ${group.members.map((member) => `<span class="member-pill">${escapeHtml(member)}</span>`).join("")}
                </div>
            </article>
        `)
        .join("");
}

function renderActivity(activity) {
    const container = document.getElementById("activity-bars");
    if (!activity.length) {
        container.innerHTML = '<p class="empty-state">Brak danych aktywnosci.</p>';
        return;
    }

    container.innerHTML = activity
        .map((bucket) => {
            const height = 52 + (bucket.alarmCount * 34) + (bucket.warnCount * 18) + (bucket.count * 10);
            return `
                <div class="activity-bar">
                    <div class="activity-column ${escapeHtml(bucket.worstLevel)}" style="height:${height}px"></div>
                    <span>${escapeHtml(bucket.label)}</span>
                </div>
            `;
        })
        .join("");
}

function renderHistory(history) {
    const body = document.getElementById("history-body");
    if (!history.length) {
        body.innerHTML = '<tr><td colspan="5" class="empty-row">Brak historii.</td></tr>';
        return;
    }

    body.innerHTML = history
        .map((event) => `
            <tr>
                <td><span class="level-pill ${escapeHtml(event.level)}">${escapeHtml(translateLevel(event.level))}</span></td>
                <td class="mono">${escapeHtml(event.port)}</td>
                <td>
                    <strong>${escapeHtml(event.displayName)}</strong>
                    <div class="subtle-copy">${escapeHtml(event.alias)}</div>
                </td>
                <td>${escapeHtml(event.message)}</td>
                <td>${escapeHtml(event.occurredAt)}</td>
            </tr>
        `)
        .join("");
}

function renderPredictions(predictions) {
    const container = document.getElementById("prediction-grid");
    if (!predictions.length) {
        container.innerHTML = '<p class="empty-state">Model predykcyjny czeka na dane urzadzen.</p>';
        return;
    }

    container.innerHTML = predictions
        .map((prediction) => `
            <article class="prediction-card">
                <div class="status-row">
                    <span class="level-pill ${riskToLevel(prediction.riskLabel)}">${escapeHtml(translateRisk(prediction.riskLabel))}</span>
                    <span class="chip mono">${escapeHtml(prediction.port)}</span>
                </div>
                <h4>${escapeHtml(prediction.displayName)}</h4>
                <p class="detail-copy">${escapeHtml(prediction.alias)}</p>
                <p>${escapeHtml(prediction.summary)}</p>
                <div class="prediction-meta">
                    <span class="chip">${escapeHtml(String(prediction.probability))}% prawdopodobienstwa</span>
                    <span class="chip">${escapeHtml(prediction.horizon)}</span>
                </div>
                <p class="muted-copy">${escapeHtml(prediction.recommendation)}</p>
            </article>
        `)
        .join("");
}

function renderOverviewPredictions(predictions) {
    const container = document.getElementById("overview-predictions");
    if (!predictions.length) {
        container.innerHTML = '<p class="empty-state">Tutaj pojawia sie sygnaly predykcyjne.</p>';
        return;
    }

    container.innerHTML = predictions
        .slice(0, 3)
        .map((prediction) => `
            <article class="stack-item">
                <div class="status-row">
                    <span class="level-pill ${riskToLevel(prediction.riskLabel)}">${escapeHtml(translateRisk(prediction.riskLabel))}</span>
                    <span class="chip">${escapeHtml(prediction.horizon)}</span>
                </div>
                <strong>${escapeHtml(prediction.displayName)}</strong>
                <p class="detail-copy">${escapeHtml(prediction.alias)}</p>
                <p>${escapeHtml(prediction.summary)}</p>
            </article>
        `)
        .join("");
}

function renderOverviewLte(lte) {
    const container = document.getElementById("overview-lte");
    if (!lte) {
        container.innerHTML = '<p class="empty-state">Brak danych o lacznosci LTE.</p>';
        return;
    }

    container.innerHTML = `
        <article class="stack-item">
            <div class="status-row">
                <span class="status-pill ${escapeHtml(lte.status)}">${escapeHtml(translateStatus(lte.status))}</span>
                <span class="chip">${escapeHtml(lte.simSlot)}</span>
                <span class="chip">${escapeHtml(lte.networkType)}</span>
            </div>
            <strong>${escapeHtml(lte.operatorName)}</strong>
            <p>${escapeHtml(lte.summary)}</p>
            <div class="chip-row">
                <span class="chip">sygnal ${escapeHtml(String(lte.signalPercent))}% / ${escapeHtml(String(lte.signalDbm))} dBm</span>
                <span class="chip">${escapeHtml(lte.signalQuality)}</span>
                <span class="chip">SIM ${escapeHtml(lte.simNumber)}</span>
            </div>
        </article>
    `;
}

function renderOverviewPnc(pncDevices) {
    const container = document.getElementById("overview-pnc");
    if (!pncDevices?.length) {
        container.innerHTML = '<p class="empty-state">Brak danych o flocie PNC.</p>';
        return;
    }

    container.innerHTML = pncDevices
        .slice(0, 3)
        .map((device) => `
            <article class="stack-item">
                <div class="status-row">
                    <span class="status-pill ${escapeHtml(device.status)}">${escapeHtml(translateStatus(device.status))}</span>
                    <span class="chip">${escapeHtml(device.deviceCode)}</span>
                    <span class="chip">${escapeHtml(device.location)}</span>
                </div>
                <strong>${escapeHtml(device.name)}</strong>
                <p>${escapeHtml(device.summary)}</p>
                <div class="chip-row">
                    <span class="chip">LTE ${escapeHtml(String(device.signalPercent))}%</span>
                    <span class="chip">RS-232 ${escapeHtml(String(device.rs232Connected))}</span>
                    <span class="chip">CAN ${escapeHtml(String(device.canConnected))}</span>
                    <span class="chip">${escapeHtml(device.firmware)}</span>
                </div>
            </article>
        `)
        .join("");
}

function renderLte(lte) {
    const container = document.getElementById("lte-panel");
    if (!lte) {
        container.innerHTML = '<p class="empty-state">Brak danych LTE.</p>';
        return;
    }

    const metrics = [
        { label: "Operator", value: lte.operatorName, footnote: lte.networkType },
        { label: "Sygnal", value: `${lte.signalPercent}%`, footnote: `${lte.signalDbm} dBm / ${lte.signalQuality}` },
        { label: "Pobieranie", value: `${lte.downloadMbps} Mb/s`, footnote: `wysylanie ${lte.uploadMbps} Mb/s` },
        { label: "Adres IP", value: lte.ipAddress, footnote: `ostatnia probka ${formatDateTime(lte.sampledAtUtc)}` },
        { label: "Rejestracja", value: lte.registrationStatus, footnote: `${lte.plmn} / MCC-MNC ${lte.mccMnc}` },
        { label: "Radiowe KPI", value: `RSRP ${lte.rsrpDbm} dBm`, footnote: `RSRQ ${lte.rsrqDb} dB / SINR ${lte.sinrDb} dB` }
    ];

    container.innerHTML = `
        <div class="summary-grid compact-grid">
            ${metrics.map((metric) => `
                <article class="metric-card">
                    <span>${escapeHtml(metric.label)}</span>
                    <strong>${escapeHtml(metric.value)}</strong>
                    <small>${escapeHtml(metric.footnote)}</small>
                </article>
            `).join("")}
        </div>
        <div class="panel-grid two-up">
            <article class="stack-item">
                <strong>Parametry modemu i karty SIM</strong>
                <p>${escapeHtml(lte.summary)}</p>
                <div class="chip-row">
                    <span class="chip">${escapeHtml(lte.modemName)}</span>
                    <span class="chip">${escapeHtml(lte.simSlot)}</span>
                    <span class="chip">MSISDN ${escapeHtml(lte.simNumber)}</span>
                </div>
                <div class="chip-row">
                    <span class="chip">ICCID ${escapeHtml(lte.iccid)}</span>
                    <span class="chip">IMSI ${escapeHtml(lte.imsi)}</span>
                    <span class="chip">IMEI ${escapeHtml(lte.imei)}</span>
                </div>
            </article>
            <article class="stack-item">
                <strong>Siec GSM/LTE</strong>
                <p>W tym mocku karta SIM jest wpieta do gniazda na plycie glownej i raportuje parametry radiowe, rejestracje, DNS i stan operatora do portalu.</p>
                <div class="chip-row">
                    <span class="chip">APN ${escapeHtml(lte.apn)}</span>
                    <span class="chip">komorka ${escapeHtml(lte.cellId)}</span>
                    <span class="chip">${escapeHtml(translateStatus(lte.status))}</span>
                </div>
                <div class="chip-row">
                    <span class="chip">PIN ${escapeHtml(lte.pinState)}</span>
                    <span class="chip">SMSC ${escapeHtml(lte.smsc)}</span>
                    <span class="chip">TAC ${escapeHtml(lte.tac)}</span>
                    <span class="chip">${lte.roaming ? "roaming" : "bez roamingu"}</span>
                </div>
                <div class="chip-row">
                    <span class="chip">DNS ${escapeHtml(lte.dnsPrimary)}</span>
                    <span class="chip">DNS ${escapeHtml(lte.dnsSecondary)}</span>
                    <span class="chip">attach ${escapeHtml(relativeTime(lte.lastAttachAtUtc))}</span>
                </div>
            </article>
        </div>
    `;
}

function renderPncDevices(pncDevices) {
    const container = document.getElementById("pnc-grid");
    if (!pncDevices?.length) {
        container.innerHTML = '<p class="empty-state">Brak danych o urzadzeniach PNC.</p>';
        return;
    }

    container.innerHTML = pncDevices
        .map((device) => `
            <article class="device-card">
                <div class="status-row">
                    <span class="status-pill ${escapeHtml(device.status)}">${escapeHtml(translateStatus(device.status))}</span>
                    <span class="chip">${escapeHtml(device.deviceCode)}</span>
                    <span class="chip">${escapeHtml(device.location)}</span>
                </div>
                <h4>${escapeHtml(device.name)}</h4>
                <p>${escapeHtml(device.summary)}</p>
                <div class="device-meta">
                    <span class="chip">${escapeHtml(device.operatorName)}</span>
                    <span class="chip">${escapeHtml(device.networkType)}</span>
                    <span class="chip">SIM ${escapeHtml(device.simNumber)}</span>
                </div>
                <div class="device-meta">
                    <span class="chip">sygnal ${escapeHtml(String(device.signalPercent))}%</span>
                    <span class="chip">${escapeHtml(String(device.signalDbm))} dBm</span>
                    <span class="chip">${escapeHtml(device.signalQuality)}</span>
                </div>
                <div class="device-meta">
                    <span class="chip">RS-232 ${escapeHtml(String(device.rs232Connected))}</span>
                    <span class="chip">CAN ${escapeHtml(String(device.canConnected))}</span>
                    <span class="chip">ETH ${escapeHtml(String(device.ethernetConnected))}</span>
                </div>
                <div class="device-meta">
                    <span class="chip">DI ${escapeHtml(String(device.digitalInputs))}</span>
                    <span class="chip">DO ${escapeHtml(String(device.digitalOutputs))}</span>
                    <span class="chip">${escapeHtml(device.firmware)}</span>
                </div>
                <div class="health-bar">
                    <span style="width:${device.signalPercent}%"></span>
                </div>
                <p class="muted-copy">Plyta glowna: ${escapeHtml(device.mainboardStatus)}, ${escapeHtml(String(device.mainboardTempC))}C, ${escapeHtml(String(device.supplyVoltage))} V. Ostatni kontakt: ${escapeHtml(device.lastSeen)}</p>
            </article>
        `)
        .join("");
}

function renderFleet(state) {
    fleetState = cloneFleetState(state);

    const devices = fleetState.pncDevices ?? [];
    renderFleetSummary(devices);

    if (!devices.length) {
        selectedPncId = "";
        wizardPncList.innerHTML = '<p class="empty-state">Brak skonfigurowanych wezlow PNC. Dodaj pierwszy formularzem po prawej stronie.</p>';
        connectionList.innerHTML = '<p class="empty-state">Najpierw zapisz PNC, aby konfigurowac porty zewnetrzne.</p>';
        document.getElementById("connection-owner").textContent = "Wybierz PNC do konfiguracji portow";
        badgeTargets.wizard.textContent = "0";
        return;
    }

    const currentCode = document.getElementById("pnc-original-code").value;
    const candidateCode = currentCode || selectedPncId || devices[0].deviceCode;
    selectedPncId = devices.some((device) => device.deviceCode === candidateCode)
        ? candidateCode
        : devices[0].deviceCode;

    renderWizardPncList(devices);
    renderConnectionList(getSelectedPnc());
    badgeTargets.wizard.textContent = String(devices.length);
    renderOtaTargetOptions(getSelectedOtaTargetIds());

    if (!fleetDirty) {
        fillPncForm(getSelectedPnc());
        resetConnectionForm();
    }
}

function renderFleetSummary(devices) {
    const container = document.getElementById("fleet-summary");
    const totalConnections = devices.reduce((sum, device) => sum + (device.connections?.length ?? 0), 0);
    const cards = [
        { label: "Wezly PNC", value: devices.length, footnote: "Zdefiniowane urzadzenia nadrzedne" },
        { label: "Online", value: devices.filter((device) => device.online).length, footnote: "Wezly gotowe do pracy" },
        { label: "Porty", value: totalConnections, footnote: "Skonfigurowane mapowania zewnetrzne" },
        { label: "SIM", value: devices.filter((device) => device.simNumber).length, footnote: "Karty SIM przypisane do PNC" },
        { label: "Watchdog", value: devices.filter((device) => device.watchdogHealthy).length, footnote: "Plyty z aktywnym watchdogiem" }
    ];

    container.innerHTML = cards
        .map((card) => `
            <article class="metric-card">
                <span>${escapeHtml(card.label)}</span>
                <strong>${escapeHtml(String(card.value))}</strong>
                <small>${escapeHtml(card.footnote)}</small>
            </article>
        `)
        .join("");
}

function renderWizardPncList(devices) {
    wizardPncList.innerHTML = devices
        .map((device) => `
            <article class="stack-item ${selectedPncId === device.deviceCode ? "selected" : ""}">
                <div class="rule-card-header">
                    <div>
                        <strong>${escapeHtml(device.name)}</strong>
                        <p class="detail-copy">${escapeHtml(device.deviceCode)} / ${escapeHtml(device.location)}</p>
                    </div>
                    <div class="inline-actions">
                        <button class="action-button ghost" type="button" data-pnc-action="edit" data-pnc-code="${escapeHtml(device.deviceCode)}">Edytuj</button>
                        <button class="action-button ghost" type="button" data-pnc-action="ports" data-pnc-code="${escapeHtml(device.deviceCode)}">Porty</button>
                        <button class="action-button danger" type="button" data-pnc-action="delete" data-pnc-code="${escapeHtml(device.deviceCode)}">Usun</button>
                    </div>
                </div>
                <div class="chip-row">
                    <span class="status-pill ${device.online ? "online" : "critical"}">${device.online ? "Online" : "Offline"}</span>
                    <span class="chip">${escapeHtml(device.operatorName)}</span>
                    <span class="chip">${escapeHtml(device.networkType)}</span>
                    <span class="chip">${escapeHtml(device.simSlot)}</span>
                    <span class="chip">${escapeHtml(device.firmware)}</span>
                </div>
                <div class="chip-row">
                    <span class="chip">sygnal ${escapeHtml(String(device.baseSignalPercent))}%</span>
                    <span class="chip">${escapeHtml(String(device.baseSignalDbm))} dBm</span>
                    <span class="chip">CPU ${escapeHtml(String(device.baseCpuLoadPercent))}%</span>
                    <span class="chip">RAM ${escapeHtml(String(device.baseMemoryPercent))}%</span>
                </div>
                <p class="subtle-copy">
                    Porty: RS-232 ${escapeHtml(String(device.rs232Connected))}, CAN ${escapeHtml(String(device.canConnected))}, ETH ${escapeHtml(String(device.ethernetConnected))}, DI ${escapeHtml(String(device.digitalInputs))}, DO ${escapeHtml(String(device.digitalOutputs))}.
                </p>
            </article>
        `)
        .join("");
}

function renderConnectionList(device) {
    const owner = document.getElementById("connection-owner");
    if (!device) {
        owner.textContent = "Wybierz PNC do konfiguracji portow";
        connectionList.innerHTML = '<p class="empty-state">Najpierw wybierz lub zapisz PNC.</p>';
        return;
    }

    owner.textContent = `Porty dla ${device.name} (${device.deviceCode})`;

    if (!device.connections?.length) {
        connectionList.innerHTML = '<p class="empty-state">Brak skonfigurowanych polaczen. Dodaj pierwsze urzadzenie po prawej stronie.</p>';
        return;
    }

    connectionList.innerHTML = device.connections
        .map((connection) => `
            <article class="stack-item ${document.getElementById("connection-id").value === connection.id ? "selected" : ""}">
                <div class="rule-card-header">
                    <div>
                        <strong>${escapeHtml(translateInterfaceType(connection.interfaceType))} / ${escapeHtml(connection.portName)}</strong>
                        <p class="detail-copy">${escapeHtml(connection.deviceName)}</p>
                    </div>
                    <div class="inline-actions">
                        <button class="action-button ghost" type="button" data-connection-action="edit" data-connection-id="${escapeHtml(connection.id)}">Edytuj</button>
                        <button class="action-button danger" type="button" data-connection-action="delete" data-connection-id="${escapeHtml(connection.id)}">Usun</button>
                    </div>
                </div>
                <div class="chip-row">
                    <span class="status-pill ${escapeHtml(connection.status)}">${escapeHtml(translateStatus(connection.status))}</span>
                    <span class="chip">${escapeHtml(connection.protocol)}</span>
                    ${connection.baudRate ? `<span class="chip">${escapeHtml(String(connection.baudRate))} baud</span>` : ""}
                </div>
                <p class="subtle-copy">${escapeHtml(connection.notes || "Brak notatek serwisowych.")}</p>
            </article>
        `)
        .join("");
}

function renderMainboardSummary(mainboards) {
    const container = document.getElementById("mainboard-summary");
    if (!mainboards?.length) {
        container.innerHTML = "";
        return;
    }

    const cards = [
        { label: "Plyty", value: mainboards.length, footnote: "Wszystkie monitorowane wezly" },
        { label: "Online", value: mainboards.filter((board) => board.status === "online").length, footnote: "Plyty bez presji serwisowej" },
        { label: "Obserwacja", value: mainboards.filter((board) => board.status === "attention").length, footnote: "Wezly do kontroli" },
        { label: "Krytyczne", value: mainboards.filter((board) => board.status === "critical").length, footnote: "Pilna interwencja" },
        { label: "Porty", value: mainboards.reduce((sum, board) => sum + board.configuredConnectionCount, 0), footnote: "Liczba mapowan zewnetrznych" }
    ];

    container.innerHTML = cards
        .map((card) => `
            <article class="metric-card">
                <span>${escapeHtml(card.label)}</span>
                <strong>${escapeHtml(String(card.value))}</strong>
                <small>${escapeHtml(card.footnote)}</small>
            </article>
        `)
        .join("");
}

function renderBoardMonitor(mainboards) {
    const container = document.getElementById("mainboard-grid");
    if (!mainboards?.length) {
        container.innerHTML = '<p class="empty-state">Brak telemetrii plyt glownych.</p>';
        return;
    }

    container.innerHTML = mainboards
        .map((board) => {
            const sourceDevice = fleetState?.pncDevices?.find((device) => device.deviceCode === board.deviceCode);
            const connections = sourceDevice?.connections ?? [];
            const preview = connections
                .slice(0, 4)
                .map((connection) => `<span class="member-pill ${connection.status === "critical" ? "alert" : ""}">${escapeHtml(translateInterfaceType(connection.interfaceType))}: ${escapeHtml(connection.portName)}</span>`)
                .join("");

            return `
                <article class="device-card">
                    <div class="status-row">
                        <span class="status-pill ${escapeHtml(board.status)}">${escapeHtml(translateStatus(board.status))}</span>
                        <span class="chip">${escapeHtml(board.deviceCode)}</span>
                        <span class="chip">${escapeHtml(board.location)}</span>
                    </div>
                    <h4>${escapeHtml(board.name)}</h4>
                    <p>${escapeHtml(board.summary)}</p>
                    <div class="chip-row">
                        <span class="chip">${escapeHtml(board.boardRevision)}</span>
                        <span class="chip">${escapeHtml(board.boardSerialNumber)}</span>
                        <span class="chip">${escapeHtml(board.firmware)}</span>
                    </div>
                    <div class="chip-row">
                        <span class="chip">${escapeHtml(board.operatorName)}</span>
                        <span class="chip">${escapeHtml(board.networkType)}</span>
                        <span class="chip">${escapeHtml(board.simSlot)} / ${escapeHtml(board.simNumber)}</span>
                    </div>
                    <div class="telemetry-grid">
                        ${telemetryCard("CPU", `${board.cpuLoadPercent}%`, board.cpuLoadPercent)}
                        ${telemetryCard("RAM", `${board.memoryPercent}%`, board.memoryPercent)}
                        ${telemetryCard("Dysk", `${board.storagePercent}%`, board.storagePercent)}
                    </div>
                    <div class="chip-row">
                        <span class="chip">temp ${escapeHtml(String(board.temperatureC))}C</span>
                        <span class="chip">${escapeHtml(String(board.supplyVoltage))} V</span>
                        <span class="chip">watchdog ${escapeHtml(board.watchdogState)}</span>
                        <span class="chip">sygnal ${escapeHtml(String(board.signalPercent))}% / ${escapeHtml(board.signalQuality)}</span>
                    </div>
                    <p class="helper-copy">${escapeHtml(board.portSummary)}. Ostatni kontakt: ${escapeHtml(board.lastSeen)}. Uptime: ${escapeHtml(String(board.uptimeHours))} h.</p>
                    <div class="member-list">
                        ${preview || '<span class="member-pill">Brak mapowan portow</span>'}
                        ${connections.length > 4 ? `<span class="member-pill">+${escapeHtml(String(connections.length - 4))} wiecej</span>` : ""}
                    </div>
                </article>
            `;
        })
        .join("");
}

function telemetryCard(label, value, width) {
    return `
        <article class="telemetry-card">
            <span>${escapeHtml(label)}</span>
            <strong>${escapeHtml(value)}</strong>
            <div class="health-bar compact">
                <span style="width:${Math.max(0, Math.min(100, Number(width) || 0))}%"></span>
            </div>
        </article>
    `;
}

async function onPncFormSubmit(event) {
    event.preventDefault();
    if (!fleetState) {
        return;
    }

    try {
        const nextDevice = collectPncFormValue();
        const originalCode = document.getElementById("pnc-original-code").value;
        fleetState.pncDevices = fleetState.pncDevices.filter((device) => device.deviceCode !== originalCode && device.deviceCode !== nextDevice.deviceCode);
        fleetState.pncDevices.unshift(nextDevice);
        selectedPncId = nextDevice.deviceCode;
        await persistFleet(`PNC ${nextDevice.deviceCode} zostal zapisany.`);
        resetConnectionForm();
    } catch (error) {
        setFleetStatus(error.message || "Nie udalo sie zapisac konfiguracji PNC.");
    }
}

async function onConnectionFormSubmit(event) {
    event.preventDefault();
    if (!fleetState) {
        return;
    }

    try {
        const selectedDevice = getSelectedPnc();
        if (!selectedDevice) {
            throw new Error("Wybierz PNC z listy, zanim dodasz polaczenie.");
        }

        const nextConnection = collectConnectionFormValue();
        const existingIndex = selectedDevice.connections.findIndex((connection) => connection.id === nextConnection.id);
        if (existingIndex >= 0) {
            selectedDevice.connections.splice(existingIndex, 1, nextConnection);
        } else {
            selectedDevice.connections.unshift(nextConnection);
        }

        syncConnectionCounts(selectedDevice);
        await persistFleet(`Polaczenie ${nextConnection.portName} zostalo zapisane.`);
        resetConnectionForm();
    } catch (error) {
        setFleetStatus(error.message || "Nie udalo sie zapisac polaczenia.");
    }
}

async function onWizardPncListClick(event) {
    const button = event.target.closest("button[data-pnc-action]");
    if (!button || !fleetState) {
        return;
    }

    const device = fleetState.pncDevices.find((item) => item.deviceCode === button.dataset.pncCode);
    if (!device) {
        return;
    }

    selectedPncId = device.deviceCode;

    if (button.dataset.pncAction === "delete") {
        fleetState.pncDevices = fleetState.pncDevices.filter((item) => item.deviceCode !== device.deviceCode);
        if (selectedPncId === device.deviceCode) {
            selectedPncId = fleetState.pncDevices[0]?.deviceCode ?? "";
        }
        await persistFleet(`PNC ${device.deviceCode} zostal usuniety.`);
        resetPncForm();
        resetConnectionForm();
        return;
    }

    renderWizardPncList(fleetState.pncDevices);
    renderConnectionList(device);

    if (button.dataset.pncAction === "edit") {
        fillPncForm(device);
        resetConnectionForm();
        setFleetStatus(`Edytujesz ${device.name}.`);
        return;
    }

    setFleetStatus(`Konfigurujesz porty dla ${device.name}.`);
}

async function onConnectionListClick(event) {
    const button = event.target.closest("button[data-connection-action]");
    if (!button || !fleetState) {
        return;
    }

    const selectedDevice = getSelectedPnc();
    if (!selectedDevice) {
        return;
    }

    const connection = selectedDevice.connections.find((item) => item.id === button.dataset.connectionId);
    if (!connection) {
        return;
    }

    if (button.dataset.connectionAction === "edit") {
        fillConnectionForm(connection);
        setFleetStatus(`Edytujesz polaczenie ${connection.portName} dla ${selectedDevice.deviceCode}.`);
        return;
    }

    selectedDevice.connections = selectedDevice.connections.filter((item) => item.id !== connection.id);
    syncConnectionCounts(selectedDevice);
    await persistFleet(`Polaczenie ${connection.portName} zostalo usuniete.`);
    resetConnectionForm();
}

async function persistFleet(successMessage) {
    if (!fleetState) {
        return;
    }

    fleetDirty = false;

    try {
        const response = await fetch(fleetUrl, {
            method: "PUT",
            headers: {
                "Content-Type": "application/json"
            },
            body: JSON.stringify({
                lte: fleetState.lte,
                pncDevices: fleetState.pncDevices
            })
        });

        if (!response.ok) {
            throw new Error(`HTTP ${response.status}`);
        }

        const state = await response.json();
        renderFleet(state);
        setFleetStatus(successMessage);
        await refreshPortal();
    } catch (error) {
        fleetDirty = true;
        console.error(error);
        setFleetStatus("Nie udalo sie zapisac konfiguracji floty.");
    }
}

function collectPncFormValue() {
    const originalCode = document.getElementById("pnc-original-code").value;
    const deviceCode = (document.getElementById("pnc-device-code").value.trim() || createPncCode(document.getElementById("pnc-name").value)).toUpperCase();
    const name = document.getElementById("pnc-name").value.trim();
    if (!name) {
        throw new Error("Podaj nazwe wezla PNC.");
    }

    const existing = fleetState?.pncDevices?.find((device) => device.deviceCode === originalCode || device.deviceCode === deviceCode);
    const connections = [...(existing?.connections ?? [])].map((connection) => ({ ...connection }));

    const nextDevice = {
        deviceCode,
        name,
        location: document.getElementById("pnc-location").value.trim() || "Nieokreslona lokalizacja",
        operatorName: document.getElementById("pnc-operator").value.trim() || "Orange PL",
        networkType: document.getElementById("pnc-network-type").value.trim() || "LTE",
        simNumber: document.getElementById("pnc-sim-number").value.trim() || "+48 500 000 000",
        simSlot: document.getElementById("pnc-sim-slot").value.trim() || "SIM1",
        baseSignalPercent: Number.parseInt(document.getElementById("pnc-signal-percent").value || "70", 10),
        baseSignalDbm: Number.parseInt(document.getElementById("pnc-signal-dbm").value || "-72", 10),
        rs232Connected: 0,
        canConnected: 0,
        ethernetConnected: 0,
        digitalInputs: 0,
        digitalOutputs: 0,
        firmware: document.getElementById("pnc-firmware").value.trim() || "PNC-OS 2.4.1",
        mainboardStatus: document.getElementById("pnc-mainboard-status").value.trim() || "stabilna",
        mainboardTempC: Number.parseInt(document.getElementById("pnc-mainboard-temp").value || "44", 10),
        supplyVoltage: Number.parseFloat(document.getElementById("pnc-supply-voltage").value || "24.0"),
        online: document.getElementById("pnc-online").checked,
        boardRevision: document.getElementById("pnc-board-revision").value.trim() || "MB-2.1",
        boardSerialNumber: document.getElementById("pnc-board-serial").value.trim() || `${deviceCode}-MB-001`,
        baseCpuLoadPercent: Number.parseInt(document.getElementById("pnc-cpu-load").value || "35", 10),
        baseMemoryPercent: Number.parseInt(document.getElementById("pnc-memory-load").value || "45", 10),
        baseStoragePercent: Number.parseInt(document.getElementById("pnc-storage-load").value || "55", 10),
        watchdogHealthy: document.getElementById("pnc-watchdog-healthy").checked,
        uptimeHours: Number.parseInt(document.getElementById("pnc-uptime-hours").value || "24", 10),
        notes: document.getElementById("pnc-notes").value.trim() || "Brak opisu dla wezla PNC.",
        connections
    };

    syncConnectionCounts(nextDevice);
    return nextDevice;
}

function collectConnectionFormValue() {
    const selectedDevice = getSelectedPnc();
    if (!selectedDevice) {
        throw new Error("Wybierz PNC z listy, zanim dodasz urzadzenie do portu.");
    }

    const interfaceType = document.getElementById("connection-interface-type").value;
    const portName = document.getElementById("connection-port-name").value.trim();
    const deviceName = document.getElementById("connection-device-name").value.trim();
    if (!deviceName) {
        throw new Error("Podaj nazwe urzadzenia koncowego.");
    }

    const normalizedPortName = portName || suggestPortName(interfaceType, selectedDevice.connections ?? []);
    const rawBaudRate = document.getElementById("connection-baud-rate").value.trim();
    return {
        id: document.getElementById("connection-id").value || createId("connection", `${selectedDevice.deviceCode}-${interfaceType}-${normalizedPortName}`),
        interfaceType,
        portName: normalizedPortName,
        deviceName,
        protocol: document.getElementById("connection-protocol").value.trim() || defaultProtocolForInterface(interfaceType),
        status: document.getElementById("connection-status").value,
        notes: document.getElementById("connection-notes").value.trim(),
        baudRate: interfaceType === "rs232" && rawBaudRate ? Number.parseInt(rawBaudRate, 10) : null
    };
}

function fillPncForm(device) {
    if (!device) {
        return;
    }

    document.getElementById("pnc-original-code").value = device.deviceCode;
    document.getElementById("pnc-device-code").value = device.deviceCode;
    document.getElementById("pnc-name").value = device.name;
    document.getElementById("pnc-location").value = device.location;
    document.getElementById("pnc-operator").value = device.operatorName;
    document.getElementById("pnc-network-type").value = device.networkType;
    document.getElementById("pnc-sim-number").value = device.simNumber;
    document.getElementById("pnc-sim-slot").value = device.simSlot;
    document.getElementById("pnc-firmware").value = device.firmware;
    document.getElementById("pnc-signal-percent").value = String(device.baseSignalPercent);
    document.getElementById("pnc-signal-dbm").value = String(device.baseSignalDbm);
    document.getElementById("pnc-board-revision").value = device.boardRevision;
    document.getElementById("pnc-board-serial").value = device.boardSerialNumber;
    document.getElementById("pnc-mainboard-status").value = device.mainboardStatus;
    document.getElementById("pnc-mainboard-temp").value = String(device.mainboardTempC);
    document.getElementById("pnc-supply-voltage").value = String(device.supplyVoltage);
    document.getElementById("pnc-uptime-hours").value = String(device.uptimeHours);
    document.getElementById("pnc-cpu-load").value = String(device.baseCpuLoadPercent);
    document.getElementById("pnc-memory-load").value = String(device.baseMemoryPercent);
    document.getElementById("pnc-storage-load").value = String(device.baseStoragePercent);
    document.getElementById("pnc-online").checked = Boolean(device.online);
    document.getElementById("pnc-watchdog-healthy").checked = Boolean(device.watchdogHealthy);
    document.getElementById("pnc-notes").value = device.notes || "";
    fleetDirty = false;
}

function fillConnectionForm(connection) {
    document.getElementById("connection-id").value = connection.id;
    document.getElementById("connection-interface-type").value = connection.interfaceType;
    document.getElementById("connection-port-name").value = connection.portName;
    document.getElementById("connection-device-name").value = connection.deviceName;
    document.getElementById("connection-protocol").value = connection.protocol;
    document.getElementById("connection-baud-rate").value = connection.baudRate ? String(connection.baudRate) : "";
    document.getElementById("connection-status").value = connection.status;
    document.getElementById("connection-notes").value = connection.notes || "";
    fleetDirty = false;
}

function resetPncForm(draftMode = false) {
    pncForm.reset();
    document.getElementById("pnc-original-code").value = "";
    document.getElementById("pnc-device-code").value = "";
    document.getElementById("pnc-signal-percent").value = "70";
    document.getElementById("pnc-signal-dbm").value = "-72";
    document.getElementById("pnc-mainboard-temp").value = "44";
    document.getElementById("pnc-supply-voltage").value = "24.0";
    document.getElementById("pnc-uptime-hours").value = "24";
    document.getElementById("pnc-cpu-load").value = "35";
    document.getElementById("pnc-memory-load").value = "45";
    document.getElementById("pnc-storage-load").value = "55";
    document.getElementById("pnc-online").checked = true;
    document.getElementById("pnc-watchdog-healthy").checked = true;
    fleetDirty = draftMode;
}

function resetConnectionForm(draftMode = false) {
    connectionForm.reset();
    document.getElementById("connection-id").value = "";
    document.getElementById("connection-interface-type").value = "rs232";
    document.getElementById("connection-status").value = "online";
    fleetDirty = draftMode;
}

function getSelectedPnc() {
    return fleetState?.pncDevices?.find((device) => device.deviceCode === selectedPncId) ?? null;
}

function syncConnectionCounts(device) {
    const connections = device.connections ?? [];
    device.rs232Connected = connections.filter((connection) => connection.interfaceType === "rs232").length;
    device.canConnected = connections.filter((connection) => connection.interfaceType === "can").length;
    device.ethernetConnected = connections.filter((connection) => connection.interfaceType === "ethernet").length;
    device.digitalInputs = connections.filter((connection) => connection.interfaceType === "digital-input").length;
    device.digitalOutputs = connections.filter((connection) => connection.interfaceType === "digital-output").length;
}

function markFleetDirty() {
    fleetDirty = true;
}

function renderOta(state) {
    const selectedTargets = getSelectedOtaTargetIds();
    const selectedRecipients = getSelectedOtaRecipientIds();
    otaState = cloneOtaState(state);

    renderOtaSummary(otaState.summary);
    renderOtaPackages(otaState.packages);
    renderOtaCampaigns(otaState.campaigns);
    renderOtaLogs(otaState.logs);
    renderOtaEmailLogs(otaState.emailLogs);
    renderOtaPackageOptions();
    renderOtaTargetOptions(selectedTargets);
    renderOtaRecipientOptions(selectedRecipients);
    badgeTargets.ota.textContent = String(otaState.summary.scheduledCount ?? 0);
}

function renderOtaSummary(summary) {
    const container = document.getElementById("ota-summary");
    const cards = [
        { label: "Pakiety", value: summary.packageCount, footnote: "Dostepne paczki OTA" },
        { label: "Kampanie", value: summary.campaignCount, footnote: `zaplanowane ${summary.scheduledCount}` },
        { label: "Zakonczone", value: summary.completedCount, footnote: `czesciowe ${summary.partialCount}` },
        { label: "Bledy", value: summary.failedCount, footnote: "Kampanie wymagajace reakcji" },
        { label: "Logi", value: summary.logCount, footnote: `e-maile ${summary.emailCount}` }
    ];

    container.innerHTML = cards
        .map((card) => `
            <article class="metric-card">
                <span>${escapeHtml(card.label)}</span>
                <strong>${escapeHtml(String(card.value))}</strong>
                <small>${escapeHtml(card.footnote)}</small>
            </article>
        `)
        .join("");
}

function renderOtaPackages(packages) {
    if (!packages?.length) {
        otaPackagesList.innerHTML = '<p class="empty-state">Brak paczek OTA. Dodaj pierwsza paczke po prawej stronie.</p>';
        return;
    }

    otaPackagesList.innerHTML = packages
        .map((pkg) => `
            <article class="stack-item">
                <div class="rule-card-header">
                    <div>
                        <strong>${escapeHtml(pkg.name)} ${escapeHtml(pkg.version)}</strong>
                        <p class="detail-copy">${escapeHtml(pkg.description || "Brak opisu pakietu.")}</p>
                    </div>
                    <div class="inline-actions">
                        <button class="action-button ghost" type="button" data-ota-package-action="edit" data-ota-package-id="${escapeHtml(pkg.id)}">Edytuj</button>
                        <button class="action-button danger" type="button" data-ota-package-action="delete" data-ota-package-id="${escapeHtml(pkg.id)}">Usun</button>
                    </div>
                </div>
                <div class="chip-row">
                    <span class="chip">${escapeHtml(pkg.target)}</span>
                    <span class="chip">${escapeHtml(pkg.fileName)}</span>
                    <span class="chip">${escapeHtml(String(pkg.sizeMb))} MB</span>
                    <span class="chip">${pkg.mandatory ? "wymagana" : "opcjonalna"}</span>
                </div>
                <p class="subtle-copy">${escapeHtml(pkg.releaseNotes || "Brak release notes.")}</p>
            </article>
        `)
        .join("");
}

function renderOtaCampaigns(campaigns) {
    if (!campaigns?.length) {
        otaCampaignsList.innerHTML = '<p class="empty-state">Brak kampanii OTA.</p>';
        return;
    }

    otaCampaignsList.innerHTML = campaigns
        .map((campaign) => `
            <article class="stack-item">
                <div class="rule-card-header">
                    <div>
                        <strong>${escapeHtml(campaign.title)}</strong>
                        <p class="detail-copy">${escapeHtml(campaign.summary)}</p>
                    </div>
                    <div class="inline-actions">
                        <button class="action-button ghost" type="button" data-ota-campaign-action="edit" data-ota-campaign-id="${escapeHtml(campaign.id)}">Edytuj</button>
                        <button class="action-button danger" type="button" data-ota-campaign-action="delete" data-ota-campaign-id="${escapeHtml(campaign.id)}">Usun</button>
                    </div>
                </div>
                <div class="chip-row">
                    <span class="status-pill ${escapeHtml(statusClassForCampaign(campaign.status))}">${escapeHtml(translateCampaignStatus(campaign.status))}</span>
                    <span class="chip">${escapeHtml(campaign.packageName)} ${escapeHtml(campaign.packageVersion)}</span>
                    <span class="chip">${escapeHtml(campaign.transport)}</span>
                    <span class="chip">${escapeHtml(campaign.window)}</span>
                </div>
                <div class="chip-row">
                    <span class="chip">cele: ${escapeHtml(String(campaign.targetCount))}</span>
                    <span class="chip">sukces ${escapeHtml(String(campaign.successfulCount ?? 0))}</span>
                    <span class="chip">bledy ${escapeHtml(String(campaign.failedCount ?? 0))}</span>
                    <span class="chip">retry ${escapeHtml(String(campaign.retryLimit))}</span>
                </div>
                <p class="subtle-copy">Termin: ${escapeHtml(formatDateTime(campaign.scheduledForUtc))}. Odbiorcy e-maila: ${escapeHtml((campaign.recipientEmails ?? []).join(", ") || "brak")}.</p>
            </article>
        `)
        .join("");
}

function renderOtaLogs(logs) {
    if (!logs?.length) {
        otaLogList.innerHTML = '<p class="empty-state">Brak logow OTA.</p>';
        return;
    }

    otaLogList.innerHTML = logs
        .map((log) => `
            <article class="stack-item">
                <div class="status-row">
                    <span class="level-pill ${escapeHtml(log.level)}">${escapeHtml(translateLevel(log.level))}</span>
                    <span class="chip">${escapeHtml(log.deviceCode)}</span>
                    <span class="chip">${escapeHtml(formatDateTime(log.occurredAtUtc))}</span>
                </div>
                <strong>${escapeHtml(log.campaignId)}</strong>
                <p>${escapeHtml(log.message)}</p>
            </article>
        `)
        .join("");
}

function renderOtaEmailLogs(emails) {
    if (!emails?.length) {
        otaEmailList.innerHTML = '<p class="empty-state">Nie wyslano jeszcze automatycznych e-maili po aktualizacji.</p>';
        return;
    }

    otaEmailList.innerHTML = emails
        .map((email) => `
            <article class="stack-item">
                <div class="status-row">
                    <span class="level-pill info">e-mail</span>
                    <span class="chip">${escapeHtml(formatDateTime(email.sentAtUtc))}</span>
                </div>
                <strong>${escapeHtml(email.subject)}</strong>
                <p>${escapeHtml(email.body)}</p>
                <p class="subtle-copy">Do: ${escapeHtml((email.recipients ?? []).join(", "))}</p>
            </article>
        `)
        .join("");
}

function renderOtaPackageOptions() {
    const select = document.getElementById("ota-campaign-package");
    const packages = otaState?.packages ?? [];
    const selectedId = select.value || document.getElementById("ota-campaign-package").dataset.selectedId || "";

    select.innerHTML = packages
        .map((pkg) => `<option value="${escapeHtml(pkg.id)}">${escapeHtml(pkg.name)} ${escapeHtml(pkg.version)}</option>`)
        .join("");

    if (!packages.length) {
        select.innerHTML = '<option value="">Brak paczek OTA</option>';
        return;
    }

    select.value = packages.some((pkg) => pkg.id === selectedId) ? selectedId : packages[0].id;
}

function renderOtaTargetOptions(selectedIds = []) {
    const container = document.getElementById("ota-target-list");
    const devices = fleetState?.pncDevices ?? [];

    if (!devices.length) {
        container.innerHTML = '<p class="empty-state">Najpierw odczytaj konfiguracje floty PNC.</p>';
        return;
    }

    const selectedSet = new Set(selectedIds);
    container.innerHTML = devices
        .map((device) => `
            <label class="selection-item">
                <input type="checkbox" value="${escapeHtml(device.deviceCode)}" ${selectedSet.has(device.deviceCode) ? "checked" : ""}>
                <span>
                    <strong>${escapeHtml(device.name)}</strong>
                    <small>${escapeHtml(device.deviceCode)} | soft ${escapeHtml(device.firmware)} | sygnal ${escapeHtml(String(device.baseSignalPercent))}%</small>
                </span>
            </label>
        `)
        .join("");
}

function renderOtaRecipientOptions(selectedIds = []) {
    const container = document.getElementById("ota-recipient-list");
    const recipients = otaState?.serviceRecipients ?? [];

    if (!recipients.length) {
        container.innerHTML = '<p class="empty-state">Brak odbiorcow serwisowych.</p>';
        return;
    }

    const selectedSet = new Set(selectedIds);
    container.innerHTML = recipients
        .map((recipient) => `
            <label class="selection-item">
                <input type="checkbox" value="${escapeHtml(recipient.id)}" ${selectedSet.has(recipient.id) ? "checked" : ""}>
                <span>
                    <strong>${escapeHtml(recipient.displayName)}</strong>
                    <small>${escapeHtml(recipient.role)} | ${escapeHtml(recipient.email)}</small>
                </span>
            </label>
        `)
        .join("");
}

async function onOtaPackageFormSubmit(event) {
    event.preventDefault();
    if (!otaState) {
        return;
    }

    try {
        const nextPackage = collectOtaPackageFormValue();
        const existingIndex = otaState.packages.findIndex((pkg) => pkg.id === nextPackage.id);
        if (existingIndex >= 0) {
            otaState.packages.splice(existingIndex, 1, nextPackage);
        } else {
            otaState.packages.unshift(nextPackage);
        }

        await persistOta("Pakiet OTA zostal zapisany.");
        resetOtaPackageForm();
    } catch (error) {
        setOtaStatus(error.message || "Nie udalo sie zapisac pakietu OTA.");
    }
}

async function onOtaCampaignFormSubmit(event) {
    event.preventDefault();
    if (!otaState) {
        return;
    }

    try {
        const nextCampaign = collectOtaCampaignFormValue();
        const existingIndex = otaState.campaigns.findIndex((campaign) => campaign.id === nextCampaign.id);
        if (existingIndex >= 0) {
            otaState.campaigns.splice(existingIndex, 1, nextCampaign);
        } else {
            otaState.campaigns.unshift(nextCampaign);
        }

        await persistOta("Kampania OTA zostala zapisana.");
        resetOtaCampaignForm();
    } catch (error) {
        setOtaStatus(error.message || "Nie udalo sie zapisac kampanii OTA.");
    }
}

async function onOtaPackagesListClick(event) {
    const button = event.target.closest("button[data-ota-package-action]");
    if (!button || !otaState) {
        return;
    }

    const pkg = otaState.packages.find((item) => item.id === button.dataset.otaPackageId);
    if (!pkg) {
        return;
    }

    if (button.dataset.otaPackageAction === "edit") {
        fillOtaPackageForm(pkg);
        setOtaStatus(`Edytujesz pakiet: ${pkg.name} ${pkg.version}`);
        return;
    }

    if (otaState.campaigns.some((campaign) => campaign.packageId === pkg.id)) {
        setOtaStatus("Nie mozna usunac pakietu przypisanego do aktywnej kampanii.");
        return;
    }

    otaState.packages = otaState.packages.filter((item) => item.id !== pkg.id);
    await persistOta(`Pakiet "${pkg.name}" zostal usuniety.`);
    resetOtaPackageForm();
}

async function onOtaCampaignsListClick(event) {
    const button = event.target.closest("button[data-ota-campaign-action]");
    if (!button || !otaState) {
        return;
    }

    const campaign = otaState.campaigns.find((item) => item.id === button.dataset.otaCampaignId);
    if (!campaign) {
        return;
    }

    if (button.dataset.otaCampaignAction === "edit") {
        fillOtaCampaignForm(campaign);
        setOtaStatus(`Edytujesz kampanie: ${campaign.title}`);
        return;
    }

    otaState.campaigns = otaState.campaigns.filter((item) => item.id !== campaign.id);
    otaState.logs = otaState.logs.filter((log) => log.campaignId !== campaign.id);
    otaState.emailLogs = otaState.emailLogs.filter((email) => email.campaignId !== campaign.id);
    await persistOta(`Kampania "${campaign.title}" zostala usunieta.`);
    resetOtaCampaignForm();
}

async function persistOta(successMessage) {
    otaDirty = false;

    try {
        const response = await fetch(otaUrl, {
            method: "PUT",
            headers: {
                "Content-Type": "application/json"
            },
            body: JSON.stringify({
                packages: otaState.packages,
                campaigns: otaState.campaigns,
                serviceRecipients: otaState.serviceRecipients,
                logs: otaState.logs,
                emailLogs: otaState.emailLogs
            })
        });

        if (!response.ok) {
            throw new Error(`HTTP ${response.status}`);
        }

        const state = await response.json();
        renderOta(state);
        setOtaStatus(successMessage);
    } catch (error) {
        otaDirty = true;
        console.error(error);
        setOtaStatus("Nie udalo sie zapisac zmian OTA.");
    }
}

function collectOtaPackageFormValue() {
    const name = document.getElementById("ota-package-name").value.trim();
    const version = document.getElementById("ota-package-version").value.trim();

    if (!name) {
        throw new Error("Podaj nazwe pakietu OTA.");
    }

    if (!version) {
        throw new Error("Podaj wersje pakietu OTA.");
    }

    const existingId = document.getElementById("ota-package-id").value;
    const generatedId = createId("pkg", `${name}-${version}`);

    return {
        id: existingId || generatedId,
        name,
        version,
        target: document.getElementById("ota-package-target").value.trim() || "PNC OS",
        fileName: document.getElementById("ota-package-file").value.trim() || `${generatedId}.bin`,
        sizeMb: Number.parseFloat(document.getElementById("ota-package-size").value || "1"),
        description: document.getElementById("ota-package-description").value.trim(),
        releaseNotes: document.getElementById("ota-package-notes").value.trim(),
        mandatory: document.getElementById("ota-package-mandatory").checked
    };
}

function collectOtaCampaignFormValue() {
    const title = document.getElementById("ota-campaign-title").value.trim();
    const packageId = document.getElementById("ota-campaign-package").value;
    const scheduled = document.getElementById("ota-campaign-scheduled").value;
    const targetDeviceCodes = getSelectedOtaTargetIds();
    const recipientIds = getSelectedOtaRecipientIds();
    const existing = otaState?.campaigns?.find((campaign) => campaign.id === document.getElementById("ota-campaign-id").value);

    if (!title) {
        throw new Error("Podaj nazwe kampanii OTA.");
    }

    if (!packageId) {
        throw new Error("Wybierz pakiet OTA.");
    }

    if (!scheduled) {
        throw new Error("Podaj termin wysylki OTA.");
    }

    if (!targetDeviceCodes.length) {
        throw new Error("Wybierz co najmniej jedno PNC dla kampanii OTA.");
    }

    return {
        id: document.getElementById("ota-campaign-id").value || createId("campaign", `${title}-${packageId}`),
        title,
        packageId,
        targetDeviceCodes,
        scheduledForUtc: new Date(scheduled).toISOString(),
        transport: document.getElementById("ota-campaign-transport").value.trim() || "LTE",
        window: document.getElementById("ota-campaign-window").value.trim() || "okno serwisowe 00:00-04:00",
        retryLimit: Number.parseInt(document.getElementById("ota-campaign-retry").value || "0", 10),
        notifyServiceByEmail: document.getElementById("ota-campaign-email").checked,
        recipientIds,
        status: existing?.status === "scheduled" || !existing ? "scheduled" : existing.status,
        notes: document.getElementById("ota-campaign-notes").value.trim(),
        createdAtUtc: existing?.createdAtUtc || new Date().toISOString(),
        startedAtUtc: existing?.startedAtUtc || null,
        completedAtUtc: existing?.completedAtUtc || null
    };
}

function fillOtaPackageForm(pkg) {
    document.getElementById("ota-package-id").value = pkg.id;
    document.getElementById("ota-package-name").value = pkg.name;
    document.getElementById("ota-package-version").value = pkg.version;
    document.getElementById("ota-package-target").value = pkg.target || "";
    document.getElementById("ota-package-file").value = pkg.fileName || "";
    document.getElementById("ota-package-size").value = String(pkg.sizeMb ?? 1);
    document.getElementById("ota-package-description").value = pkg.description || "";
    document.getElementById("ota-package-notes").value = pkg.releaseNotes || "";
    document.getElementById("ota-package-mandatory").checked = Boolean(pkg.mandatory);
    otaDirty = false;
}

function fillOtaCampaignForm(campaign) {
    document.getElementById("ota-campaign-id").value = campaign.id;
    document.getElementById("ota-campaign-title").value = campaign.title;
    document.getElementById("ota-campaign-package").dataset.selectedId = campaign.packageId;
    renderOtaPackageOptions();
    document.getElementById("ota-campaign-package").value = campaign.packageId;
    document.getElementById("ota-campaign-scheduled").value = toLocalInputValue(campaign.scheduledForUtc);
    document.getElementById("ota-campaign-transport").value = campaign.transport || "LTE";
    document.getElementById("ota-campaign-window").value = campaign.window || "";
    document.getElementById("ota-campaign-retry").value = String(campaign.retryLimit ?? 0);
    document.getElementById("ota-campaign-email").checked = Boolean(campaign.notifyServiceByEmail);
    document.getElementById("ota-campaign-notes").value = campaign.notes || "";
    renderOtaTargetOptions(campaign.targetDeviceCodes ?? []);
    renderOtaRecipientOptions(campaign.recipientIds ?? []);
    otaDirty = false;
}

function resetOtaPackageForm() {
    otaPackageForm.reset();
    document.getElementById("ota-package-id").value = "";
    document.getElementById("ota-package-size").value = "32";
    otaDirty = false;
}

function resetOtaCampaignForm() {
    otaCampaignForm.reset();
    document.getElementById("ota-campaign-id").value = "";
    document.getElementById("ota-campaign-scheduled").value = toLocalInputValue(new Date(Date.now() + (2 * 60 * 60 * 1000)).toISOString());
    document.getElementById("ota-campaign-transport").value = "LTE";
    document.getElementById("ota-campaign-retry").value = "2";
    document.getElementById("ota-campaign-email").checked = true;
    renderOtaPackageOptions();
    renderOtaTargetOptions([]);
    renderOtaRecipientOptions([]);
    otaDirty = false;
}

function getSelectedOtaTargetIds() {
    return [...document.querySelectorAll('#ota-target-list input[type="checkbox"]:checked')]
        .map((input) => input.value);
}

function getSelectedOtaRecipientIds() {
    return [...document.querySelectorAll('#ota-recipient-list input[type="checkbox"]:checked')]
        .map((input) => input.value);
}

function markOtaDirty() {
    otaDirty = true;
}

function suggestPortName(interfaceType, existingConnections) {
    const prefix = interfaceType === "rs232"
        ? "COM"
        : interfaceType === "can"
            ? "CAN-"
            : interfaceType === "ethernet"
                ? "LAN"
                : interfaceType === "digital-input"
                    ? "DI"
                    : "DO";

    const nextIndex = (existingConnections ?? [])
        .filter((connection) => connection.interfaceType === interfaceType)
        .length + 1;

    return `${prefix}${nextIndex}`;
}

function renderRulebook(state) {
    const selectedRecipients = getSelectedRecipientIds();
    rulebookState = cloneRulebookState(state);

    renderRulebookSummary(rulebookState.summary);
    renderRulesList(rulebookState.rules, rulebookState.users);
    renderUsersList(rulebookState.users);
    renderActiveRuleMatches(rulebookState.activeMatches);
    renderDispatchLog(rulebookState.dispatches);
    renderRuleRecipientOptions(selectedRecipients);
    badgeTargets.rules.textContent = String(rulebookState.summary.ruleCount);
}

function renderRulebookSummary(summary) {
    const container = document.getElementById("rulebook-summary");
    const cards = [
        { label: "Uzytkownicy", value: summary.userCount, footnote: "Zdefiniowani odbiorcy powiadomien" },
        { label: "Reguly", value: summary.ruleCount, footnote: `Aktywne: ${summary.enabledRuleCount}` },
        { label: "Aktywacje", value: summary.activeMatchCount, footnote: "Biezace dopasowania do komunikatow" },
        { label: "Progi", value: summary.escalatedMatchCount, footnote: "Ile aktywacji przekroczylo prog" },
        { label: "Wysylki", value: summary.dispatchCount, footnote: "Symulowane SMS i e-mail" }
    ];

    container.innerHTML = cards
        .map((card) => `
            <article class="metric-card">
                <span>${escapeHtml(card.label)}</span>
                <strong>${escapeHtml(String(card.value))}</strong>
                <small>${escapeHtml(card.footnote)}</small>
            </article>
        `)
        .join("");
}

function renderRulesList(rules, users) {
    if (!rules.length) {
        rulesList.innerHTML = '<p class="empty-state">Brak zapisanych regul. Dodaj pierwsze mapowanie po prawej stronie.</p>';
        return;
    }

    const usersById = Object.fromEntries(users.map((user) => [user.id, user]));

    rulesList.innerHTML = rules
        .map((rule) => {
            const recipients = (rule.recipientIds ?? [])
                .map((id) => usersById[id]?.displayName)
                .filter(Boolean);

            return `
                <article class="stack-item">
                    <div class="rule-card-header">
                        <div>
                            <strong>${escapeHtml(rule.name)}</strong>
                            <p class="detail-copy">${escapeHtml(rule.description || "Brak opisu operacyjnego.")}</p>
                        </div>
                        <div class="inline-actions">
                            <button class="action-button ghost" type="button" data-rule-action="edit" data-rule-id="${escapeHtml(rule.id)}">Edytuj</button>
                            <button class="action-button danger" type="button" data-rule-action="delete" data-rule-id="${escapeHtml(rule.id)}">Usun</button>
                        </div>
                    </div>
                    <div class="chip-row">
                        <span class="chip mono">${escapeHtml(rule.matchText)}</span>
                        <span class="chip">${escapeHtml(translateMessageType(rule.messageType))}</span>
                        <span class="chip">prog: ${escapeHtml(formatHours(rule.thresholdHours))}</span>
                        <span class="chip">${escapeHtml(channelLabel(rule))}</span>
                        <span class="chip">${rule.enabled ? "aktywna" : "wylaczona"}</span>
                    </div>
                    <p class="subtle-copy">Odbiorcy: ${escapeHtml(recipients.length ? recipients.join(", ") : "brak")}</p>
                </article>
            `;
        })
        .join("");
}

function renderUsersList(users) {
    if (!users.length) {
        usersList.innerHTML = '<p class="empty-state">Lista odbiorcow jest pusta.</p>';
        return;
    }

    usersList.innerHTML = users
        .map((user) => `
            <article class="stack-item">
                <div class="rule-card-header">
                    <div>
                        <strong>${escapeHtml(user.displayName)}</strong>
                        <p class="detail-copy">${escapeHtml(user.role)}</p>
                    </div>
                    <div class="inline-actions">
                        <button class="action-button ghost" type="button" data-user-action="edit" data-user-id="${escapeHtml(user.id)}">Edytuj</button>
                        <button class="action-button danger" type="button" data-user-action="delete" data-user-id="${escapeHtml(user.id)}">Usun</button>
                    </div>
                </div>
                <div class="chip-row">
                    <span class="chip">${escapeHtml(user.email || "brak e-maila")}</span>
                    <span class="chip">${escapeHtml(user.phone || "brak telefonu")}</span>
                </div>
            </article>
        `)
        .join("");
}

function renderActiveRuleMatches(matches) {
    const container = document.getElementById("active-rule-matches");
    if (!matches.length) {
        container.innerHTML = '<p class="empty-state">Brak aktywnych dopasowan do regul.</p>';
        return;
    }

    container.innerHTML = matches
        .map((match) => `
            <article class="stack-item">
                <div class="status-row">
                    <span class="level-pill ${match.thresholdReached ? "alarm" : "info"}">${match.thresholdReached ? "prog przekroczony" : "obserwacja"}</span>
                    <span class="chip">${escapeHtml(match.port)}</span>
                    <span class="chip">${escapeHtml(match.alias)}</span>
                </div>
                <strong>${escapeHtml(match.ruleName)}</strong>
                <p class="detail-copy">${escapeHtml(match.lastMessage)}</p>
                <div class="chip-row">
                    <span class="chip">${escapeHtml(translateMessageType(match.messageType))}</span>
                    <span class="chip">trwa: ${escapeHtml(formatHours(match.elapsedHours))}</span>
                    <span class="chip">prog: ${escapeHtml(formatHours(match.thresholdHours))}</span>
                    <span class="chip">do: ${escapeHtml(relativeTime(match.dueAtUtc))}</span>
                </div>
                <p class="subtle-copy">
                    Kanaly: ${escapeHtml(channelListLabel(match.channels))}. Odbiorcy: ${escapeHtml((match.recipients ?? []).join(", ") || "brak")}.
                </p>
            </article>
        `)
        .join("");
}

function renderDispatchLog(dispatches) {
    const container = document.getElementById("dispatch-log");
    if (!dispatches.length) {
        container.innerHTML = '<p class="empty-state">Nie bylo jeszcze symulowanych wysylek.</p>';
        return;
    }

    container.innerHTML = dispatches
        .map((dispatch) => `
            <article class="stack-item">
                <div class="status-row">
                    <span class="level-pill ${dispatch.channel === "sms" ? "warn" : "info"}">${escapeHtml(channelDisplay(dispatch.channel))}</span>
                    <span class="chip">${escapeHtml(dispatch.recipientName)}</span>
                    <span class="chip">${escapeHtml(formatDateTime(dispatch.triggeredAtUtc))}</span>
                </div>
                <strong>${escapeHtml(dispatch.ruleName)}</strong>
                <p class="detail-copy">${escapeHtml(dispatch.alias)} / ${escapeHtml(dispatch.port)}</p>
                <p>${escapeHtml(dispatch.message)}</p>
                <p class="subtle-copy">Adres docelowy: ${escapeHtml(dispatch.recipientAddress || "brak")}</p>
            </article>
        `)
        .join("");
}

function renderRuleRecipientOptions(selectedIds = []) {
    const container = document.getElementById("rule-recipient-list");
    const users = rulebookState?.users ?? [];

    if (!users.length) {
        container.innerHTML = '<p class="empty-state">Dodaj najpierw odbiorce systemu.</p>';
        return;
    }

    const selectedSet = new Set(selectedIds);
    container.innerHTML = users
        .map((user) => `
            <label class="selection-item">
                <input type="checkbox" value="${escapeHtml(user.id)}" ${selectedSet.has(user.id) ? "checked" : ""}>
                <span>
                    <strong>${escapeHtml(user.displayName)}</strong>
                    <small>${escapeHtml(user.role)} | ${escapeHtml(user.email || user.phone || "brak danych kontaktowych")}</small>
                </span>
            </label>
        `)
        .join("");
}

async function onRuleFormSubmit(event) {
    event.preventDefault();
    if (!rulebookState) {
        return;
    }

    try {
        const nextRule = collectRuleFormValue();
        const existingIndex = rulebookState.rules.findIndex((rule) => rule.id === nextRule.id);
        if (existingIndex >= 0) {
            rulebookState.rules.splice(existingIndex, 1, nextRule);
        } else {
            rulebookState.rules.unshift(nextRule);
        }

        await persistRulebook("Regula zostala zapisana.");
        resetRuleForm();
    } catch (error) {
        setRulebookStatus(error.message || "Nie udalo sie zapisac reguly.");
    }
}

async function onUserFormSubmit(event) {
    event.preventDefault();
    if (!rulebookState) {
        return;
    }

    try {
        const nextUser = collectUserFormValue();
        const existingIndex = rulebookState.users.findIndex((user) => user.id === nextUser.id);
        if (existingIndex >= 0) {
            rulebookState.users.splice(existingIndex, 1, nextUser);
        } else {
            rulebookState.users.unshift(nextUser);
        }

        await persistRulebook("Uzytkownik zostal zapisany.");
        resetUserForm();
    } catch (error) {
        setRulebookStatus(error.message || "Nie udalo sie zapisac uzytkownika.");
    }
}

async function onRulesListClick(event) {
    const button = event.target.closest("button[data-rule-action]");
    if (!button || !rulebookState) {
        return;
    }

    const rule = rulebookState.rules.find((item) => item.id === button.dataset.ruleId);
    if (!rule) {
        return;
    }

    if (button.dataset.ruleAction === "edit") {
        fillRuleForm(rule);
        setRulebookStatus(`Edytujesz regule: ${rule.name}`);
        return;
    }

    rulebookState.rules = rulebookState.rules.filter((item) => item.id !== rule.id);
    await persistRulebook(`Regula "${rule.name}" zostala usunieta.`);
    resetRuleForm();
}

async function onUsersListClick(event) {
    const button = event.target.closest("button[data-user-action]");
    if (!button || !rulebookState) {
        return;
    }

    const user = rulebookState.users.find((item) => item.id === button.dataset.userId);
    if (!user) {
        return;
    }

    if (button.dataset.userAction === "edit") {
        fillUserForm(user);
        setRulebookStatus(`Edytujesz uzytkownika: ${user.displayName}`);
        return;
    }

    rulebookState.users = rulebookState.users.filter((item) => item.id !== user.id);
    rulebookState.rules = rulebookState.rules.map((rule) => ({
        ...rule,
        recipientIds: (rule.recipientIds ?? []).filter((recipientId) => recipientId !== user.id)
    }));

    await persistRulebook(`Uzytkownik "${user.displayName}" zostal usuniety.`);
    resetUserForm();
}

async function persistRulebook(successMessage) {
    if (!rulebookState) {
        return;
    }

    rulebookDirty = false;

    try {
        const response = await fetch(rulebookUrl, {
            method: "PUT",
            headers: {
                "Content-Type": "application/json"
            },
            body: JSON.stringify({
                users: rulebookState.users,
                rules: rulebookState.rules
            })
        });

        if (!response.ok) {
            throw new Error(`HTTP ${response.status}`);
        }

        const state = await response.json();
        renderRulebook(state);
        setRulebookStatus(successMessage);
    } catch (error) {
        rulebookDirty = true;
        console.error(error);
        setRulebookStatus("Nie udalo sie zapisac zmian w rulebook.");
    }
}

function collectRuleFormValue() {
    const name = document.getElementById("rule-name").value.trim();
    const matchText = document.getElementById("rule-match-text").value.trim();

    if (!name) {
        throw new Error("Podaj nazwe biznesowa dla reguly.");
    }

    if (!matchText) {
        throw new Error("Podaj wzorzec surowego komunikatu.");
    }

    return {
        id: document.getElementById("rule-id").value || createId("rule", `${name}-${matchText}`),
        name,
        matchText,
        messageType: document.getElementById("rule-message-type").value || "any",
        description: document.getElementById("rule-description").value.trim(),
        thresholdHours: Number.parseFloat(document.getElementById("rule-threshold").value || "0"),
        sendSms: document.getElementById("rule-send-sms").checked,
        sendEmail: document.getElementById("rule-send-email").checked,
        recipientIds: getSelectedRecipientIds(),
        enabled: document.getElementById("rule-enabled").checked
    };
}

function collectUserFormValue() {
    const displayName = document.getElementById("user-display-name").value.trim();
    if (!displayName) {
        throw new Error("Podaj imie i nazwisko uzytkownika.");
    }

    return {
        id: document.getElementById("user-id").value || createId("user", displayName),
        displayName,
        role: document.getElementById("user-role").value.trim() || "Operator",
        email: document.getElementById("user-email").value.trim(),
        phone: document.getElementById("user-phone").value.trim()
    };
}

function fillRuleForm(rule) {
    document.getElementById("rule-id").value = rule.id;
    document.getElementById("rule-name").value = rule.name;
    document.getElementById("rule-match-text").value = rule.matchText;
    document.getElementById("rule-message-type").value = rule.messageType || "any";
    document.getElementById("rule-description").value = rule.description || "";
    document.getElementById("rule-threshold").value = String(rule.thresholdHours ?? 0);
    document.getElementById("rule-send-sms").checked = Boolean(rule.sendSms);
    document.getElementById("rule-send-email").checked = Boolean(rule.sendEmail);
    document.getElementById("rule-enabled").checked = Boolean(rule.enabled);
    renderRuleRecipientOptions(rule.recipientIds ?? []);
    rulebookDirty = false;
}

function fillUserForm(user) {
    document.getElementById("user-id").value = user.id;
    document.getElementById("user-display-name").value = user.displayName;
    document.getElementById("user-role").value = user.role || "";
    document.getElementById("user-email").value = user.email || "";
    document.getElementById("user-phone").value = user.phone || "";
    rulebookDirty = false;
}

function resetRuleForm() {
    ruleForm.reset();
    document.getElementById("rule-id").value = "";
    document.getElementById("rule-threshold").value = "5";
    document.getElementById("rule-message-type").value = "any";
    document.getElementById("rule-send-email").checked = true;
    document.getElementById("rule-enabled").checked = true;
    renderRuleRecipientOptions([]);
    rulebookDirty = false;
}

function resetUserForm() {
    userForm.reset();
    document.getElementById("user-id").value = "";
    rulebookDirty = false;
}

function getSelectedRecipientIds() {
    return [...document.querySelectorAll('#rule-recipient-list input[type="checkbox"]:checked')]
        .map((input) => input.value);
}

function markRulebookDirty() {
    rulebookDirty = true;
}

function updateBadges(overview, devices, alerts, groups, history, predictions, lte, pncDevices, mainboards) {
    badgeTargets.overview.textContent = "live";
    badgeTargets.alerts.textContent = alerts.length;
    badgeTargets.devices.textContent = devices.length;
    badgeTargets.groups.textContent = groups.length;
    badgeTargets.history.textContent = history.length;
    badgeTargets.prediction.textContent = predictions.length;
    badgeTargets.lte.textContent = badgeTextForStatus(lte?.status);
    badgeTargets.pnc.textContent = pncDevices?.length ?? 0;
    badgeTargets.wizard.textContent = fleetState?.pncDevices?.length ?? (pncDevices?.length ?? 0);
    badgeTargets.board.textContent = (mainboards ?? []).filter((board) => board.status !== "online").length;
    if (!otaState) {
        badgeTargets.ota.textContent = "0";
    }
    if (!rulebookState) {
        badgeTargets.rules.textContent = String(overview.activeRuleCount ?? 0);
    }
}

function setPortalStatus(kind, title, copy) {
    const status = document.getElementById("portal-status");
    const statusCopy = document.getElementById("portal-status-copy");
    status.textContent = title;
    status.className = `status-strong ${kind}`;
    statusCopy.textContent = copy;
}

function setRulebookStatus(copy) {
    document.getElementById("rulebook-status").textContent = copy;
}

function setFleetStatus(copy) {
    document.getElementById("fleet-status").textContent = copy;
}

function setOtaStatus(copy) {
    document.getElementById("ota-status").textContent = copy;
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
        minute: "2-digit",
        second: "2-digit"
    });
}

function formatHours(value) {
    const numeric = Number(value) || 0;
    return `${numeric.toFixed(numeric % 1 === 0 ? 0 : 1)} h`;
}

function channelDisplay(channel) {
    return String(channel).toLowerCase() === "sms" ? "SMS" : "E-mail";
}

function channelLabel(rule) {
    const channels = [];
    if (rule.sendSms) {
        channels.push("SMS");
    }
    if (rule.sendEmail) {
        channels.push("e-mail");
    }
    return channels.length ? channels.join(" + ") : "mapowanie bez wysylki";
}

function channelListLabel(channels) {
    if (!channels?.length) {
        return "brak";
    }

    return channels.map(channelDisplay).join(" + ");
}

function badgeTextForStatus(status) {
    switch (String(status ?? "").toLowerCase()) {
        case "critical":
            return "alarm";
        case "attention":
            return "uwaga";
        default:
            return "ok";
    }
}

function mapRoadmapStatus(status) {
    switch (status) {
        case "active":
            return "info";
        case "building":
            return "warn";
        case "prototype":
            return "warn";
        default:
            return "alarm";
    }
}

function translateRoadmapStatus(status) {
    switch (status) {
        case "active":
            return "aktywne";
        case "building":
            return "w budowie";
        case "prototype":
            return "prototyp";
        default:
            return "nastepne";
    }
}

function riskToLevel(riskLabel) {
    switch (String(riskLabel).toLowerCase()) {
        case "high":
            return "alarm";
        case "medium":
            return "warn";
        default:
            return "info";
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

function translateMessageType(messageType) {
    switch (String(messageType ?? "").toLowerCase()) {
        case "alarm":
            return "Alarm";
        case "warn":
            return "Ostrzezenie";
        case "error":
            return "Blad";
        case "info":
            return "Informacja";
        default:
            return "Dowolny";
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

function statusClassForCampaign(status) {
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

function defaultProtocolForInterface(interfaceType) {
    switch (String(interfaceType ?? "").toLowerCase()) {
        case "can":
            return "CANopen";
        case "ethernet":
            return "TCP/IP";
        case "digital-input":
        case "digital-output":
            return "GPIO";
        default:
            return "MODBUS RTU";
    }
}

function createId(prefix, seed) {
    const normalized = String(seed)
        .toLowerCase()
        .replaceAll(/[^a-z0-9]+/g, "-")
        .replaceAll(/(^-|-$)/g, "");

    return `${prefix}-${normalized || Date.now()}`;
}

function createPncCode(seed) {
    const normalized = String(seed)
        .toUpperCase()
        .replaceAll(/[^A-Z0-9]+/g, "-")
        .replaceAll(/(^-|-$)/g, "");

    return `PNC-${normalized || Date.now()}`;
}

function toLocalInputValue(value) {
    if (!value) {
        return "";
    }

    const date = new Date(value);
    const offsetMs = date.getTimezoneOffset() * 60 * 1000;
    return new Date(date.getTime() - offsetMs).toISOString().slice(0, 16);
}

function cloneFleetState(state) {
    return {
        lte: { ...(state.lte ?? {}) },
        pncDevices: [...(state.pncDevices ?? [])].map((device) => ({
            ...device,
            connections: [...(device.connections ?? [])].map((connection) => ({ ...connection }))
        }))
    };
}

function cloneRulebookState(state) {
    return {
        summary: { ...(state.summary ?? {}) },
        users: [...(state.users ?? [])].map((user) => ({ ...user })),
        rules: [...(state.rules ?? [])].map((rule) => ({
            ...rule,
            recipientIds: [...(rule.recipientIds ?? [])]
        })),
        activeMatches: [...(state.activeMatches ?? [])].map((match) => ({ ...match })),
        dispatches: [...(state.dispatches ?? [])].map((dispatch) => ({ ...dispatch }))
    };
}

function cloneOtaState(state) {
    return {
        summary: { ...(state.summary ?? {}) },
        packages: [...(state.packages ?? [])].map((pkg) => ({ ...pkg })),
        serviceRecipients: [...(state.serviceRecipients ?? [])].map((recipient) => ({ ...recipient })),
        campaigns: [...(state.campaigns ?? [])].map((campaign) => ({
            ...campaign,
            targetDeviceCodes: [...(campaign.targetDeviceCodes ?? [])],
            targetLabels: [...(campaign.targetLabels ?? [])],
            recipientIds: [...(campaign.recipientIds ?? [])],
            recipientEmails: [...(campaign.recipientEmails ?? [])]
        })),
        logs: [...(state.logs ?? [])].map((log) => ({ ...log })),
        emailLogs: [...(state.emailLogs ?? [])].map((email) => ({
            ...email,
            recipients: [...(email.recipients ?? [])]
        }))
    };
}

function escapeHtml(value) {
    return String(value)
        .replaceAll("&", "&amp;")
        .replaceAll("<", "&lt;")
        .replaceAll(">", "&gt;")
        .replaceAll('"', "&quot;")
        .replaceAll("'", "&#39;");
}

setView(currentView);
resetRuleForm();
resetUserForm();
resetPncForm();
resetConnectionForm();
resetOtaPackageForm();
resetOtaCampaignForm();
refreshPortal();
refreshRulebook(true);
refreshFleet(true);
refreshOta(true);
setInterval(refreshPortal, refreshMs);
