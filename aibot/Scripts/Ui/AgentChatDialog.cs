using System.Collections.Concurrent;
using System.Text;
using Godot;
using MegaCrit.Sts2.Core.Helpers;
using aibot.Scripts.Agent;
using aibot.Scripts.Core;

namespace aibot.Scripts.Ui;

public sealed partial class AgentChatDialog : CanvasLayer
{
    private static AgentChatDialog? _instance;

    private readonly ConcurrentQueue<(string Role, string Content)> _pendingMessages = new();
    private readonly List<(string Role, string Content)> _messages = new();

    private readonly PanelContainer _panel;
    private readonly Label _title;
    private readonly RichTextLabel _history;
    private readonly LineEdit _input;
    private readonly Button _sendButton;

    private AiBotRuntime? _runtime;
    private AgentMode _mode;

    public AgentChatDialog()
    {
        Layer = 210;
        ProcessMode = ProcessModeEnum.Always;

        _panel = new PanelContainer();
        _panel.AnchorLeft = 0f;
        _panel.AnchorRight = 0f;
        _panel.AnchorTop = 1f;
        _panel.AnchorBottom = 1f;
        _panel.OffsetLeft = 12f;
        _panel.OffsetRight = 520f;
        _panel.OffsetTop = -420f;
        _panel.OffsetBottom = -12f;
        AddChild(_panel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 10);
        margin.AddThemeConstantOverride("margin_right", 10);
        margin.AddThemeConstantOverride("margin_top", 10);
        margin.AddThemeConstantOverride("margin_bottom", 10);
        _panel.AddChild(margin);

        var layout = new VBoxContainer();
        layout.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        margin.AddChild(layout);

        _title = new Label
        {
            Text = "Agent Chat",
            HorizontalAlignment = HorizontalAlignment.Left
        };
        layout.AddChild(_title);

        _history = new RichTextLabel
        {
            BbcodeEnabled = false,
            ScrollActive = true,
            FitContent = false,
            SelectionEnabled = true,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Text = "输入消息后，Agent 会在这里回复。"
        };
        layout.AddChild(_history);

        var inputRow = new HBoxContainer();
        layout.AddChild(inputRow);

        _input = new LineEdit
        {
            PlaceholderText = "输入指令或问题..."
        };
        _input.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        inputRow.AddChild(_input);

        _sendButton = new Button
        {
            Text = "发送"
        };
        inputRow.AddChild(_sendButton);

        _sendButton.Pressed += async () => await SubmitAsync();
        _input.TextSubmitted += async _ => await SubmitAsync();

        Visible = false;
    }

    public static void EnsureCreated(AiBotRuntime runtime)
    {
        if (_instance is not null && GodotObject.IsInstanceValid(_instance) && _instance.GetParent() is not null)
        {
            _instance._runtime = runtime;
            return;
        }

        var root = ((SceneTree)Engine.GetMainLoop()).Root;
        _instance = new AgentChatDialog
        {
            Name = "AgentChatDialog",
            _runtime = runtime
        };
        root.AddChild(_instance);
    }

    public static void ShowForMode(AgentMode mode, string? systemMessage = null)
    {
        if (_instance is null)
        {
            return;
        }

        if (_instance._runtime?.Config.Ui.ShowChatDialog == false)
        {
            _instance.Visible = false;
            return;
        }

        _instance._mode = mode;
        _instance._title.Text = mode switch
        {
            AgentMode.SemiAuto => "Agent Chat - Semi Auto",
            AgentMode.QnA => "Agent Chat - QnA",
            _ => "Agent Chat"
        };
        _instance.Visible = true;
        if (!string.IsNullOrWhiteSpace(systemMessage))
        {
            _instance.EnqueueMessage("系统", systemMessage);
        }
    }

    public static void HideDialog()
    {
        if (_instance is null)
        {
            return;
        }

        _instance.Visible = false;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        base._UnhandledInput(@event);
        if (_runtime?.Config.Ui.ShowChatDialog != true)
        {
            return;
        }

        if (@event is not InputEventKey keyEvent || !keyEvent.Pressed || keyEvent.Echo)
        {
            return;
        }

        if (HotkeyMatches(_runtime?.Config.Ui.ChatHotkey, keyEvent.Keycode))
        {
            Visible = !Visible;
            if (Visible)
            {
                _input.GrabFocus();
            }
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Process(double delta)
    {
        var changed = false;
        while (_pendingMessages.TryDequeue(out var item))
        {
            _messages.Add(item);
            changed = true;
        }

        if (changed)
        {
            var maxHistory = Math.Max(1, _runtime?.Config.Agent.MaxConversationHistory ?? 50);
            while (_messages.Count > maxHistory)
            {
                _messages.RemoveAt(0);
            }

            RefreshText();
        }
    }

    private async Task SubmitAsync()
    {
        var text = _input.Text.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        _input.Text = string.Empty;
        EnqueueMessage("你", text);
        EnqueueMessage("系统", "处理中...");

        try
        {
            var response = await AgentCore.Instance.SubmitUserInputAsync(text);
            ReplaceLastSystemMessage(response);
        }
        catch (Exception ex)
        {
            ReplaceLastSystemMessage($"处理失败：{ex.Message}");
        }
    }

    private void EnqueueMessage(string role, string content)
    {
        _pendingMessages.Enqueue((role, content));
    }

    private void ReplaceLastSystemMessage(string content)
    {
        if (_messages.Count > 0 && _messages[^1].Role == "系统" && _messages[^1].Content == "处理中...")
        {
            _messages[^1] = ("Agent", content);
            RefreshText();
            return;
        }

        EnqueueMessage("Agent", content);
    }

    private void RefreshText()
    {
        if (_messages.Count == 0)
        {
            _history.Text = "输入消息后，Agent 会在这里回复。";
            return;
        }

        var builder = new StringBuilder();
        foreach (var (role, content) in _messages)
        {
            builder.Append('[').Append(role).Append("] ").AppendLine(content);
            builder.AppendLine();
        }

        _history.Text = builder.ToString().TrimEnd();
        _history.ScrollToLine(Math.Max(0, _history.GetLineCount() - 1));
    }

    private static bool HotkeyMatches(string? configuredHotkey, Key keycode)
    {
        if (string.IsNullOrWhiteSpace(configuredHotkey))
        {
            return false;
        }

        var normalized = configuredHotkey.Trim().Replace("-", string.Empty).Replace(" ", string.Empty);
        return Enum.TryParse<Key>(normalized, true, out var parsed) && parsed == keycode;
    }
}
