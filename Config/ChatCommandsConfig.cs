using BepInEx.Configuration;

namespace ChatCommands;

internal static class ChatCommandsConfig
{
    private const string HistorySection = "History";

    private const string MaxLinesKey = "MaxLines";
    private const int MaxLinesDefault = 10;
    private const string MaxLinesDescription = "Maximum history lines shown above input.";

    private const string VisibleSecondsKey = "VisibleSeconds";
    private const float VisibleSecondsDefault = 5f;
    private const string VisibleSecondsDescription = "How long history remains visible after submit.";

    private const string FadeSecondsKey = "FadeSeconds";
    private const float FadeSecondsDefault = 0.35f;
    private const string FadeSecondsDescription = "Fade-out duration in seconds.";

    private const string BackgroundAlphaKey = "BackgroundAlpha";
    private const float BackgroundAlphaDefault = 0.35f;
    private const string BackgroundAlphaDescription = "Background alpha for history area (0-1).";

    internal static ConfigEntry<int> BindHistoryMaxLines(ConfigFile config)
    {
        return config.Bind(HistorySection, MaxLinesKey, MaxLinesDefault, MaxLinesDescription);
    }

    internal static ConfigEntry<float> BindHistoryVisibleSeconds(ConfigFile config)
    {
        return config.Bind(HistorySection, VisibleSecondsKey, VisibleSecondsDefault, VisibleSecondsDescription);
    }

    internal static ConfigEntry<float> BindHistoryFadeSeconds(ConfigFile config)
    {
        return config.Bind(HistorySection, FadeSecondsKey, FadeSecondsDefault, FadeSecondsDescription);
    }

    internal static ConfigEntry<float> BindHistoryBackgroundAlpha(ConfigFile config)
    {
        return config.Bind(HistorySection, BackgroundAlphaKey, BackgroundAlphaDefault, BackgroundAlphaDescription);
    }
}
