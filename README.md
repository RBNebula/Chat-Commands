# Chat Commands

Shared in-game command bar for MineMogul mods.

## Behavior

- Press `Enter` to open.
- Press `/` to open with `/` prefilled.
- Type a command like `/mymod print success`.
- Press `Enter` to dispatch.
- Press `Esc` to close.
- Type `/help` to show registered prefixes.
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

bool PublishInfo(string message);
bool PublishInfo(string message, out string error);

bool PublishError(string message);
bool PublishError(string message, out string error);
```

Notes:
- `handler` receives everything after `/<prefix>`.
- Example: `/mymod print success` -> handler args are `print success`.
- All calls return `false` if the host is not ready.

## Full Mod Example

This plugin registers `/mymod`, supports `/mymod print success`, and logs `Command execution successful.` to the BepInEx log.

```csharp
using System;
using BepInEx;
using ChatCommands;

[BepInPlugin("com.mymod", "My Mod", "1.0.0")]
[BepInDependency("com.chatcommands", BepInDependency.DependencyFlags.HardDependency)]
public sealed class MyModPlugin : BaseUnityPlugin
{
    private void Awake()
    {
        if (!ChatCommandsApi.IsAvailable)
        {
            Logger.LogWarning("ChatCommands API is not available yet.");
            return;
        }

        if (!ChatCommandsApi.RegisterPrefix(
                prefix: "mymod",
                owner: "com.mymod",
                handler: HandleMyModCommand,
                error: out var registerError,
                description: "My mod command root"))
        {
            Logger.LogError($"Failed to register /mymod: {registerError}");
            return;
        }

        if (!ChatCommandsApi.SetCommands("mymod", new[]
            {
                "/mymod print success"
            }, out var setCommandsError))
        {
            Logger.LogWarning($"Failed to set /mymod autocomplete commands: {setCommandsError}");
        }
    }

    private void OnDestroy()
    {
        ChatCommandsApi.UnregisterPrefix("mymod");
    }

    private void HandleMyModCommand(string args)
    {
        var normalized = (args ?? string.Empty).Trim();

        if (string.Equals(normalized, "print success", StringComparison.OrdinalIgnoreCase))
        {
            Logger.LogInfo("Command execution successful.");
            ChatCommandsApi.PublishInfo("Command execution successful.");
            return;
        }

        ChatCommandsApi.PublishError("Unknown mymod command. Try: /mymod print success");
    }
}
```
