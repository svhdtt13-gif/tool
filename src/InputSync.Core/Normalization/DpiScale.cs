namespace InputSync.Core.Normalization;

/// <summary>
/// Physical/logical pixel conversion for DPI-aware tracing and mapping.
/// Windows reports hook coordinates in physical pixels while client rects
/// are logical; 96 DPI is the system baseline (100%).
/// </summary>
public static class DpiScale
{
    public const int StandardDpi = 96;

    public static int ToLogical(int physicalPixels, int dpi) =>
        dpi <= 0 ? physicalPixels : (int)Math.Round((double)physicalPixels * StandardDpi / dpi);

    public static int ToPhysical(int logicalPixels, int dpi) =>
        dpi <= 0 ? logicalPixels : (int)Math.Round((double)logicalPixels * dpi / StandardDpi);
}
