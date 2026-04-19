internal sealed class TissueProcessorPredictionService(
    PredictionDatasetService predictionDatasetService)
{
    public PortalPredictionAnalysisRecord BuildAnalysis(
        PortalDeviceRecord[] devices,
        PortalAlertRecord[] alerts,
        HistoryEventRecord[] history,
        ActivityBucketRecord[] activity)
    {
        var targetLabels =
        new[]
        {
            new PortalPredictionTargetRecord(
                Code: "alarm_in_next_30m",
                Title: "Alarm w kolejnych 30 minutach",
                Horizon: "30 min",
                Priority: "MVP",
                Summary: "Najlepszy pierwszy target do wdrozenia, bo wprost wspiera dashboard serwisowy i szybka reakcje technika."),
            new PortalPredictionTargetRecord(
                Code: "batch_failure",
                Title: "Niepowodzenie lub przerwanie batcha",
                Horizon: "biezacy cykl",
                Priority: "Etap 2",
                Summary: "Wymaga danych o recipe i batchach, ale daje bardzo mocna wartosc biznesowa dla laboratorium."),
            new PortalPredictionTargetRecord(
                Code: "service_intervention_24h",
                Title: "Interwencja serwisowa w 24 godziny",
                Horizon: "24 h",
                Priority: "Etap 2",
                Summary: "Dobry target operacyjny po zebraniu historii alarmow, restartow i wynikow interwencji.")
        };

        var predictions = BuildPredictions(devices, alerts, history, activity);
        var dataset = predictionDatasetService.CaptureAndGetState(targetLabels, devices, alerts, history, activity);

        return new PortalPredictionAnalysisRecord(
            RecommendedModel: new PortalPredictionModelRecommendationRecord(
                ModelName: "XGBoost",
                DeploymentMode: "serwer centralny / scoring batch + online",
                ProblemType: "klasyfikacja tablicowa dla alarmow i ryzyka batcha",
                InferenceWindow: "30 min dla alarmu, 24 h dla interwencji serwisowej",
                Summary: "Dla danych RS-232 z procesora tkankowego najlepszym pierwszym modelem wdrozeniowym jest XGBoost: dobrze pracuje na mieszanych cechach statusowych, alarmowych i telemetrycznych, jest szybki po stronie serwera i pozostaje wyjasnialny operacyjnie.",
                Reasons:
                [
                    "Dane wejsciowe beda tablicowe: status, krok programu, czas, temperatura, vacuum, alarmy i recipe.",
                    "Model dobrze radzi sobie z malymi oraz srednimi zbiorami i brakami danych po normalizacji ramek.",
                    "Mozna go uruchomic jako lekki scoring serwerowy bez obciazania PNC.",
                        "Latwiej uzasadnic wynik niz przy LSTM lub Transformerze, co jest wazne dla serwisu i traceability."
                ]),
            TargetLabels: targetLabels,
            FeatureGroups:
            [
                new PortalPredictionFeatureGroupRecord(
                    Name: "Status procesu",
                    Purpose: "Opisuje w jakim punkcie cyklu jest urzadzenie i czy proces przebiega zgodnie z recipe.",
                    Features:
                    [
                        "process_state",
                        "current_step",
                        "program_id",
                        "elapsed_time_sec",
                        "time_left_sec"
                    ]),
                new PortalPredictionFeatureGroupRecord(
                    Name: "Termika i vacuum",
                    Purpose: "Pokazuje odchylenia fizyczne, ktore najczesciej poprzedzaja alarm lub nieudany batch.",
                    Features:
                    [
                        "chamber_temp_c",
                        "temp_delta_to_recipe",
                        "vacuum_state",
                        "pressure_state",
                        "rotation_state"
                    ]),
                new PortalPredictionFeatureGroupRecord(
                    Name: "Alarmy i zdarzenia",
                    Purpose: "Buduje presje alarmowa i kontekst bezposrednio poprzedzajacy blad.",
                    Features:
                    [
                        "alarm_count_5m",
                        "alarm_count_60m",
                        "pause_count_24h",
                        "door_open_flag",
                        "last_alarm_code"
                    ]),
                new PortalPredictionFeatureGroupRecord(
                    Name: "Recipe i batch",
                    Purpose: "Laczy stan procesu z realnym obciazeniem wsadu i oczekiwanym przebiegiem programu.",
                    Features:
                    [
                        "recipe_id",
                        "reagent_index",
                        "step_duration_delta",
                        "batch_size",
                        "cassette_count"
                    ]),
                new PortalPredictionFeatureGroupRecord(
                    Name: "Telemetria techniczna",
                    Purpose: "Daje sygnaly o degradacji samego urzadzenia, zanim pojawi sie alarm produkcyjny.",
                    Features:
                    [
                        "pump_status",
                        "valve_state",
                        "cycle_counter",
                        "service_counter",
                        "power_state"
                    ])
            ],
            NormalizationPipeline:
            [
                new PortalPredictionNormalizationStepRecord(
                    Step: "1. Ramka",
                    Input: "RAW RS232 / sniff / request-response",
                    Output: "zdekodowana ramka producenta",
                    Summary: "Najpierw przechwytujemy surowy tekst lub binarna ramke i walidujemy framing oraz checksumy."),
                new PortalPredictionNormalizationStepRecord(
                    Step: "2. JSON",
                    Input: "zdekodowana ramka producenta",
                    Output: "wspolny JSON PathoNet",
                    Summary: "Mapujemy vendor-specific komunikat na wspolny model: status, krok, czas, temperatura, alarm, batch."),
                new PortalPredictionNormalizationStepRecord(
                    Step: "3. Wektor cech",
                    Input: "strumien JSON + okna czasowe",
                    Output: "feature vector do modelu",
                    Summary: "Budujemy cechy w oknach 5 min / 60 min / 24 h oraz odchylenia od recipe i historii batcha."),
                new PortalPredictionNormalizationStepRecord(
                    Step: "4. Scoring",
                    Input: "feature vector",
                    Output: "risk score + rekomendacja",
                    Summary: "Serwer odpala scoring XGBoost i zwraca prawdopodobienstwo, horyzont oraz zalecana akcje.")
            ],
            Dataset: dataset,
            Predictions: predictions);
    }

    private static PredictionRecord[] BuildPredictions(
        PortalDeviceRecord[] devices,
        PortalAlertRecord[] alerts,
        HistoryEventRecord[] history,
        ActivityBucketRecord[] activity)
    {
        var alertsByPort = alerts
            .GroupBy(alert => alert.Port, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        var historyByPort = history
            .GroupBy(item => item.Port, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);

        var fleetPressure = activity.TakeLast(4).Sum(bucket => (bucket.AlarmCount * 7) + (bucket.WarnCount * 3));

        return devices
            .Select(device =>
            {
                var alertCount = alertsByPort.GetValueOrDefault(device.Port, 0);
                var historyItems = historyByPort.GetValueOrDefault(device.Port, []);
                var recentAlarmHistory = historyItems.Count(item => item.Level is "alarm" or "error");
                var recentWarnHistory = historyItems.Count(item => item.Level == "warn");

                var probability = Math.Clamp(
                    device.RiskScore
                    + (alertCount * 8)
                    + (recentAlarmHistory * 6)
                    + (recentWarnHistory * 3)
                    + (device.ThresholdReached ? 10 : 0)
                    + Math.Min(fleetPressure, 12),
                    18,
                    97);

                var riskLabel = probability switch
                {
                    >= 75 => "high",
                    >= 45 => "medium",
                    _ => "low"
                };

                var horizon = probability switch
                {
                    >= 80 => "30 min",
                    >= 60 => "6 h",
                    _ => "24 h"
                };

                var summary = $"{device.DisplayName} na {device.Port} ma {alertCount} aktywnych alertow, poziom {device.CurrentLevel} i zdrowie {device.HealthScore}%. Wektor pod XGBoost powinien uwzglednic status procesu, temperature, vacuum i presje alarmowa.";

                return new PredictionRecord(
                    Alias: device.Alias,
                    DisplayName: device.DisplayName,
                    Port: device.Port,
                    RiskLabel: riskLabel,
                    Probability: probability,
                    Horizon: horizon,
                    Summary: summary,
                    Recommendation: device.Recommendation);
            })
            .OrderByDescending(prediction => prediction.Probability)
            .ThenBy(prediction => prediction.Port, StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToArray();
    }
}
