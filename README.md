# Chat Commands

Shared in-game command bar for MineMogul mods.

## Behavior

- Press `Enter` to open.
- Press `/` to open with `/` prefilled.
- Type a command like `/mymod print success`.
- Press `Enter` to dispatch.
- Press `Esc` to close.
- Type `/help` to show registered prefixes.
- Type `/help <prefix>` (example: `/help mb`) to show that prefix's commands and descriptions.
- Unknown prefixes show an on-screen error line in history.
- Inline autocomplete appears in gray.
- `Tab` accepts/cycles suggestions by command segment.
- `Space` accepts the current gray suggestion segment.
- Shows command history above input (newest at bottom).
- After submit, input closes and history stays visible for a few seconds, then fades out.

## Config

In BepInEx config for this plugin:

- `History.MaxLines` (default `10`)
- `History.VisibleSeconds` (default `5`)
- `History.FadeSeconds` (default `0.35`)
- `History.BackgroundAlpha` (default `0.35`)

## API

Namespace: `ChatCommands`
Type: `ChatCommandsApi`

```csharp
bool IsAvailable { get; }

bool RegisterPrefix(string prefix, string owner, Action<string> handler, string description = "");
bool RegisterPrefix(string prefix, string owner, Action<string> handler, out string error, string description = "");

bool UnregisterPrefix(string prefix);

bool SetCommands(string prefix, IEnumerable<string> commands);
bool SetCommands(string prefix, IEnumerable<string> commands, out string error);

readonly struct CommandDefinition
{
    string Command { get; }
    string Description { get; }
}

bool SetCommands(string prefix, IEnumerable<CommandDefinition> commands);
bool SetCommands(string prefix, IEnumerable<CommandDefinition> commands, out string error);

bool SetAutocompleteCommands(string prefix, IEnumerable<string>? commands);
bool SetAutocompleteCommands(string prefix, IEnumerable<string>? commands, out string error);

bool PublishInfo(string message);
bool PublishInfo(string message, out string error);

bool PublishError(string message);
bool PublishError(string message, out string error);
```

Notes:
- `handler` receives everything after `/<prefix>`.
- Example: `/mymod print success` -> handler args are `print success`.
- All calls return `false` if the host is not ready.
- `CommandDefinition.Description` is optional, and appears in `/help <prefix>` output when provided.
- `SetCommands(...)` continues to drive both `/help <prefix>` and autocomplete by default.
- `SetAutocompleteCommands(...)` lets a mod override autocomplete with a separate dynamic list.
- Passing `null` or an empty list to `SetAutocompleteCommands(...)` clears the override and falls back to the help-command catalog again.

## Full Mod Example

This plugin registers `/minersbp`, keeps a static documented command list for `/help minersbp`, and publishes a separate dynamic autocomplete catalog.

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using ChatCommands;

[BepInPlugin("com.minersbp", "Miners Blueprint", "1.0.0")]
[BepInDependency("com.ChatCommands", BepInDependency.DependencyFlags.HardDependency)]
public sealed class MinersBlueprintPlugin : BaseUnityPlugin
{
    private readonly Dictionary<string, Action> _commands = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ChatCommandsApi.CommandDefinition[] HelpCommands =
    {
        new("/minersbp set pos 1", "Set position 1."),
        new("/minersbp set pos 2", "Set position 2."),
        new("/minersbp copy", "Copy current selection."),
        new("/minersbp paste", "Paste current clipboard."),
        new("/minersbp clear", "Clear selection/preview.")
    };

    // Handling the commands locally
    private void Awake()
    {
        // Check if available
        if (!ChatCommandsApi.IsAvailable)
        {
            Logger.LogWarning("ChatCommands API is not available yet.");
            return;
        }

        _commands["set pos 1"] = HandleSetPos1;
        _commands["set pos 2"] = HandleSetPos2;
        _commands["copy"] = HandleCopy;
        _commands["paste"] = HandlePaste;

        // Registering command prefix
        if (!ChatCommandsApi.RegisterPrefix(
                prefix: "minersbp",
                owner: "com.minersbp",
                handler: ChatCommandHandler,
                error: out var registerError,
                description: "Miners Blueprint commands"))
        {
            Logger.LogError($"Failed to register /minersbp: {registerError}");
            return;
        }

        // Optional command metadata for autocomplete and /help output.
        // This curated list drives /help minersbp.
        ChatCommandsApi.SetCommands("minersbp", HelpCommands, out var setCommandsError);

        if (!string.IsNullOrWhiteSpace(setCommandsError))
        {
            Logger.LogWarning($"Failed to set /minersbp commands: {setCommandsError}");
        }

        RefreshAutocompleteCatalog();
    }

    private void OnDestroy()
    {
        ChatCommandsApi.UnregisterPrefix("minersbp");
    }

    private void ChatCommandHandler(string args)
    {
        var normalized = (args ?? string.Empty).Trim();

        if (_commands.TryGetValue(normalized, out var handler))
        {
            handler();
            return;
        }

        ChatCommandsApi.PublishError("Unknown minersbp command. Type /help minersbp");
    }

    private void RefreshAutocompleteCatalog()
    {
        var blueprintNames = GetBlueprintNames();
        var directions = new[] { "north", "east", "south", "west" };

        var autocompleteCommands = HelpCommands
            .Select(def => def.Command)
            .Concat(blueprintNames.Select(name => $"/minersbp load {name}"))
            .Concat(directions.Select(direction => $"/minersbp paste {direction}"));

        if (!ChatCommandsApi.SetAutocompleteCommands("minersbp", autocompleteCommands, out var autocompleteError) &&
            !string.IsNullOrWhiteSpace(autocompleteError))
        {
            Logger.LogWarning($"Failed to set /minersbp autocomplete commands: {autocompleteError}");
        }
    }

    private static IEnumerable<string> GetBlueprintNames()
    {
        yield return "starter-house";
        yield return "smelter";
    }

    private void HandleSetPos1()
    {
        Logger.LogInfo("set pos 1 command received");
        ChatCommandsApi.PublishInfo("set pos 1 command received");
    }

    private void HandleSetPos2() { /* ... */ }
    private void HandleCopy() { /* ... */ }
    private void HandlePaste() { /* ... */ }
}
```

Migration notes:
- Existing mods can keep using `SetCommands(...)` only. Behavior is unchanged.
- Mods that want curated help plus dynamic autocomplete should keep their documented commands in `SetCommands(...)`, then publish runtime suggestions through `SetAutocompleteCommands(...)`.
