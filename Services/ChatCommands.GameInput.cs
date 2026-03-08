using System;
using System.Reflection;
using UnityEngine;

namespace ChatCommands;

public sealed partial class ChatCommands
{
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
        _log?.LogWarning($"{ModInfo.LOG_PREFIX} {message}");
    }
}
