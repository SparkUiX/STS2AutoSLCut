namespace AutoSLCut;

internal static class AutoSLCutSettings
{
    // Positive values move load markers later; negative values move them earlier.
    public static double LoadMarkerOffsetMilliseconds { get; set; } = 0d;

    // Dedupes repeated load signals from multiple hook points in the same flow.
    public static int LoadDedupWindowMilliseconds { get; set; } = 250;

    public static bool EnableVerboseLogs { get; set; } = true;

    public static bool EnableObsWebSocket { get; set; } = true;

    public static string ObsWebSocketUrl { get; set; } = "ws://127.0.0.1:4455";

    public static string ObsWebSocketPassword { get; set; } = string.Empty;

    public static int ObsReconnectDelayMilliseconds { get; set; } = 5000;

    public static string ClipDataOutputPath { get; set; } = "user://autoslcut/clip_timeline.json";
}
