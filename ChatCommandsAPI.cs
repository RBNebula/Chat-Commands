using System;
using System.Collections.Generic;

namespace ChatCommands;

public static class ChatCommandsApi
{
    private static bool _hostReady;

    internal static void SetHost(ChatCommands _)
    {
        _hostReady = true;
    }

    public static bool IsAvailable => _hostReady;

    public static bool RegisterPrefix(string prefix, string owner, Action<string> handler, string description = "")
    {
        if (!_hostReady)
        {
            return false;
        }

        return ChatCommands.RegisterPrefix(prefix, owner, handler, description ?? string.Empty, out _);
    }

    public static bool RegisterPrefix(string prefix, string owner, Action<string> handler, out string error, string description = "")
    {
        if (!_hostReady)
        {
            error = "Chat Commands host is not ready.";
            return false;
        }

        return ChatCommands.RegisterPrefix(prefix, owner, handler, description ?? string.Empty, out error);
    }

    public static bool UnregisterPrefix(string prefix)
    {
        if (!_hostReady)
        {
            return false;
        }

        return ChatCommands.UnregisterPrefix(prefix);
    }

    public static bool SetCommands(string prefix, IEnumerable<string> commands)
    {
        if (!_hostReady)
        {
            return false;
        }

        return ChatCommands.SetCommands(prefix, commands, out _);
    }

    public static bool SetCommands(string prefix, IEnumerable<string> commands, out string error)
    {
        if (!_hostReady)
        {
            error = "Chat Commands host is not ready.";
            return false;
        }

        return ChatCommands.SetCommands(prefix, commands, out error);
    }

    public static bool PublishInfo(string message)
    {
        if (!_hostReady)
        {
            return false;
        }

        return ChatCommands.PostHistoryLine(message, isError: false, out _);
    }

    public static bool PublishInfo(string message, out string error)
    {
        if (!_hostReady)
        {
            error = "Chat Commands host is not ready.";
            return false;
        }

        return ChatCommands.PostHistoryLine(message, isError: false, out error);
    }

    public static bool PublishError(string message)
    {
        if (!_hostReady)
        {
            return false;
        }

        return ChatCommands.PostHistoryLine(message, isError: true, out _);
    }

    public static bool PublishError(string message, out string error)
    {
        if (!_hostReady)
        {
            error = "Chat Commands host is not ready.";
            return false;
        }

        return ChatCommands.PostHistoryLine(message, isError: true, out error);
    }
}
