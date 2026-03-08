using System;
using UnityEngine;

namespace ChatCommands;

public sealed partial class ChatCommands
{
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
}
