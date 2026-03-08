using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace ChatCommands;

public sealed partial class ChatCommands
{
    private const string InternalPrefix = "chatcommands";
    private const string InternalPrefixDescription = "Chat Commands settings";

    private static readonly ChatCommandsApi.CommandDefinition[] InternalCommandDefinitions =
    {
        new("/chatcommands historymax <value>", "Set maximum history lines (1-40)."),
        new("/chatcommands historyvisible <seconds>", "Set history visible duration (>= 0)."),
        new("/chatcommands historyfadeduration <seconds>", "Set history fade duration (>= 0.05)."),
        new("/chatcommands historybackgroundalpha <0-1>", "Set history background alpha (0-1).")
    };

    private void RegisterInternalChatCommands()
    {
        if (!RegisterPrefix(
                prefix: InternalPrefix,
                owner: ModInfo.PLUGIN_GUID,
                handler: HandleInternalChatCommand,
                description: InternalPrefixDescription,
                error: out var registerError))
        {
            _log?.LogWarning($"{ModInfo.LOG_PREFIX} Failed to register /{InternalPrefix}: {registerError}");
            return;
        }

        if (!SetCommands(InternalPrefix, InternalCommandDefinitions, out var setCommandsError))
        {
            _log?.LogWarning($"{ModInfo.LOG_PREFIX} Failed to set /{InternalPrefix} commands: {setCommandsError}");
        }
    }

    private void HandleInternalChatCommand(string args)
    {
        var tokens = (args ?? string.Empty)
            .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

        if (tokens.Length != 2)
        {
            PublishInternalUsage();
            return;
        }

        var command = tokens[0].Trim();
        var valueToken = tokens[1].Trim();

        if (string.Equals(command, "historymax", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryParseInt(valueToken, out var parsed))
            {
                PushFeedbackLine("Invalid value for historymax. Example: /chatcommands historymax 10", isError: true);
                return;
            }

            var value = Mathf.Clamp(parsed, 1, 40);
            _historyMaxLines!.Value = value;
            SaveConfigAndPublish($"History.MaxLines set to {value}.");
            return;
        }

        if (string.Equals(command, "historyvisible", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryParseFloat(valueToken, out var parsed))
            {
                PushFeedbackLine("Invalid value for historyvisible. Example: /chatcommands historyvisible 5", isError: true);
                return;
            }

            var value = Math.Max(0f, parsed);
            _historyVisibleSeconds!.Value = value;
            SaveConfigAndPublish($"History.VisibleSeconds set to {value:0.###}.");
            return;
        }

        if (string.Equals(command, "historyfadeduration", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryParseFloat(valueToken, out var parsed))
            {
                PushFeedbackLine("Invalid value for historyfadeduration. Example: /chatcommands historyfadeduration 0.35", isError: true);
                return;
            }

            var value = Math.Max(0.05f, parsed);
            _historyFadeSeconds!.Value = value;
            SaveConfigAndPublish($"History.FadeSeconds set to {value:0.###}.");
            return;
        }

        if (string.Equals(command, "historybackgroundalpha", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryParseFloat(valueToken, out var parsed))
            {
                PushFeedbackLine("Invalid value for historybackgroundalpha. Example: /chatcommands historybackgroundalpha 0.35", isError: true);
                return;
            }

            var value = Mathf.Clamp(parsed, 0f, 1f);
            _historyBackgroundAlpha!.Value = value;
            SaveConfigAndPublish($"History.BackgroundAlpha set to {value:0.###}.");
            return;
        }

        PublishInternalUsage();
    }

    private void PublishInternalUsage()
    {
        PushFeedbackLine("Usage: /chatcommands historymax|historyvisible|historyfadeduration|historybackgroundalpha <value>", isError: true);
    }

    private void SaveConfigAndPublish(string successMessage)
    {
        Config.Save();
        PushFeedbackLine(successMessage, isError: false);
    }

    private static bool TryParseInt(string value, out int parsed)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)
               || int.TryParse(value, out parsed);
    }

    private static bool TryParseFloat(string value, out float parsed)
    {
        return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)
               || float.TryParse(value, out parsed);
    }

    private void Dispatch(string line)
    {
        if (IsHelpCommand(line))
        {
            DispatchHelp(line);
            return;
        }

        if (!TryParse(line, out var prefix, out var args))
        {
            const string msg = "Command format: /prefix args";
            _log?.LogInfo($"{ModInfo.LOG_PREFIX} {msg}");
            PushFeedbackLine(msg, isError: true);
            return;
        }

        if (!Registrations.TryGetValue(prefix, out var registration))
        {
            var msg = $"/{prefix} is not a recognized mod prefix. Please re-enter your command and try again.";
            _log?.LogInfo($"{ModInfo.LOG_PREFIX} {msg}");
            PushFeedbackLine(msg, isError: true);
            return;
        }

        try
        {
            registration.Handler(args);
        }
        catch (Exception ex)
        {
            _log?.LogError($"{ModInfo.LOG_PREFIX} Handler for '/{registration.Prefix}' failed: {ex}");
            PushFeedbackLine($"/{registration.Prefix} handler failed. Check BepInEx logs.", isError: true);
            return;
        }

        _log?.LogInfo($"{ModInfo.LOG_PREFIX} Dispatched '/{registration.Prefix}' to {registration.Owner}");
    }

    private static bool IsHelpCommand(string line)
    {
        if (!line.StartsWith("/help", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return line.Length == 5 || char.IsWhiteSpace(line[5]);
    }

    private void DispatchHelp(string line)
    {
        var suffix = line.Length > 5 ? line.Substring(5).Trim() : string.Empty;
        if (suffix.Length == 0)
        {
            var helpLines = BuildHelpLines();
            for (var i = 0; i < helpLines.Count; i++)
            {
                _log?.LogInfo($"{ModInfo.LOG_PREFIX} {helpLines[i]}");
                PushFeedbackLine(helpLines[i], isError: false);
            }

            return;
        }

        var firstToken = suffix.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)[0];
        var normalizedPrefix = NormalizePrefix(firstToken);
        if (normalizedPrefix.Length == 0 || !Registrations.TryGetValue(normalizedPrefix, out var registration))
        {
            var missing = normalizedPrefix.Length == 0 ? firstToken : normalizedPrefix;
            var msg = $"/help {missing}: prefix not found.";
            _log?.LogInfo($"{ModInfo.LOG_PREFIX} {msg}");
            PushFeedbackLine(msg, isError: true);
            return;
        }

        var lines = BuildPrefixHelpLines(registration);
        for (var i = 0; i < lines.Count; i++)
        {
            _log?.LogInfo($"{ModInfo.LOG_PREFIX} {lines[i]}");
            PushFeedbackLine(lines[i], isError: false);
        }
    }

    private static bool TryParse(string line, out string prefix, out string args)
    {
        prefix = string.Empty;
        args = string.Empty;

        if (!line.StartsWith("/", StringComparison.Ordinal))
        {
            return false;
        }

        var body = line.Substring(1).Trim();
        if (body.Length == 0)
        {
            return false;
        }

        var space = body.IndexOf(' ');
        if (space < 0)
        {
            prefix = body;
            return true;
        }

        prefix = body.Substring(0, space).Trim();
        args = body.Substring(space + 1).Trim();
        return prefix.Length > 0;
    }

    private List<string> BuildHelpLines()
    {
        if (Registrations.Count == 0)
        {
            return new List<string> { "No command prefixes registered yet." };
        }

        var lines = new List<string>(Registrations.Count);
        foreach (var reg in Registrations.Values)
        {
            var label = $"/help {reg.Prefix}";
            if (!string.IsNullOrWhiteSpace(reg.Description))
            {
                label += $" - {reg.Description}";
            }

            lines.Add(label);
        }

        return lines;
    }

    private static List<string> BuildPrefixHelpLines(Registration registration)
    {
        var lines = new List<string>();
        var header = $"/{registration.Prefix} ({registration.Owner})";
        if (!string.IsNullOrWhiteSpace(registration.Description))
        {
            header += $" - {registration.Description}";
        }
        lines.Add(header);

        for (var i = 0; i < registration.Commands.Count; i++)
        {
            var cmd = registration.Commands[i];
            if (string.Equals(cmd, "/" + registration.Prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (registration.CommandDescriptions.TryGetValue(cmd, out var commandDesc) &&
                !string.IsNullOrWhiteSpace(commandDesc))
            {
                lines.Add($"{cmd} - {commandDesc}");
            }
            else
            {
                lines.Add(cmd);
            }
        }

        return lines;
    }

    internal static bool RegisterPrefix(string prefix, string owner, Action<string> handler, string description, out string error)
    {
        error = string.Empty;

        var normalized = NormalizePrefix(prefix);
        if (normalized.Length == 0)
        {
            error = "Prefix cannot be empty.";
            return false;
        }

        if (handler == null)
        {
            error = "Handler cannot be null.";
            return false;
        }

        if (Registrations.ContainsKey(normalized))
        {
            error = $"Prefix '/{normalized}' is already registered.";
            return false;
        }

        var registration = new Registration
        {
            Prefix = normalized,
            Owner = string.IsNullOrWhiteSpace(owner) ? "UnknownMod" : owner.Trim(),
            Description = description?.Trim() ?? string.Empty,
            Handler = handler
        };

        ResetRegistrationCommandCatalog(registration, normalized);
        Registrations[normalized] = registration;

        _log?.LogInfo($"{ModInfo.LOG_PREFIX} Registered '/{normalized}' for {owner}.");
        return true;
    }

    internal static bool SetCommands(string prefix, IEnumerable<string> commands, out string error)
    {
        error = string.Empty;

        var normalized = NormalizePrefix(prefix);
        if (normalized.Length == 0)
        {
            error = "Prefix cannot be empty.";
            return false;
        }

        if (!Registrations.TryGetValue(normalized, out var registration))
        {
            error = $"Prefix '/{normalized}' is not registered.";
            return false;
        }

        ResetRegistrationCommandCatalog(registration, normalized);

        if (commands != null)
        {
            var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "/" + normalized };
            foreach (var raw in commands)
            {
                var cmd = NormalizeCommand(raw, normalized);
                if (cmd.Length == 0)
                {
                    continue;
                }

                if (unique.Add(cmd))
                {
                    registration.Commands.Add(cmd);
                }
            }
        }

        _log?.LogInfo($"{ModInfo.LOG_PREFIX} Updated command catalog for '/{normalized}' with {registration.Commands.Count} entries.");
        return true;
    }

    internal static bool SetCommands(string prefix, IEnumerable<ChatCommandsApi.CommandDefinition> commands, out string error)
    {
        error = string.Empty;

        var normalized = NormalizePrefix(prefix);
        if (normalized.Length == 0)
        {
            error = "Prefix cannot be empty.";
            return false;
        }

        if (!Registrations.TryGetValue(normalized, out var registration))
        {
            error = $"Prefix '/{normalized}' is not registered.";
            return false;
        }

        ResetRegistrationCommandCatalog(registration, normalized);

        if (commands != null)
        {
            var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "/" + normalized };
            foreach (var item in commands)
            {
                var cmd = NormalizeCommand(item.Command, normalized);
                if (cmd.Length == 0)
                {
                    continue;
                }

                if (!unique.Add(cmd))
                {
                    continue;
                }

                registration.Commands.Add(cmd);
                var desc = (item.Description ?? string.Empty).Trim();
                if (desc.Length > 0)
                {
                    registration.CommandDescriptions[cmd] = desc;
                }
            }
        }

        _log?.LogInfo($"{ModInfo.LOG_PREFIX} Updated command catalog for '/{normalized}' with descriptions ({registration.Commands.Count} entries).");
        return true;
    }

    private static void ResetRegistrationCommandCatalog(Registration registration, string normalizedPrefix)
    {
        var rootCommand = "/" + normalizedPrefix;
        registration.Commands.Clear();
        registration.Commands.Add(rootCommand);
        registration.CommandDescriptions.Clear();
        if (!string.IsNullOrWhiteSpace(registration.Description))
        {
            registration.CommandDescriptions[rootCommand] = registration.Description.Trim();
        }
    }

    internal static bool UnregisterPrefix(string prefix)
    {
        var normalized = NormalizePrefix(prefix);
        if (normalized.Length == 0)
        {
            return false;
        }

        var removed = Registrations.Remove(normalized);
        if (removed)
        {
            _log?.LogInfo($"{ModInfo.LOG_PREFIX} Unregistered '/{normalized}'.");
        }

        return removed;
    }

    internal static bool PostHistoryLine(string line, bool isError, out string error)
    {
        error = string.Empty;

        var text = (line ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            error = "History message cannot be empty.";
            return false;
        }

        if (_instance == null)
        {
            error = "Chat Commands host is not ready.";
            return false;
        }

        lock (PendingHistoryLock)
        {
            PendingHistory.Enqueue(new PendingHistoryLine(text, isError));
        }

        return true;
    }

    private static string NormalizePrefix(string prefix)
    {
        var value = (prefix ?? string.Empty).Trim();
        if (value.StartsWith("/", StringComparison.Ordinal))
        {
            value = value.Substring(1);
        }

        return value.Trim();
    }

    private static string NormalizeCommand(string raw, string normalizedPrefix)
    {
        var value = (raw ?? string.Empty).Trim();
        if (value.Length == 0)
        {
            return string.Empty;
        }

        if (!value.StartsWith("/", StringComparison.Ordinal))
        {
            value = "/" + normalizedPrefix + " " + value;
        }

        value = CollapseSpaces(value);

        var prefixToken = "/" + normalizedPrefix;
        if (!value.StartsWith(prefixToken, StringComparison.OrdinalIgnoreCase))
        {
            value = $"{prefixToken} {value.TrimStart('/')}";
            value = CollapseSpaces(value);
        }

        return value;
    }

    private static string CollapseSpaces(string value)
    {
        var pieces = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", pieces);
    }
}
