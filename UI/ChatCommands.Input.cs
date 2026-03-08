using System;
using System.Collections.Generic;
using UnityEngine;

namespace ChatCommands;

public sealed partial class ChatCommands
{
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

    private void HandleInputEvents()
    {
        if (!_lineChangedByCode && !string.Equals(_line, _lastObservedLine, StringComparison.Ordinal))
        {
            _tabCycle = null;
            ClearSubmittedInputBrowseState();
        }

        _lineChangedByCode = false;
        _lastObservedLine = _line;

        var evt = Event.current;
        if (evt.type != EventType.KeyDown)
        {
            return;
        }

        if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
        {
            evt.Use();

            if (_suppressSubmitThisFrame)
            {
                return;
            }

            SubmitCurrentLine();
            return;
        }

        if (evt.keyCode == KeyCode.Tab)
        {
            ClearSubmittedInputBrowseState();
            if (HandleTabAutocomplete())
            {
                evt.Use();
            }

            return;
        }

        if (evt.keyCode == KeyCode.Space)
        {
            ClearSubmittedInputBrowseState();
            if (TryAcceptInlineSuggestion(appendTrailingSpace: true))
            {
                evt.Use();
            }

            _tabCycle = null;
            return;
        }

        if (!IsModifierOnlyKey(evt.keyCode))
        {
            ClearSubmittedInputBrowseState();
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
        const float ghostXAdjust = -14.5f;
        const float ghostYAdjust = -2.8f;
        var x = textRect.x + _chatInputStyle.padding.left + typedWidth + ghostXAdjust;
        var maxWidth = textRect.xMax - x - 6f;
        if (maxWidth <= 4f)
        {
            return;
        }

        var rect = new Rect(x, textRect.y + 3f + ghostYAdjust, maxWidth, textRect.height);
        GUI.Label(rect, _ghostSuffix, _ghostTextStyle);
    }

    private void TryMoveCaretToEnd()
    {
        if (_moveCaretToEndFramesRemaining <= 0)
        {
            return;
        }

        if (!string.Equals(GUI.GetNameOfFocusedControl(), InputControlName, StringComparison.Ordinal))
        {
            return;
        }

        if (!TryGetInputTextEditor(out var editor))
        {
            return;
        }

        var end = (_line ?? string.Empty).Length;
        editor.cursorIndex = end;
        editor.selectIndex = end;
        _moveCaretToEndFramesRemaining = Mathf.Max(0, _moveCaretToEndFramesRemaining - 1);
    }

    private void RequestMoveCaretToEnd(int frames = 3)
    {
        if (frames <= 0)
        {
            return;
        }

        _moveCaretToEndFramesRemaining = Mathf.Max(_moveCaretToEndFramesRemaining, frames);
    }

    private static bool TryGetInputTextEditor(out TextEditor editor)
    {
        editor = null!;
        var keyboardControl = GUIUtility.keyboardControl;
        if (keyboardControl == 0)
        {
            return false;
        }

        var state = GUIUtility.GetStateObject(typeof(TextEditor), keyboardControl);
        if (state is not TextEditor textEditor)
        {
            return false;
        }

        editor = textEditor;
        return true;
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
        ClearSubmittedInputBrowseState();

        if (prefillSlash)
        {
            _line = "/";
            RequestMoveCaretToEnd();
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
        ClearSubmittedInputBrowseState();
        _ghostSuffix = string.Empty;
        _acceptSuggestionLine = null;
        SetGameplayInputSuppressed(suppress: false);
    }

    private void SubmitCurrentLine()
    {
        var line = (_line ?? string.Empty).Trim();
        if (line.Length == 0)
        {
            _suppressOpenOnSubmitKeyUntilRelease = true;
            CloseInput();
            return;
        }

        PushSubmittedInput(line);

        if (line.StartsWith("/", StringComparison.Ordinal))
        {
            Dispatch(line);
        }

        PushHistory(line);
        _line = string.Empty;
        _suppressOpenOnSubmitKeyUntilRelease = true;
        CloseInput();
    }

    private void PushSubmittedInput(string line)
    {
        _submittedInputHistory.Add(line);
        while (_submittedInputHistory.Count > SubmittedInputHistoryCapacity)
        {
            _submittedInputHistory.RemoveAt(0);
        }

        ClearSubmittedInputBrowseState();
    }

    private bool TryBrowseSubmittedInput(bool older)
    {
        if (_submittedInputHistory.Count == 0)
        {
            return false;
        }

        if (older)
        {
            if (_submittedInputBrowseIndex < 0)
            {
                _submittedInputBrowseDraft = _line ?? string.Empty;
                _submittedInputBrowseIndex = _submittedInputHistory.Count - 1;
            }
            else if (_submittedInputBrowseIndex > 0)
            {
                _submittedInputBrowseIndex--;
            }

            ApplySubmittedInputBrowseLine(_submittedInputHistory[_submittedInputBrowseIndex]);
            _tabCycle = null;
            return true;
        }

        if (_submittedInputBrowseIndex < 0)
        {
            return false;
        }

        if (_submittedInputBrowseIndex < _submittedInputHistory.Count - 1)
        {
            _submittedInputBrowseIndex++;
            ApplySubmittedInputBrowseLine(_submittedInputHistory[_submittedInputBrowseIndex]);
            _tabCycle = null;
            return true;
        }

        var draft = _submittedInputBrowseDraft;
        ClearSubmittedInputBrowseState();
        ApplySubmittedInputBrowseLine(draft);
        _tabCycle = null;
        return true;
    }

    private void ApplySubmittedInputBrowseLine(string line)
    {
        _line = line ?? string.Empty;
        _lineChangedByCode = true;
        _focusInput = true;
        RequestMoveCaretToEnd(frames: 6);
    }

    private void ClearSubmittedInputBrowseState()
    {
        _submittedInputBrowseIndex = -1;
        _submittedInputBrowseDraft = string.Empty;
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
        RequestMoveCaretToEnd();
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
        RequestMoveCaretToEnd();
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
}
