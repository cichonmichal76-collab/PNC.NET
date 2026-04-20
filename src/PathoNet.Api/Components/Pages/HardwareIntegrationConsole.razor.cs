namespace PathoNet.Api.Components.Pages;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using PathoNet.Api.Components.Shared;
using PathoNet.Contracts;

public partial class HardwareIntegrationConsole
{
    [CascadingParameter] private Task<AuthenticationState> AuthenticationStateTask { get; set; } = default!;

    private static readonly HardwareWizardStepDefinition[] WizardSteps = HardwareIntegrationWizardSupport.WizardSteps;
    private static readonly HardwareCommissioningProgressStepDefinition[] CommissioningProgressSteps = HardwareIntegrationWizardSupport.CommissioningProgressSteps;

    private HardwareIntegrationStateRecord? _state;
    private CollectorHardwareConfigStateRecord? _configState;
    private bool _loading = true;
    private string? _error;
    private string? _statusMessage;
    private string _statusTone = "info";
    private bool _canManage;
    private bool _canRestartCollector;
    private string? _selectedInterfaceType;
    private string? _selectedPortId;
    private string? _suggestedPortId;
    private string? _selectedProfileName;
    private bool _personalizationInitialized;
    private HardwarePersonalizationFormModel _personalizationForm = HardwarePersonalizationFormModel.CreateEmpty();
    private HardwarePortFormModel _portForm = HardwarePortFormModel.CreateEmpty();
    private HardwareWizardStep _wizardStep = HardwareWizardStep.SelectPort;
    private HardwareDeploymentStage _deploymentStage = HardwareDeploymentStage.Startup;
    private bool _testingSignal;
    private HardwarePortSignalTestResultRecord? _signalTestResult;

    private CollectorPortConfigRecord? SelectedPortConfig =>
        _configState?.Ports.FirstOrDefault(port => string.Equals(port.PortId, _selectedPortId, StringComparison.OrdinalIgnoreCase));

    private HardwarePortStatusRecord? SelectedRuntimePort =>
        _state?.Ports.FirstOrDefault(port => string.Equals(port.PortId, _selectedPortId, StringComparison.OrdinalIgnoreCase));

    private HardwarePortStatusRecord? SuggestedRuntimePort =>
        _state?.Ports.FirstOrDefault(port => string.Equals(port.PortId, _suggestedPortId, StringComparison.OrdinalIgnoreCase));

    private HardwareSelfCheckStateRecord? SelfCheck =>
        _state?.SelfCheck;

    private HardwarePersonalizationStateRecord? PersonalizationState =>
        _state?.Personalization;

    private IReadOnlyList<HardwareLocationOptionRecord> PersonalizationLocations =>
        PersonalizationState?.Locations ?? [];

    private IReadOnlyList<string> ProvinceOptions =>
        PersonalizationLocations
            .Select(static location => location.Province)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static province => province, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private IReadOnlyList<string> CityOptions =>
        PersonalizationLocations
            .Where(location => string.Equals(location.Province, _personalizationForm.Province, StringComparison.OrdinalIgnoreCase))
            .Select(static location => location.City)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static city => city, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private IReadOnlyList<string> HospitalOptions =>
        PersonalizationLocations
            .Where(location =>
                string.Equals(location.Province, _personalizationForm.Province, StringComparison.OrdinalIgnoreCase)
                && string.Equals(location.City, _personalizationForm.City, StringComparison.OrdinalIgnoreCase))
            .Select(static location => location.Hospital)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static hospital => hospital, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private IReadOnlyList<string> SiteOptions =>
        PersonalizationLocations
            .Where(location =>
                string.Equals(location.Province, _personalizationForm.Province, StringComparison.OrdinalIgnoreCase)
                && string.Equals(location.City, _personalizationForm.City, StringComparison.OrdinalIgnoreCase)
                && string.Equals(location.Hospital, _personalizationForm.Hospital, StringComparison.OrdinalIgnoreCase))
            .Select(static location => location.Site)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static site => site, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private int PassedSelfCheckCount =>
        SelfCheck?.Items.Count(static item => item.Passed) ?? 0;

    private int RequiredSelfCheckCount =>
        SelfCheck?.RequiredCount ?? 0;

    private int PassedRequiredSelfCheckCount =>
        SelfCheck?.PassedRequiredCount ?? 0;

    private int SelfCheckProgressPercent =>
        RequiredSelfCheckCount == 0
            ? 0
            : (int)Math.Round((double)PassedRequiredSelfCheckCount / RequiredSelfCheckCount * 100);

    private bool SelfCheckCompleted =>
        SelfCheck?.Completed == true;

    private int SignalProgressPercent =>
        SelfCheck?.Items.FirstOrDefault(static item => item.Key == "signal")?.ProgressPercent ?? 0;

    private string SignalProgressLabel =>
        SelfCheck?.Items.FirstOrDefault(static item => item.Key == "signal")?.ProgressLabel ?? "n/d";

    private string SignalProgressClass =>
        SignalProgressPercent switch
        {
            >= 70 => "good",
            >= 35 => "medium",
            _ => "poor"
        };

    protected override async Task OnInitializedAsync() =>
        await LoadAsync();

    private async Task LoadAsync()
    {
        _loading = true;
        _error = null;

        try
        {
            var principal = (await AuthenticationStateTask).User;
            _canManage = principal.HasPermission(PathoNetPermissions.PncManage);
            _canRestartCollector = principal.HasPermission(PathoNetPermissions.HealthManage);
            _state = await PortalService.GetHardwareIntegrationStateAsync(CancellationToken.None);
            _configState = PortalService.GetCollectorConfigState();

            if (_configState.Ports.Length > 0)
            {
                var selectedExists = _configState.Ports.Any(port => string.Equals(port.PortId, _selectedPortId, StringComparison.OrdinalIgnoreCase));
                _suggestedPortId = ComputeSuggestedPortId();
                _selectedPortId = selectedExists
                    ? _selectedPortId
                    : _suggestedPortId ?? _configState.Ports[0].PortId;
                SyncSelectedInterfaceType();
                SyncPortForm();
            }
            else
            {
                _selectedInterfaceType = null;
                _selectedPortId = null;
                _suggestedPortId = null;
                _selectedProfileName = null;
                _signalTestResult = null;
                _portForm = HardwarePortFormModel.CreateEmpty();
                _wizardStep = HardwareWizardStep.SelectPort;
            }

            if (!_personalizationInitialized)
            {
                InitializePersonalizationForm(principal);
            }

            if (_deploymentStage == HardwareDeploymentStage.Startup && SelfCheckCompleted)
            {
                _statusMessage = $"Kontrola systemu zakonczona powodzeniem. PASS: {PassedRequiredSelfCheckCount}/{RequiredSelfCheckCount}. Mozesz przejsc dalej.";
                _statusTone = "online";
            }
        }
        catch (Exception ex)
        {
            _error = $"Nie udalo sie zaladowac kreatora podlaczenia: {ex.Message}";
        }
        finally
        {
            _loading = false;
        }
    }

    private void SelectPort(string portId)
    {
        if (string.IsNullOrWhiteSpace(portId))
        {
            return;
        }

        _selectedPortId = portId;
        SyncSelectedInterfaceType();
        _selectedProfileName = null;
        _signalTestResult = null;
        SyncPortForm();
    }

    private void SelectInterfaceType(string interfaceType)
    {
        if (string.IsNullOrWhiteSpace(interfaceType))
        {
            return;
        }

        _selectedInterfaceType = interfaceType;

        if (SelectedPortConfig is not null
            && string.Equals(SelectedPortConfig.InterfaceType, interfaceType, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var nextPortId = _configState is null
            ? null
            : HardwareIntegrationWizardSupport.FindFirstPortIdForInterface(_configState.Ports, interfaceType, _suggestedPortId);

        if (!string.IsNullOrWhiteSpace(nextPortId))
        {
            SelectPort(nextPortId);
        }
    }

    private void ProceedToPersonalization()
    {
        if (!SelfCheckCompleted)
        {
            _statusMessage = "Najpierw zakoncz kontrole systemu PNC. Przejscie dalej jest dostepne dopiero po statusie PASS.";
            _statusTone = "critical";
            return;
        }

        _deploymentStage = HardwareDeploymentStage.Personalization;
        _statusMessage = "Kontrola systemu zakonczona. Uzupelnij teraz dane urzadzenia i lokalizacji instalacji.";
        _statusTone = "online";
    }

    private void ReturnToStartup() =>
        _deploymentStage = HardwareDeploymentStage.Startup;

    private void ProceedToPortCommissioning()
    {
        if (!SelfCheckCompleted)
        {
            _statusMessage = "Najpierw zakoncz kontrole systemu PNC. Przejscie dalej jest dostepne dopiero po statusie PASS.";
            _statusTone = "critical";
            return;
        }

        if (!ValidatePersonalizationForm())
        {
            _statusMessage = "Uzupelnij dane personalizacji urzadzenia przed przejsciem dalej.";
            _statusTone = "critical";
            return;
        }

        _deploymentStage = HardwareDeploymentStage.PortCommissioning;
        _statusMessage = "Personalizacja zapisana. Wybierz teraz typ interfejsu i port do konfiguracji.";
        _statusTone = "online";
    }

    private void ResetPortForm()
    {
        SyncPortForm();
        _selectedProfileName = null;
    }

    private void SyncPortForm()
    {
        _portForm = SelectedPortConfig is null
            ? HardwarePortFormModel.CreateEmpty()
            : HardwarePortFormModel.FromConfig(SelectedPortConfig);
    }

    private void SyncSelectedInterfaceType()
    {
        _selectedInterfaceType =
            SelectedPortConfig?.InterfaceType
            ?? SelectedRuntimePort?.InterfaceType
            ?? _selectedInterfaceType;
    }

    private async Task SavePortAsync()
    {
        if (!EnsureCanManage())
        {
            return;
        }

        var result = await SavePortCoreAsync();
        _statusMessage = result.Message;
        _statusTone = result.Success ? "online" : "critical";

        if (result.Success)
        {
            await LoadAsync();
            _statusMessage = $"{result.Message} Konfiguracja zostala zapisana i commissioning mozna uznac za zakonczony.";
            _statusTone = "online";
        }
    }

    private async Task SaveAndRestartPortAsync()
    {
        if (!EnsureCanManage() || !EnsureCanRestartCollector())
        {
            return;
        }

        var saveResult = await SavePortCoreAsync();
        if (!saveResult.Success)
        {
            _statusMessage = saveResult.Message;
            _statusTone = "critical";
            return;
        }

        var principal = (await AuthenticationStateTask).User;
        var restartResult = await PortalService.RequestServiceRestartAsync(
            "PathoNet.Collector",
            principal.Identity?.Name ?? principal.FindFirst(PathoNetClaimTypes.DisplayName)?.Value ?? "hardware-integration",
            CancellationToken.None);

        _statusMessage = restartResult.Accepted
            ? $"{saveResult.Message} Restart collectora zostal zlecony."
            : $"{saveResult.Message} Restart collectora nie zostal przyjety: {restartResult.Message}";
        _statusTone = restartResult.Accepted ? "online" : "critical";

        await LoadAsync();

        if (restartResult.Accepted)
        {
            _statusMessage = $"{saveResult.Message} Restart collectora zostal zlecony. Commissioning mozna zakonczc po potwierdzeniu pracy na nowych nastawach.";
            _statusTone = "online";
        }
    }

    private Task<BlazorMutationResult> SavePortCoreAsync() =>
        PortalService.SaveCollectorPortAsync(
            new BlazorCollectorPortInputRecord(
                _portForm.PortId,
                _portForm.Alias,
                _portForm.InterfaceType,
                _portForm.DevicePath,
                _portForm.NetworkInterfaceName,
                _portForm.BaudRate,
                _portForm.DataBits,
                _portForm.Parity,
                _portForm.StopBits,
                _portForm.ParserKind,
                _portForm.FrameMode,
                _portForm.Enabled,
                _portForm.AllowSimulationFallback,
                _portForm.Description),
            CancellationToken.None);

    private async Task TestSignalAsync()
    {
        if (string.IsNullOrWhiteSpace(_selectedPortId))
        {
            _statusMessage = "Najpierw wybierz port do testu sygnalu.";
            _statusTone = "critical";
            return;
        }

        _testingSignal = true;

        try
        {
            _signalTestResult = await PortalService.TestHardwarePortSignalAsync(_selectedPortId, CancellationToken.None);
            _statusMessage = _signalTestResult.Summary;
            _statusTone = _signalTestResult.Tone;
        }
        catch (Exception ex)
        {
            _statusMessage = $"Nie udalo sie wykonac testu sygnalu: {ex.Message}";
            _statusTone = "critical";
        }
        finally
        {
            _testingSignal = false;
        }
    }

    private void NextStep()
    {
        if (!CanMoveNext())
        {
            return;
        }

        _wizardStep = _wizardStep switch
        {
            HardwareWizardStep.SelectPort => HardwareWizardStep.ChooseProfile,
            HardwareWizardStep.ChooseProfile => HardwareWizardStep.Configure,
            HardwareWizardStep.Configure => HardwareWizardStep.Verify,
            HardwareWizardStep.Verify => HardwareWizardStep.Commit,
            _ => _wizardStep
        };
    }

    private void PreviousStep()
    {
        _wizardStep = _wizardStep switch
        {
            HardwareWizardStep.Commit => HardwareWizardStep.Verify,
            HardwareWizardStep.Verify => HardwareWizardStep.Configure,
            HardwareWizardStep.Configure => HardwareWizardStep.ChooseProfile,
            HardwareWizardStep.ChooseProfile => HardwareWizardStep.SelectPort,
            _ => _wizardStep
        };
    }

    private bool CanMoveNext() =>
        _wizardStep switch
        {
            HardwareWizardStep.SelectPort => SelectedPortConfig is not null,
            HardwareWizardStep.ChooseProfile => SelectedPortConfig is not null,
            HardwareWizardStep.Configure => ValidatePortForm(),
            HardwareWizardStep.Verify => SelectedPortConfig is not null,
            _ => false
        };

    private bool CanJumpToStep(HardwareWizardStep step) =>
        step <= _wizardStep
        || step == HardwareWizardStep.ChooseProfile && SelectedPortConfig is not null
        || step == HardwareWizardStep.Configure && SelectedPortConfig is not null
        || step == HardwareWizardStep.Verify && ValidatePortForm()
        || step == HardwareWizardStep.Commit && ValidatePortForm();

    private void JumpToStep(HardwareWizardStep step)
    {
        if (CanJumpToStep(step))
        {
            _wizardStep = step;
        }
    }

    private bool ValidatePortForm() =>
        !string.IsNullOrWhiteSpace(_portForm.PortId)
        && !string.IsNullOrWhiteSpace(_portForm.Alias)
        && !string.IsNullOrWhiteSpace(_portForm.DevicePath)
        && _portForm.BaudRate is >= 1200 and <= 921600
        && _portForm.DataBits is >= 5 and <= 8;

    private bool ValidatePersonalizationForm() =>
        !string.IsNullOrWhiteSpace(_personalizationForm.SerialNumber)
        && !string.IsNullOrWhiteSpace(_personalizationForm.Province)
        && !string.IsNullOrWhiteSpace(_personalizationForm.City)
        && !string.IsNullOrWhiteSpace(_personalizationForm.Hospital)
        && !string.IsNullOrWhiteSpace(_personalizationForm.Site)
        && !string.IsNullOrWhiteSpace(_personalizationForm.InstallerName)
        && !string.IsNullOrWhiteSpace(_personalizationForm.Variant);

    private void InitializePersonalizationForm(System.Security.Claims.ClaimsPrincipal principal)
    {
        var personalization = PersonalizationState;
        var firstLocation = personalization?.Locations.FirstOrDefault();
        var installerName =
            principal.Identity?.Name
            ?? principal.FindFirst(PathoNetClaimTypes.DisplayName)?.Value
            ?? "Admin";

        _personalizationForm = new HardwarePersonalizationFormModel
        {
            SerialNumber = personalization?.DetectedDeviceCode ?? string.Empty,
            Province = firstLocation?.Province ?? string.Empty,
            City = firstLocation?.City ?? string.Empty,
            Hospital = firstLocation?.Hospital ?? string.Empty,
            Site = firstLocation?.Site ?? string.Empty,
            DetectedSimNumber = personalization?.DetectedSimNumber ?? "n/d",
            DetectedSoftwareVersion = personalization?.DetectedSoftwareVersion ?? "n/d",
            DetectedBoardSerialNumber = personalization?.DetectedBoardSerialNumber ?? "n/d",
            InstallerName = installerName,
            InstallationDate = DateTime.Today,
            Variant = personalization?.VariantOptions.FirstOrDefault() ?? string.Empty
        };

        NormalizePersonalizationSelection();
        _personalizationInitialized = true;
    }

    private void NormalizePersonalizationSelection()
    {
        if (ProvinceOptions.Count > 0 && !ProvinceOptions.Contains(_personalizationForm.Province, StringComparer.OrdinalIgnoreCase))
        {
            _personalizationForm.Province = ProvinceOptions[0];
        }

        if (CityOptions.Count > 0 && !CityOptions.Contains(_personalizationForm.City, StringComparer.OrdinalIgnoreCase))
        {
            _personalizationForm.City = CityOptions[0];
        }

        if (HospitalOptions.Count > 0 && !HospitalOptions.Contains(_personalizationForm.Hospital, StringComparer.OrdinalIgnoreCase))
        {
            _personalizationForm.Hospital = HospitalOptions[0];
        }

        if (SiteOptions.Count > 0 && !SiteOptions.Contains(_personalizationForm.Site, StringComparer.OrdinalIgnoreCase))
        {
            _personalizationForm.Site = SiteOptions[0];
        }
    }

    private void OnSerialNumberChanged(ChangeEventArgs args) =>
        _personalizationForm.SerialNumber = args.Value?.ToString()?.Trim() ?? string.Empty;

    private void OnProvinceChanged(ChangeEventArgs args)
    {
        _personalizationForm.Province = args.Value?.ToString() ?? string.Empty;
        _personalizationForm.City = string.Empty;
        _personalizationForm.Hospital = string.Empty;
        _personalizationForm.Site = string.Empty;
        NormalizePersonalizationSelection();
    }

    private void OnCityChanged(ChangeEventArgs args)
    {
        _personalizationForm.City = args.Value?.ToString() ?? string.Empty;
        _personalizationForm.Hospital = string.Empty;
        _personalizationForm.Site = string.Empty;
        NormalizePersonalizationSelection();
    }

    private void OnHospitalChanged(ChangeEventArgs args)
    {
        _personalizationForm.Hospital = args.Value?.ToString() ?? string.Empty;
        _personalizationForm.Site = string.Empty;
        NormalizePersonalizationSelection();
    }

    private void OnSiteChanged(ChangeEventArgs args) =>
        _personalizationForm.Site = args.Value?.ToString() ?? string.Empty;

    private void OnInstallerChanged(ChangeEventArgs args) =>
        _personalizationForm.InstallerName = args.Value?.ToString()?.Trim() ?? string.Empty;

    private void OnVariantChanged(ChangeEventArgs args) =>
        _personalizationForm.Variant = args.Value?.ToString() ?? string.Empty;

    private void OnInstallationDateChanged(ChangeEventArgs args)
    {
        if (DateTime.TryParse(args.Value?.ToString(), out var date))
        {
            _personalizationForm.InstallationDate = date;
        }
    }

    private string? ComputeSuggestedPortId() =>
        _state?.Ports
            .OrderByDescending(HardwareIntegrationWizardSupport.GetPortPriority)
            .Select(static port => port.PortId)
            .FirstOrDefault();

    private bool EnsureCanManage()
    {
        if (_canManage)
        {
            return true;
        }

        _statusMessage = $"Brak uprawnienia {PathoNetPermissions.PncManage}.";
        _statusTone = "critical";
        return false;
    }

    private bool EnsureCanRestartCollector()
    {
        if (_canRestartCollector)
        {
            return true;
        }

        _statusMessage = $"Brak uprawnienia {PathoNetPermissions.HealthManage} do restartu collectora.";
        _statusTone = "critical";
        return false;
    }

    private void ApplyProfile(HardwarePortProfileDefinition profile)
    {
        _portForm.InterfaceType = profile.InterfaceType;
        _portForm.BaudRate = profile.BaudRate;
        _portForm.DataBits = profile.DataBits;
        _portForm.Parity = profile.Parity;
        _portForm.StopBits = profile.StopBits;
        _portForm.ParserKind = profile.ParserKind;
        _portForm.FrameMode = profile.FrameMode;
        _portForm.AllowSimulationFallback = profile.AllowSimulationFallback;
        _portForm.Description = profile.Description;

        if (!string.IsNullOrWhiteSpace(profile.AliasHint))
        {
            _portForm.Alias = profile.AliasHint;
        }

        if (!string.IsNullOrWhiteSpace(profile.DevicePathHint))
        {
            _portForm.DevicePath = profile.DevicePathHint;
        }

        if (!string.IsNullOrWhiteSpace(profile.NetworkInterfaceNameHint))
        {
            _portForm.NetworkInterfaceName = profile.NetworkInterfaceNameHint;
        }

        _selectedProfileName = profile.Name;
        _statusMessage = $"Wczytano profil {profile.Name}. Zweryfikuj dane portu przed zapisem onsite.";
        _statusTone = "info";
        _wizardStep = HardwareWizardStep.Configure;
    }

    private static IReadOnlyList<HardwarePortProfileDefinition> GetProfilesForPort(CollectorPortConfigRecord port) =>
        HardwareIntegrationWizardSupport.GetProfilesForPort(port);

    private static string MapTone(string value) =>
        HardwareIntegrationWizardSupport.MapTone(value);

    private static bool IsPortConnected(HardwarePortStatusRecord port) =>
        HardwareIntegrationWizardSupport.IsPortConnected(port);
    private bool IsCurrentCommissioningStep(HardwareCommissioningProgressStepDefinition step)
    {
        if (_deploymentStage != step.Stage)
        {
            return false;
        }

        if (step.Stage != HardwareDeploymentStage.PortCommissioning)
        {
            return true;
        }

        return step.WizardStep == _wizardStep;
    }

    private bool IsCompletedCommissioningStep(HardwareCommissioningProgressStepDefinition step)
    {
        if (_deploymentStage > step.Stage)
        {
            return true;
        }

        if (_deploymentStage < step.Stage)
        {
            return false;
        }

        if (step.Stage != HardwareDeploymentStage.PortCommissioning || step.WizardStep is null)
        {
            return false;
        }

        return _wizardStep > step.WizardStep;
    }

}

internal sealed class HardwarePersonalizationFormModel
{
    public string SerialNumber { get; set; } = string.Empty;
    public string Province { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Hospital { get; set; } = string.Empty;
    public string Site { get; set; } = string.Empty;
    public string DetectedSimNumber { get; set; } = "n/d";
    public string DetectedSoftwareVersion { get; set; } = "n/d";
    public string DetectedBoardSerialNumber { get; set; } = "n/d";
    public string InstallerName { get; set; } = string.Empty;
    public DateTime InstallationDate { get; set; } = DateTime.Today;
    public string Variant { get; set; } = string.Empty;

    public static HardwarePersonalizationFormModel CreateEmpty() => new();
}
