namespace DesktopAssist.Util;

/// <summary>
/// Central location for shared literal values (durations, dimensions, grid sizing, etc.).
/// Avoids magic numbers scattered across the codebase and eases future tuning.
/// </summary>
internal static class AppConstants
{
    // Delays / timing
    public const int DefaultStepInterDelayMs = 100;          // Between executed tool steps (legacy scattered Thread.Sleep(100))
    public const int LaunchDialogWarmupMs = 150;             // After Win+R before typing
    public const int KeyTapHoldMs = 10;                      // Down->up gap for simple taps
    public const int DefaultKeyIntervalMs = 20;              // Interval between chars when writing
    public const int MouseMoveSettleMs = 25;                 // After SetCursorPos before click
    public const int MouseClickDefaultIntervalMs = 120;      // Between multi-click sequences

    // Screenshot / overlays
    public const int GridMinorPx = 50;
    public const int GridMajorPx = 200;
    public const int InsetCropSizePx = 160;                  // Region side length around cursor
    public const int InsetScaleFactor = 6;                   // Magnification scale for inset

    // Limits
    public const int MaxSleepSecsCap = 5;                    // Cap for sleep tool
    public const int MaxMouseClicks = 4;                     // Defensive upper bound

    // UI Overlay sizing (minimums)
    public const int OverlayMinWidth = 400;
    public const int OverlayMinHeight = 250;
}
