using BepInEx;

namespace ChatCommands;

[BepInPlugin(ModInfo.PLUGIN_GUID, ModInfo.PLUGIN_NAME, ModInfo.PLUGIN_VERSION)]
public sealed partial class ChatCommands : BaseUnityPlugin
{
    private void Awake()
    {
        _log = Logger;
        _instance = this;
        _historyMaxLines = ChatCommandsConfig.BindHistoryMaxLines(Config);
        _historyVisibleSeconds = ChatCommandsConfig.BindHistoryVisibleSeconds(Config);
        _historyFadeSeconds = ChatCommandsConfig.BindHistoryFadeSeconds(Config);
        _historyBackgroundAlpha = ChatCommandsConfig.BindHistoryBackgroundAlpha(Config);
        ChatCommandsApi.SetHost(this);
        RegisterInternalChatCommands();
        Logger.LogInfo($"{ModInfo.LOG_PREFIX} {ModInfo.PLUGIN_NAME} {ModInfo.PLUGIN_VERSION} loaded.");
    }
}
