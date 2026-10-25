using System;
using System.Reflection;

namespace ChatCommands;

public sealed partial class ChatCommands
{
    private static bool TryRelayPlainChatToMultiplayer(string line, out string displayLine)
    {
        displayLine = line;

        try
        {
            var pluginType = FindTypeByName("MineMogulMultiplayer.Plugin");
            var instanceProperty = pluginType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            var pluginInstance = instanceProperty?.GetValue(null);
            if (pluginInstance == null)
            {
                return false;
            }

            var trySendMethod = pluginType!.GetMethod("TrySendChatMessage", BindingFlags.Public | BindingFlags.Instance);
            if (trySendMethod == null)
            {
                return false;
            }

            var result = trySendMethod.Invoke(pluginInstance, new object[] { line });
            if (result is not bool sent || !sent)
            {
                return false;
            }

            displayLine = $"You: {line}";
            return true;
        }
        catch (Exception ex)
        {
            _log?.LogWarning($"{ModInfo.LOG_PREFIX} Failed to relay plain chat through MineMogulMultiplayer: {ex.Message}");
            return false;
        }
    }
}
