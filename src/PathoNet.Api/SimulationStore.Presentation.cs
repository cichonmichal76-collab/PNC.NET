internal sealed partial class SimulationStore
{
    private static PortalRoleRecord[] BuildRoles() =>
    [
        new(
            Name: "Admin",
            Focus: "Nadzor nad cala flota, wdrozeniami i kondycja platformy.",
            DefaultView: "Przeglad",
            Capabilities: ["urzadzenia", "grupy", "alerty", "historia", "predykcja", "lte", "pnc", "wizard", "board", "reguly", "ota", "mock", "analiza"]),
        new(
            Name: "Serwis",
            Focus: "Obsluga incydentow, diagnostyka i plan reakcji terenowej.",
            DefaultView: "Alerty",
            Capabilities: ["alerty", "urzadzenia", "historia", "hdmi", "lte", "pnc", "wizard", "board", "reguly", "ota", "mock"]),
        new(
            Name: "Klient",
            Focus: "Widocznosc statusu operacyjnego, czasu pracy i harmonogramu wdrozen bez narzedzi serwisowych.",
            DefaultView: "Przeglad",
            Capabilities: ["przeglad", "urzadzenia", "grupy", "client", "hdmi"])
    ];

    private static RoadmapPhaseRecord[] BuildRoadmap() =>
    [
        new(
            Phase: "Etap 1",
            Title: "Collector + API + baza danych",
            Status: "active",
            Summary: "Collector, ingest API i heartbeat dzialaja juz w lokalnym srodowisku mock."),
        new(
            Phase: "Etap 2",
            Title: "Portal + alerty",
            Status: "building",
            Summary: "Portal biznesowy, centrum alertow i ekrany operatora sa juz dostepne w GUI."),
        new(
            Phase: "Etap 3",
            Title: "Reguly mapowania i eskalacje",
            Status: "active",
            Summary: "Surowe komunikaty mozna mapowac na etykiety biznesowe i progi eskalacji z odbiorcami."),
        new(
            Phase: "Etap 4",
            Title: "Kreator wdrozen PNC",
            Status: "building",
            Summary: "Kreator dodawania wezlow PNC, mapowania portow i monitor plyty glownej sa dostepne w GUI."),
        new(
            Phase: "Etap 5",
            Title: "OTA + panele specjalistyczne",
            Status: "active",
            Summary: "Dostepny jest panel OTA, osobna strona klienta, analiza, mock i panel serwisu."),
        new(
            Phase: "Etap 6",
            Title: "Predykcja",
            Status: "building",
            Summary: "Analiza zdarzen i predykcja sa juz wydzielone i gotowe na podpiete modele ML.")
    ];

    private static string BuildHdmiHeadline(IEnumerable<PortalAlertRecord> alerts, IReadOnlyList<PredictionRecord> predictions)
    {
        var topAlert = alerts.FirstOrDefault();
        if (topAlert is not null)
        {
            return $"{topAlert.DisplayName} wymaga uwagi: {topAlert.Summary}";
        }

        var topPrediction = predictions.FirstOrDefault();
        return topPrediction is null
            ? "System pracuje stabilnie. Brak aktywnej presji alarmowej."
            : $"{topPrediction.DisplayName} pokazuje {topPrediction.RiskLabel.ToLowerInvariant()} poziom ryzyka w horyzoncie {topPrediction.Horizon}.";
    }

    private static int CalculateRiskScore(string currentLevel, int warnCount, int alarmCount, int errorCount)
    {
        var baseScore = currentLevel switch
        {
            "alarm" => 58,
            "error" => 52,
            "warn" => 34,
            _ => 14
        };

        return Math.Clamp(baseScore + (warnCount * 6) + (alarmCount * 12) + (errorCount * 10), 8, 95);
    }

    private static string ComputeTrend(IReadOnlyList<string> levels)
    {
        if (levels.Count < 2)
        {
            return "steady";
        }

        var current = SeverityRank(levels[0]);
        var previous = SeverityRank(levels[1]);

        if (current > previous)
        {
            return "rising";
        }

        if (current < previous)
        {
            return "recovering";
        }

        return "steady";
    }

    private static string RecommendationForLevel(string currentLevel, PortalMessageRuleRecord? rule)
    {
        if (rule is not null)
        {
            return $"Aktywna regula: {rule.Name}. Zweryfikuj odbiorcow i progi eskalacji.";
        }

        return currentLevel switch
        {
            "alarm" => "Wyslij serwis i odizoluj sciezke urzadzenia objeta problemem.",
            "error" => "Eskaluij do analizy inzynierskiej i sprawdz integralnosc danych.",
            "warn" => "Przejrzyj diagnostyke i przygotuj dzialania prewencyjne.",
            _ => "Kontynuuj monitoring i utrzymuj obiekt pod obserwacja."
        };
    }

    private static string ActionForAlert(
        string currentLevel,
        PortalMessageRuleRecord? rule,
        RuleActivationState? state,
        DateTimeOffset now)
    {
        if (rule is not null && state is not null && IsThresholdReached(rule, state, now))
        {
            return $"Uruchom {ResolveChannelLabel(rule)}";
        }

        if (rule is not null)
        {
            return $"Pilnuj progu {FormatHours(rule.ThresholdHours)}";
        }

        return currentLevel switch
        {
            "alarm" => "Reakcja natychmiastowa",
            "error" => "Sprawdz parser i transport",
            "warn" => "Zaplanij przeglad serwisowy",
            _ => "Obserwuj"
        };
    }

    private static string GroupSummary(string groupName, string worstLevel, int deviceCount)
    {
        return worstLevel switch
        {
            "alarm" => $"{groupName} obejmuje {deviceCount} urzadzen i zawiera co najmniej jedna krytyczna sciezke.",
            "warn" => $"{groupName} pokazuje podwyzszona niestabilnosc sygnalow i wymaga przegladu.",
            _ => $"{groupName} pracuje w granicach nominalnych parametrow."
        };
    }

    private static string PredictionSummary(PortalDeviceRecord device)
    {
        return device.CurrentLevel switch
        {
            "alarm" => $"{device.DisplayName} prawdopodobnie wywola kolejna interwencje, jesli biezaca usterka nie zostanie usunieta.",
            "warn" => $"{device.DisplayName} pokazuje wzorzec, ktory moze rozwinac sie w incydent przy kolejnym oknie serwisowym.",
            _ => $"{device.DisplayName} pozostaje stabilny, ale powinien zostac w aktywnej petli monitoringu."
        };
    }

    private static string RiskLabel(int riskScore)
    {
        return riskScore switch
        {
            >= 60 => "High",
            >= 35 => "Medium",
            _ => "Low"
        };
    }

    private static string HorizonForRisk(int riskScore, string currentLevel)
    {
        if (currentLevel == "alarm")
        {
            return "natychmiastowy";
        }

        return riskScore switch
        {
            >= 60 => "najblizsze 6 godzin",
            >= 35 => "najblizsze 12 godzin",
            _ => "najblizsze 24 godziny"
        };
    }

    private static string ResolveGroupName(string port)
    {
        return port switch
        {
            "/dev/ttyEM0" or "/dev/ttyEM1" or "/dev/ttyEM2" => "Opieka Krytyczna",
            "/dev/ttyEM3" or "/dev/ttyEM4" or "/dev/ttyEM5" => "Diagnostyka",
            _ => "Infrastruktura"
        };
    }

    private static string SignalQualityLabel(int signalPercent)
    {
        return signalPercent switch
        {
            >= 75 => "bardzo dobry",
            >= 55 => "dobry",
            >= 35 => "sredni",
            _ => "slaby"
        };
    }

    private static string StatusFromLevel(string currentLevel) =>
        currentLevel switch
        {
            "alarm" => "critical",
            "error" => "critical",
            "warn" => "attention",
            _ => "online"
        };

    private static string GetWorstLevel(IEnumerable<string> levels)
    {
        return levels
            .Select(NormalizeLevel)
            .DefaultIfEmpty("info")
            .OrderByDescending(SeverityRank)
            .First();
    }

    private static int SeverityRank(string level)
    {
        return NormalizeLevel(level) switch
        {
            "alarm" => 4,
            "error" => 3,
            "warn" => 2,
            _ => 1
        };
    }

    private static string NormalizeLevel(string? level)
    {
        return level?.Trim().ToLowerInvariant() switch
        {
            "alarm" => "alarm",
            "warn" => "warn",
            "warning" => "warn",
            "error" => "error",
            _ => "info"
        };
    }
}
