using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

namespace ChatCommands;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class ChatCommands : BaseUnityPlugin
{
    public const string PluginGuid = "com.chatcommands";
    public const string PluginName = "Chat Commands";
    public const string PluginVersion = "0.1.0";

    private static ManualLogSource? _log;
    private static ChatCommands? _instance;
    private static readonly Dictionary<string, Registration> Registrations = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object PendingHistoryLock = new();
    private static readonly Queue<PendingHistoryLine> PendingHistory = new();
    private const string InputControlName = "ChatCommands_Text";

    private bool _isOpen;
    private bool _focusInput;
    private bool _suppressSubmitThisFrame;
    private bool _gameInputSuppressed;
    private bool _playerInputWasEnabledBeforeSuppression;
    private bool _inputSuppressionWarningLogged;
    private readonly List<MonoBehaviour> _disabledScripts = new();
    private MonoBehaviour? _suppressedUiManagerBehaviour;
    private bool _suppressedUiManagerWasEnabled;
    private string _line = string.Empty;
    private GUIStyle? _chatBoxStyle;
    private GUIStyle? _chatInputStyle;
    private GUIStyle? _historyTextStyle;
    private GUIStyle? _ghostTextStyle;
    private readonly List<string> _history = new();
    private float _historyVisibleUntil;
    private float _historyFadeEndTime;
    private ConfigEntry<int>? _historyMaxLines;
    private ConfigEntry<float>? _historyVisibleSeconds;
    private ConfigEntry<float>? _historyFadeSeconds;
    private ConfigEntry<float>? _historyBackgroundAlpha;
    private string _ghostSuffix = string.Empty;
    private string? _acceptSuggestionLine;
    private bool _lineChangedByCode;
    private string _lastObservedLine = string.Empty;
    private TabCycleState? _tabCycle;

    private sealed class Registration
    {
        public string Prefix { get; set; } = string.Empty;
        public string Owner { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public Action<string> Handler { get; set; } = _ => { };
        public List<string> Commands { get; } = new();
    }

    private readonly struct PendingHistoryLine
    {
        public PendingHistoryLine(string text, bool isError)
        {
            Text = text;
            IsError = isError;
        }

        public string Text { get; }
        public bool IsError { get; }
    }

    private sealed class AutocompleteContext
    {
        public string SeedLine { get; set; } = string.Empty;
        public List<string> SeedTokens { get; set; } = new();
        public bool SeedTrailingSpace { get; set; }
        public int TokenIndex { get; set; }
        public List<string> Options { get; set; } = new();
    }

    private sealed class TabCycleState
    {
        public AutocompleteContext Context { get; set; } = new();
        public List<string> Options { get; set; } = new();
        public int OptionIndex { get; set; }
        public string LastAppliedLine { get; set; } = string.Empty;
    }

    private void Awake()
    {
        _log = Logger;
        _instance = this;
        _historyMaxLines = Config.Bind("History", "MaxLines", 10, "Maximum history lines shown above input.");
        _historyVisibleSeconds = Config.Bind("History", "VisibleSeconds", 5f, "How long history remains visible after submit.");
        _historyFadeSeconds = Config.Bind("History", "FadeSeconds", 0.35f, "Fade-out duration in seconds.");
        _historyBackgroundAlpha = Config.Bind("History", "BackgroundAlpha", 0.35f, "Background alpha for history area (0-1).");
        ChatCommandsApi.SetHost(this);
        Logger.LogInfo($"{PluginName} {PluginVersion} loaded.");
    }

    private void OnDestroy()
    {
        SetGameplayInputSuppressed(suppress: false);

        if (ReferenceEquals(_instance, this))
        {
            _instance = null;
        }
    }

    private void Update()
    {
        DrainPendingHistory();

        if (!_isOpen && IsSlashOpenKeyDown())
        {
            OpenInput(prefillSlash: true);
            return;
        }

        if (!_isOpen && IsSubmitKeyDown())
        {
            OpenInput(prefillSlash: false);
            return;
        }

        if (!_isOpen)
        {
            return;
        }

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            CloseInput();
            return;
        }

        if (_suppressSubmitThisFrame)
        {
            _suppressSubmitThisFrame = false;
            return;
        }

        if (IsSubmitKeyDown())
        {
            SubmitCurrentLine();
        }
    }

    private void OnGUI()
    {
        EnsureStyles();

        const float sidePadding = 20f;
        var boxWidth = Mathf.Min(720f, Screen.width - sidePadding * 2f);
        var boxHeight = 44f;
        var boxX = sidePadding;
        var boxY = Screen.height - 92f;
        var boxRect = new Rect(boxX, boxY, boxWidth, boxHeight);
        var textRect = new Rect(boxX + 12f, boxY + 9f, boxWidth - 24f, 26f);

        DrawHistoryOverlay(boxX, boxY, boxWidth);

        if (!_isOpen)
        {
            return;
        }

        TrySubmitFromGuiEvent();
        if (!_isOpen)
        {
            return;
        }

        var prev = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.72f);
        GUI.Box(boxRect, GUIContent.none, _chatBoxStyle);
        GUI.color = prev;

        GUI.SetNextControlName(InputControlName);
        _line = GUI.TextField(textRect, _line ?? string.Empty, 512, _chatInputStyle);

        if (_focusInput)
        {
            GUI.FocusControl(InputControlName);
            _focusInput = false;
        }

        HandleInputEvents();
        RefreshInlineSuggestion();
        DrawInlineSuggestion(textRect);
    }

    private void TrySubmitFromGuiEvent()
    {
        var evt = Event.current;
        if (evt.rawType != EventType.KeyDown)
        {
            return;
        }

        if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter)
        {
            return;
        }

        evt.Use();

        if (_suppressSubmitThisFrame)
        {
            return;
        }

        SubmitCurrentLine();
    }

    private void HandleInputEvents()
    {
        if (!_lineChangedByCode && !string.Equals(_line, _lastObservedLine, StringComparison.Ordinal))
        {
            _tabCycle = null;
        }

        _lineChangedByCode = false;
        _lastObservedLine = _line;

        var evt = Event.current;
        if (evt.type != EventType.KeyDown)
        {
            return;
        }

        if (evt.keyCode == KeyCode.Tab)
        {
            if (HandleTabAutocomplete())
            {
                evt.Use();
            }

            return;
        }

        if (evt.keyCode == KeyCode.Space)
        {
            if (TryAcceptInlineSuggestion(appendTrailingSpace: true))
            {
                evt.Use();
            }

            _tabCycle = null;
            return;
        }

        if (!IsModifierOnlyKey(evt.keyCode))
        {
            _tabCycle = null;
        }
    }

    private static bool IsModifierOnlyKey(KeyCode key)
    {
        return key == KeyCode.LeftShift ||
               key == KeyCode.RightShift ||
               key == KeyCode.LeftControl ||
               key == KeyCode.RightControl ||
               key == KeyCode.LeftAlt ||
               key == KeyCode.RightAlt;
    }

    private void DrawInlineSuggestion(Rect textRect)
    {
        if (string.IsNullOrEmpty(_ghostSuffix) || _chatInputStyle == null || _ghostTextStyle == null)
        {
            return;
        }

        var typed = _line ?? string.Empty;
        var typedWidth = _chatInputStyle.CalcSize(new GUIContent(typed)).x;
        var x = textRect.x + _chatInputStyle.padding.left + typedWidth + 1f;
        var maxWidth = textRect.xMax - x - 6f;
        if (maxWidth <= 4f)
        {
            return;
        }

        var rect = new Rect(x, textRect.y + 3f, maxWidth, textRect.height);
        GUI.Label(rect, _ghostSuffix, _ghostTextStyle);
    }

    private static bool IsSubmitKeyDown()
    {
        return Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
    }

    private static bool IsSlashOpenKeyDown()
    {
        return Input.GetKeyDown(KeyCode.Slash);
    }

    private void OpenInput(bool prefillSlash)
    {
        _isOpen = true;
        _focusInput = true;
        _suppressSubmitThisFrame = true;
        SetGameplayInputSuppressed(suppress: true);
        _tabCycle = null;

        if (prefillSlash)
        {
            _line = "/";
        }
        else if (string.IsNullOrWhiteSpace(_line))
        {
            _line = string.Empty;
        }
    }

    private void CloseInput()
    {
        _isOpen = false;
        _focusInput = false;
        _tabCycle = null;
        _ghostSuffix = string.Empty;
        _acceptSuggestionLine = null;
        SetGameplayInputSuppressed(suppress: false);
    }

    private void SubmitCurrentLine()
    {
        var line = (_line ?? string.Empty).Trim();
        if (line.Length == 0)
        {
            CloseInput();
            return;
        }

        Dispatch(line);
        PushHistory(line);
        _line = string.Empty;
        CloseInput();
    }

    private void Dispatch(string line)
    {
        if (line.Equals("/help", StringComparison.OrdinalIgnoreCase))
        {
            var text = BuildHelpText();
            _log?.LogInfo($"[ChatCommands] {text}");
            PushFeedbackLine(text, isError: false);
            return;
        }

        if (!TryParse(line, out var prefix, out var args))
        {
            const string msg = "Command format: /prefix args";
            _log?.LogInfo($"[ChatCommands] {msg}");
            PushFeedbackLine(msg, isError: true);
            return;
        }

        if (!Registrations.TryGetValue(prefix, out var registration))
        {
            var msg = $"/{prefix} is not a recognized mod prefix. Please re-enter your command and try again.";
            _log?.LogInfo($"[ChatCommands] {msg}");
            PushFeedbackLine(msg, isError: true);
            return;
        }

        try
        {
            registration.Handler(args);
        }
        catch (Exception ex)
        {
            _log?.LogError($"Handler for '/{registration.Prefix}' failed: {ex}");
            PushFeedbackLine($"/{registration.Prefix} handler failed. Check BepInEx logs.", isError: true);
            return;
        }

        _log?.LogInfo($"[ChatCommands] Dispatched '/{registration.Prefix}' to {registration.Owner}");
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

    private string BuildHelpText()
    {
        if (Registrations.Count == 0)
        {
            return "No command prefixes registered yet.";
        }

        var parts = new List<string>(Registrations.Count);
        foreach (var reg in Registrations.Values)
        {
            var label = $"/{reg.Prefix} ({reg.Owner})";
            if (!string.IsNullOrWhiteSpace(reg.Description))
            {
                label += $" - {reg.Description}";
            }

            parts.Add(label);
        }

        return string.Join(" | ", parts);
    }

    private void EnsureStyles()
    {
        if (_chatBoxStyle == null)
        {
            _chatBoxStyle = new GUIStyle(GUI.skin.textField)
            {
                border = new RectOffset(4, 4, 4, 4),
                padding = new RectOffset(10, 10, 8, 8),
                margin = new RectOffset(0, 0, 0, 0)
            };
        }

        if (_chatInputStyle == null)
        {
            _chatInputStyle = new GUIStyle(GUI.skin.textField)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = 15,
                padding = new RectOffset(8, 8, 4, 4),
                margin = new RectOffset(0, 0, 0, 0),
                normal = { textColor = Color.white },
                focused = { textColor = Color.white },
                hover = { textColor = Color.white },
                active = { textColor = Color.white }
            };
        }

        if (_historyTextStyle == null)
        {
            _historyTextStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.LowerLeft,
                fontSize = 14,
                richText = false,
                wordWrap = false,
                clipping = TextClipping.Clip,
                padding = new RectOffset(4, 4, 0, 0),
                normal = { textColor = new Color(1f, 1f, 1f, 0.94f) }
            };
        }

        if (_ghostTextStyle == null && _chatInputStyle != null)
        {
            _ghostTextStyle = new GUIStyle(_chatInputStyle)
            {
                normal = { textColor = new Color(0.78f, 0.78f, 0.78f, 0.72f) },
                focused = { textColor = new Color(0.78f, 0.78f, 0.78f, 0.72f) },
                hover = { textColor = new Color(0.78f, 0.78f, 0.78f, 0.72f) },
                active = { textColor = new Color(0.78f, 0.78f, 0.78f, 0.72f) }
            };
        }
    }

    private void PushHistory(string line)
    {
        _history.Add(line);

        var maxLines = Mathf.Clamp(_historyMaxLines?.Value ?? 10, 1, 40);
        while (_history.Count > maxLines)
        {
            _history.RemoveAt(0);
        }

        var visibleSeconds = Mathf.Max(0f, _historyVisibleSeconds?.Value ?? 5f);
        var fadeSeconds = Mathf.Max(0.05f, _historyFadeSeconds?.Value ?? 0.35f);
        _historyVisibleUntil = Time.unscaledTime + visibleSeconds;
        _historyFadeEndTime = _historyVisibleUntil + fadeSeconds;
    }

    private void PushFeedbackLine(string line, bool isError)
    {
        var text = (line ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return;
        }

        if (isError)
        {
            PushHistory($"[Error] {text}");
            return;
        }

        PushHistory(text);
    }

    private void DrainPendingHistory()
    {
        while (true)
        {
            PendingHistoryLine next;
            lock (PendingHistoryLock)
            {
                if (PendingHistory.Count == 0)
                {
                    break;
                }

                next = PendingHistory.Dequeue();
            }

            PushFeedbackLine(next.Text, next.IsError);
        }
    }

    private void DrawHistoryOverlay(float boxX, float boxY, float boxWidth)
    {
        if (_history.Count == 0 || _historyTextStyle == null)
        {
            return;
        }

        var now = Time.unscaledTime;
        var shouldShow = _isOpen || now < _historyFadeEndTime;
        if (!shouldShow)
        {
            return;
        }

        var alpha = 1f;
        if (!_isOpen && now > _historyVisibleUntil)
        {
            var fadeSeconds = Mathf.Max(0.05f, _historyFadeSeconds?.Value ?? 0.35f);
            var t = Mathf.Clamp01((now - _historyVisibleUntil) / fadeSeconds);
            alpha = 1f - t;
        }

        if (alpha <= 0.001f)
        {
            return;
        }

        var maxLines = Mathf.Clamp(_historyMaxLines?.Value ?? 10, 1, 40);
        var lineHeight = 18f;
        var shownLines = Mathf.Min(maxLines, _history.Count);
        var historyHeight = shownLines * lineHeight + 12f;
        var historyRect = new Rect(boxX, boxY - historyHeight - 8f, boxWidth, historyHeight);

        var bgAlpha = Mathf.Clamp01(_historyBackgroundAlpha?.Value ?? 0.35f) * alpha;
        var prev = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, bgAlpha);
        GUI.DrawTexture(historyRect, Texture2D.whiteTexture, ScaleMode.StretchToFill);
        GUI.color = prev;

        var start = _history.Count - shownLines;
        for (var i = 0; i < shownLines; i++)
        {
            var text = _history[start + i];
            var y = historyRect.y + 8f + i * lineHeight;
            var textRect = new Rect(historyRect.x + 10f, y, historyRect.width - 20f, lineHeight);
            var prevTextColor = _historyTextStyle.normal.textColor;
            _historyTextStyle.normal.textColor = new Color(prevTextColor.r, prevTextColor.g, prevTextColor.b, 0.96f * alpha);
            GUI.Label(textRect, text, _historyTextStyle);
            _historyTextStyle.normal.textColor = prevTextColor;
        }
    }

    private void RefreshInlineSuggestion()
    {
        _ghostSuffix = string.Empty;
        _acceptSuggestionLine = null;

        if (!_isOpen || string.IsNullOrWhiteSpace(_line))
        {
            return;
        }

        if (!TryBuildAutocompleteContext(_line, advanceTokenWhenComplete: false, out var context))
        {
            return;
        }

        if (context.Options.Count != 1)
        {
            return;
        }

        var option = context.Options[0];
        if (context.SeedTrailingSpace)
        {
            _ghostSuffix = option;
            _acceptSuggestionLine = ApplyOptionToContext(context, option);
            return;
        }

        var typedToken = context.SeedTokens[context.TokenIndex];
        if (!option.StartsWith(typedToken, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var remainder = option.Substring(typedToken.Length);
        if (remainder.Length == 0)
        {
            return;
        }

        _ghostSuffix = remainder;
        _acceptSuggestionLine = ApplyOptionToContext(context, option);
    }

    private bool TryAcceptInlineSuggestion(bool appendTrailingSpace)
    {
        if (string.IsNullOrEmpty(_acceptSuggestionLine))
        {
            return false;
        }

        _line = _acceptSuggestionLine!;
        if (appendTrailingSpace && !_line.EndsWith(" ", StringComparison.Ordinal))
        {
            _line += " ";
        }

        _lineChangedByCode = true;
        _focusInput = true;
        _tabCycle = null;
        return true;
    }

    private bool HandleTabAutocomplete()
    {
        if (_tabCycle != null &&
            (string.Equals(_line, _tabCycle.LastAppliedLine, StringComparison.Ordinal) ||
             string.Equals(_line, _tabCycle.Context.SeedLine, StringComparison.Ordinal)))
        {
            _tabCycle.OptionIndex = (_tabCycle.OptionIndex + 1) % _tabCycle.Options.Count;
            ApplyTabCycleOption(_tabCycle);
            return true;
        }

        if (!TryBuildAutocompleteContext(_line, advanceTokenWhenComplete: true, out var context))
        {
            return false;
        }

        var cycle = new TabCycleState
        {
            Context = context,
            Options = context.Options,
            OptionIndex = 0
        };

        _tabCycle = cycle;
        ApplyTabCycleOption(cycle);
        return true;
    }

    private void ApplyTabCycleOption(TabCycleState cycle)
    {
        var option = cycle.Options[cycle.OptionIndex];
        _line = ApplyOptionToContext(cycle.Context, option);
        cycle.LastAppliedLine = _line;
        _lineChangedByCode = true;
        _focusInput = true;
    }

    private static string ApplyOptionToContext(AutocompleteContext context, string option)
    {
        if (context.SeedTrailingSpace)
        {
            var baseLine = string.Join(" ", context.SeedTokens);
            return baseLine.Length == 0 ? option : $"{baseLine} {option}";
        }

        var tokens = new List<string>(context.SeedTokens);
        if (context.TokenIndex < 0 || context.TokenIndex >= tokens.Count)
        {
            return string.Join(" ", tokens);
        }

        tokens[context.TokenIndex] = option;
        return string.Join(" ", tokens);
    }

    private static bool TryBuildAutocompleteContext(string line, bool advanceTokenWhenComplete, out AutocompleteContext context)
    {
        context = null!;

        var normalized = NormalizeInputLine(line);
        var tokens = Tokenize(normalized, out var trailingSpace);
        if (tokens.Count == 0)
        {
            return false;
        }

        var tokenIndex = trailingSpace ? tokens.Count : tokens.Count - 1;
        var options = GetTokenOptions(tokens, trailingSpace, tokenIndex);
        if (options.Count == 0)
        {
            return false;
        }

        if (advanceTokenWhenComplete && !trailingSpace && options.Count == 1)
        {
            var typedToken = tokens[tokenIndex];
            if (string.Equals(options[0], typedToken, StringComparison.OrdinalIgnoreCase))
            {
                var nextOptions = GetTokenOptions(tokens, trailingSpace: true, tokenIndex: tokens.Count);
                if (nextOptions.Count > 0)
                {
                    trailingSpace = true;
                    tokenIndex = tokens.Count;
                    options = nextOptions;
                }
            }
        }

        context = new AutocompleteContext
        {
            SeedLine = normalized,
            SeedTokens = tokens,
            SeedTrailingSpace = trailingSpace,
            TokenIndex = tokenIndex,
            Options = options
        };

        return true;
    }

    private static string NormalizeInputLine(string line)
    {
        return (line ?? string.Empty).TrimStart();
    }

    private static List<string> Tokenize(string value, out bool trailingSpace)
    {
        trailingSpace = value.EndsWith(" ", StringComparison.Ordinal);
        var split = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        return new List<string>(split);
    }

    private static List<string> GetTokenOptions(List<string> inputTokens, bool trailingSpace, int tokenIndex)
    {
        var options = new List<string>();
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var catalog = BuildCommandCatalog();

        for (var i = 0; i < catalog.Count; i++)
        {
            var cmd = catalog[i];
            var cmdTokens = Tokenize(cmd, out _);
            if (cmdTokens.Count <= tokenIndex)
            {
                continue;
            }

            if (!PrefixMatches(inputTokens, cmdTokens, tokenIndex))
            {
                continue;
            }

            if (!trailingSpace)
            {
                var typed = inputTokens[tokenIndex];
                if (!cmdTokens[tokenIndex].StartsWith(typed, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
            }

            var option = cmdTokens[tokenIndex];
            if (unique.Add(option))
            {
                options.Add(option);
            }
        }

        return options;
    }

    private static bool PrefixMatches(List<string> inputTokens, List<string> cmdTokens, int tokenIndex)
    {
        var exactCount = tokenIndex;
        if (exactCount <= 0)
        {
            return true;
        }

        if (inputTokens.Count < exactCount || cmdTokens.Count < exactCount)
        {
            return false;
        }

        for (var i = 0; i < exactCount; i++)
        {
            if (!string.Equals(inputTokens[i], cmdTokens[i], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static List<string> BuildCommandCatalog()
    {
        var commands = new List<string>();
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var reg in Registrations.Values)
        {
            for (var i = 0; i < reg.Commands.Count; i++)
            {
                var cmd = reg.Commands[i];
                if (unique.Add(cmd))
                {
                    commands.Add(cmd);
                }
            }
        }

        return commands;
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

        registration.Commands.Add("/" + normalized);
        Registrations[normalized] = registration;

        _log?.LogInfo($"Registered '/{normalized}' for {owner}.");
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

        registration.Commands.Clear();
        registration.Commands.Add("/" + normalized);

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

        _log?.LogInfo($"Updated command catalog for '/{normalized}' with {registration.Commands.Count} entries.");
        return true;
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
            _log?.LogInfo($"Unregistered '/{normalized}'.");
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

    private void SetGameplayInputSuppressed(bool suppress)
    {
        SetGameInputSuppressed(suppress);

        if (suppress)
        {
            BeginUiManagerSuppression();
            TogglePlayerControls(enable: false);
            return;
        }

        TogglePlayerControls(enable: true);
        EndUiManagerSuppression();
    }

    private void SetGameInputSuppressed(bool suppress)
    {
        if (_gameInputSuppressed == suppress)
        {
            return;
        }

        if (suppress)
        {
            if (!TryGetPlayerActionMap(out var playerActionMap))
            {
                LogInputSuppressionWarningOnce("Could not find player input map to suppress game hotkeys.");
                return;
            }

            _playerInputWasEnabledBeforeSuppression = TryGetActionMapEnabled(playerActionMap, out var enabled) && enabled;
            if (_playerInputWasEnabledBeforeSuppression)
            {
                TryInvokeMapToggle(playerActionMap, enable: false);
            }

            _gameInputSuppressed = true;
            return;
        }

        if (_playerInputWasEnabledBeforeSuppression && TryGetPlayerActionMap(out var currentActionMap))
        {
            TryInvokeMapToggle(currentActionMap, enable: true);
        }

        _playerInputWasEnabledBeforeSuppression = false;
        _gameInputSuppressed = false;
    }

    private void TogglePlayerControls(bool enable)
    {
        GameObject? playerRoot = null;
        try
        {
            playerRoot = GameObject.FindWithTag("Player");
        }
        catch
        {
            // Ignore missing Player tag and continue with camera-root fallback.
        }

        if (playerRoot == null && Camera.main != null)
        {
            playerRoot = Camera.main.transform.root.gameObject;
        }

        if (playerRoot == null)
        {
            return;
        }

        var monoBehaviours = playerRoot.GetComponents<MonoBehaviour>();
        for (var i = 0; i < monoBehaviours.Length; i++)
        {
            var mono = monoBehaviours[i];
            if (mono == null)
            {
                continue;
            }

            var typeName = mono.GetType().Name;
            if (!string.Equals(typeName, "PlayerController", StringComparison.Ordinal) &&
                !string.Equals(typeName, "PlayerInventory", StringComparison.Ordinal))
            {
                continue;
            }

            if (!enable)
            {
                if (!mono.enabled)
                {
                    continue;
                }

                mono.enabled = false;
                if (!_disabledScripts.Contains(mono))
                {
                    _disabledScripts.Add(mono);
                }

                continue;
            }

            if (_disabledScripts.Contains(mono))
            {
                mono.enabled = true;
            }
        }

        if (enable)
        {
            _disabledScripts.Clear();
        }
    }

    private void BeginUiManagerSuppression()
    {
        if (_suppressedUiManagerBehaviour != null)
        {
            return;
        }

        var instance = TryGetSingletonInstance("UIManager");
        if (instance is not MonoBehaviour uiManager)
        {
            return;
        }

        _suppressedUiManagerBehaviour = uiManager;
        _suppressedUiManagerWasEnabled = uiManager.enabled;
        uiManager.enabled = false;
    }

    private void EndUiManagerSuppression()
    {
        if (_suppressedUiManagerBehaviour == null)
        {
            return;
        }

        _suppressedUiManagerBehaviour.enabled = _suppressedUiManagerWasEnabled;
        _suppressedUiManagerBehaviour = null;
        _suppressedUiManagerWasEnabled = false;
    }

    private static object? TryGetSingletonInstance(string typeName)
    {
        var type = FindTypeByName(typeName);
        if (type == null)
        {
            return null;
        }

        var instanceProperty = type.GetProperty("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy);
        return instanceProperty?.GetValue(null);
    }

    private static bool TryGetPlayerActionMap(out object playerActionMap)
    {
        playerActionMap = null!;

        var keybindManagerType = FindTypeByName("KeybindManager");
        if (keybindManagerType == null)
        {
            return false;
        }

        var instanceProperty = keybindManagerType.GetProperty("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy);
        var keybindManager = instanceProperty?.GetValue(null);
        if (keybindManager == null)
        {
            return false;
        }

        var inputProperty = keybindManagerType.GetProperty("Input", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        var inputActions = inputProperty?.GetValue(keybindManager);
        if (inputActions == null)
        {
            return false;
        }

        var playerProperty = inputActions.GetType().GetProperty("Player", BindingFlags.Public | BindingFlags.Instance);
        playerActionMap = playerProperty?.GetValue(inputActions)!;
        return playerActionMap != null;
    }

    private static bool TryGetActionMapEnabled(object actionMap, out bool enabled)
    {
        enabled = false;
        var enabledProperty = actionMap.GetType().GetProperty("enabled", BindingFlags.Public | BindingFlags.Instance);
        if (enabledProperty == null)
        {
            return false;
        }

        var value = enabledProperty.GetValue(actionMap);
        if (value is not bool boolValue)
        {
            return false;
        }

        enabled = boolValue;
        return true;
    }

    private static bool TryInvokeMapToggle(object actionMap, bool enable)
    {
        var methodName = enable ? "Enable" : "Disable";
        var method = actionMap.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
        if (method == null)
        {
            return false;
        }

        method.Invoke(actionMap, null);
        return true;
    }

    private static Type? FindTypeByName(string name)
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (var i = 0; i < assemblies.Length; i++)
        {
            var type = assemblies[i].GetType(name, throwOnError: false);
            if (type != null)
            {
                return type;
            }
        }

        return null;
    }

    private void LogInputSuppressionWarningOnce(string message)
    {
        if (_inputSuppressionWarningLogged)
        {
            return;
        }

        _inputSuppressionWarningLogged = true;
        _log?.LogWarning(message);
    }
}
