using UnityEngine;

namespace ChatCommands;

public sealed partial class ChatCommands
{
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
            _ghostTextStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = _chatInputStyle.fontSize,
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
                clipping = TextClipping.Clip,
                wordWrap = false,
                normal = { textColor = new Color(0.78f, 0.78f, 0.78f, 0.72f) },
                focused = { textColor = new Color(0.78f, 0.78f, 0.78f, 0.72f) },
                hover = { textColor = new Color(0.78f, 0.78f, 0.78f, 0.72f) },
                active = { textColor = new Color(0.78f, 0.78f, 0.78f, 0.72f) }
            };

            _ghostTextStyle.normal.background = null;
            _ghostTextStyle.focused.background = null;
            _ghostTextStyle.hover.background = null;
            _ghostTextStyle.active.background = null;
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
}
