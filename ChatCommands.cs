using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

namespace ChatCommands;

[BepInPlugin(ModInfo.PluginGuid, ModInfo.PluginName, ModInfo.PluginVersion)]
public sealed partial class ChatCommands : BaseUnityPlugin
{
    private static ManualLogSource? _log;
    private static ChatCommands? _instance;
    private static readonly Dictionary<string, Registration> Registrations = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object PendingHistoryLock = new();
    private static readonly Queue<PendingHistoryLine> PendingHistory = new();
    private const string InputControlName = "ChatCommands_Text";
    private const int SubmittedInputHistoryCapacity = 100;

    private bool _isOpen;
    private bool _focusInput;
    private bool _suppressSubmitThisFrame;
    private bool _suppressOpenOnSubmitKeyUntilRelease;
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
    private int _moveCaretToEndFramesRemaining;
    private readonly List<string> _submittedInputHistory = new();
    private int _submittedInputBrowseIndex = -1;
    private string _submittedInputBrowseDraft = string.Empty;

    private sealed class Registration
    {
        public string Prefix { get; set; } = string.Empty;
        public string Owner { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public Action<string> Handler { get; set; } = _ => { };
        public List<string> Commands { get; } = new();
        public Dictionary<string, string> CommandDescriptions { get; } = new(StringComparer.OrdinalIgnoreCase);
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
}
