namespace SharedUI.Services.Settings;

public sealed record AppSettings(
    AppThemeMode ThemeMode = AppThemeMode.System,
    bool PropertiesPanelDefaultOpen = true,
    bool AutoClosePropertiesOnMobileEnter = true,

    // Touch UX
    double TouchMinScale = 0.05,
    double TouchMaxScale = 20.0,
    double TouchPinchExponent = 1.0,

    bool TouchInertiaEnabled = true,
    double TouchInertiaStartSpeed = 0.05,
    double TouchInertiaStopSpeed = 0.01,
    double TouchInertiaDecayPer16ms = 0.92
);
