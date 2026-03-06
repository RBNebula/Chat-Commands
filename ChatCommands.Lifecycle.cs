using System;
using UnityEngine;

namespace ChatCommands;

public sealed partial class ChatCommands
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
        Logger.LogInfo($"{ModInfo.PluginName} {ModInfo.PluginVersion} loaded.");
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

        if (_suppressOpenOnSubmitKeyUntilRelease &&
            !Input.GetKey(KeyCode.Return) &&
            !Input.GetKey(KeyCode.KeypadEnter))
        {
            _suppressOpenOnSubmitKeyUntilRelease = false;
        }

        if (!_isOpen && IsSlashOpenKeyDown())
        {
            OpenInput(prefillSlash: true);
            return;
        }

        if (!_isOpen && !_suppressOpenOnSubmitKeyUntilRelease && IsSubmitKeyDown())
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
        const float bottomOffset = 160f;
        var boxX = sidePadding;
        var boxY = Screen.height - bottomOffset;
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

        TryBrowseSubmittedInputFromGuiEvent();

        var prev = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.72f);
        GUI.Box(boxRect, GUIContent.none, _chatBoxStyle);
        GUI.color = prev;

        GUI.SetNextControlName(InputControlName);
        if (_focusInput || _moveCaretToEndFramesRemaining > 0)
        {
            FocusInputTextControl();
        }

        _line = GUI.TextField(textRect, _line ?? string.Empty, 512, _chatInputStyle);

        if (_focusInput)
        {
            FocusInputTextControl();
            _focusInput = false;
        }

        TryMoveCaretToEnd();
        HandleInputEvents();
        RefreshInlineSuggestion();
        DrawInlineSuggestion(textRect);
    }

    private void TrySubmitFromGuiEvent()
    {
        var evt = Event.current;
        if (evt.rawType != EventType.KeyDown && evt.type != EventType.KeyDown)
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

    private void TryBrowseSubmittedInputFromGuiEvent()
    {
        var evt = Event.current;
        if (evt.rawType != EventType.KeyDown && evt.type != EventType.KeyDown)
        {
            return;
        }

        if (evt.keyCode == KeyCode.UpArrow)
        {
            if (TryBrowseSubmittedInput(older: true))
            {
                evt.Use();
            }

            return;
        }

        if (evt.keyCode == KeyCode.DownArrow)
        {
            if (TryBrowseSubmittedInput(older: false))
            {
                evt.Use();
            }
        }
    }

    private static void FocusInputTextControl()
    {
        GUI.FocusControl(InputControlName);
    }
}
