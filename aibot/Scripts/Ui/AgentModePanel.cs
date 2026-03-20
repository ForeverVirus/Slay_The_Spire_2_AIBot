using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using aibot.Scripts.Agent;
using aibot.Scripts.Core;

namespace aibot.Scripts.Ui;

public sealed partial class AgentModePanel : CanvasLayer
{
    private static AgentModePanel? _instance;

    private readonly PanelContainer _panel;
    private readonly Label _title;
    private readonly Label _currentModeLabel;
    private readonly Label _statusLabel;
    private readonly Label _hintLabel;
    private readonly Dictionary<AgentMode, Button> _modeButtons = new();
    private readonly PanelContainer _confirmPanel;
    private readonly Label _confirmLabel;
    private readonly Button _confirmYesButton;
    private readonly Button _confirmNoButton;

    private AiBotRuntime? _runtime;
    private AgentModeChangeRequest? _pendingRequest;

    public AgentModePanel()
    {
        Layer = 205;
        ProcessMode = ProcessModeEnum.Always;

        _panel = new PanelContainer();
        _panel.AnchorLeft = 0f;
        _panel.AnchorRight = 0f;
        _panel.AnchorTop = 0f;
        _panel.AnchorBottom = 0f;
        _panel.OffsetLeft = 12f;
        _panel.OffsetRight = 280f;
        _panel.OffsetTop = 12f;
        _panel.OffsetBottom = 250f;
        AddChild(_panel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 10);
        margin.AddThemeConstantOverride("margin_right", 10);
        margin.AddThemeConstantOverride("margin_top", 10);
        margin.AddThemeConstantOverride("margin_bottom", 10);
        _panel.AddChild(margin);

        var layout = new VBoxContainer();
        margin.AddChild(layout);

        _title = new Label
        {
            Text = "Agent Modes",
            HorizontalAlignment = HorizontalAlignment.Left
        };
        layout.AddChild(_title);

        _currentModeLabel = new Label
        {
            Text = "当前模式：未知"
        };
        layout.AddChild(_currentModeLabel);

        _statusLabel = new Label
        {
            Text = "点击按钮切换模式。",
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        layout.AddChild(_statusLabel);

        var buttonGrid = new GridContainer
        {
            Columns = 2
        };
        layout.AddChild(buttonGrid);

        foreach (var mode in new[] { AgentMode.FullAuto, AgentMode.SemiAuto, AgentMode.Assist, AgentMode.QnA })
        {
            var button = new Button
            {
                Text = GetModeDisplayName(mode),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            button.Pressed += () => TaskHelper.RunSafely(RequestModeSwitchAsync(mode));
            buttonGrid.AddChild(button);
            _modeButtons[mode] = button;
        }

        _hintLabel = new Label
        {
            Text = "热键：F5/F6/F7/F8 切换模式",
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        layout.AddChild(_hintLabel);

        _confirmPanel = new PanelContainer
        {
            Visible = false
        };
        layout.AddChild(_confirmPanel);

        var confirmMargin = new MarginContainer();
        confirmMargin.AddThemeConstantOverride("margin_left", 8);
        confirmMargin.AddThemeConstantOverride("margin_right", 8);
        confirmMargin.AddThemeConstantOverride("margin_top", 8);
        confirmMargin.AddThemeConstantOverride("margin_bottom", 8);
        _confirmPanel.AddChild(confirmMargin);

        var confirmLayout = new VBoxContainer();
        confirmMargin.AddChild(confirmLayout);

        _confirmLabel = new Label
        {
            Text = "确认切换模式？",
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        confirmLayout.AddChild(_confirmLabel);

        var confirmButtons = new HBoxContainer();
        confirmLayout.AddChild(confirmButtons);

        _confirmYesButton = new Button
        {
            Text = "确认切换"
        };
        _confirmYesButton.Pressed += () => TaskHelper.RunSafely(ConfirmPendingRequestAsync());
        confirmButtons.AddChild(_confirmYesButton);

        _confirmNoButton = new Button
        {
            Text = "取消"
        };
        _confirmNoButton.Pressed += CancelPendingRequest;
        confirmButtons.AddChild(_confirmNoButton);

        Visible = false;
    }

    public static void EnsureCreated(AiBotRuntime runtime)
    {
        if (_instance is not null && GodotObject.IsInstanceValid(_instance) && _instance.GetParent() is not null)
        {
            _instance._runtime = runtime;
            _instance.ApplyRuntimeConfiguration();
            return;
        }

        var root = ((SceneTree)Engine.GetMainLoop()).Root;
        _instance = new AgentModePanel
        {
            Name = "AgentModePanel",
            _runtime = runtime
        };
        root.AddChild(_instance);
    }

    public override void _Ready()
    {
        base._Ready();
        AgentCore.Instance.ModeChangeRequested += OnModeChangeRequested;
        AgentCore.Instance.ModeChanged += OnModeChanged;
        ApplyRuntimeConfiguration();
        UpdateCurrentMode(AgentCore.Instance.CurrentMode);
    }

    public override void _ExitTree()
    {
        AgentCore.Instance.ModeChangeRequested -= OnModeChangeRequested;
        AgentCore.Instance.ModeChanged -= OnModeChanged;
        base._ExitTree();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        base._UnhandledInput(@event);
        if (_runtime is null)
        {
            return;
        }

        if (@event is not InputEventKey keyEvent || !keyEvent.Pressed || keyEvent.Echo)
        {
            return;
        }

        if (TryHandleModeHotkey(keyEvent.Keycode))
        {
            GetViewport().SetInputAsHandled();
            return;
        }

        if (_runtime.Config.Ui.ShowModePanel && HotkeyMatches(_runtime.Config.Ui.ModePanelHotkey, keyEvent.Keycode))
        {
            Visible = !Visible;
            GetViewport().SetInputAsHandled();
        }
    }

    private async Task RequestModeSwitchAsync(AgentMode mode)
    {
        if (_runtime is null)
        {
            return;
        }

        if (AgentCore.Instance.CurrentMode == mode)
        {
            SetStatus($"当前已经是 {GetModeDisplayName(mode)} 模式。", false);
            return;
        }

        SetStatus($"请求切换到 {GetModeDisplayName(mode)}...", false);
        var changed = await AgentCore.Instance.SwitchModeAsync(mode, $"mode-panel:{mode}");
        if (changed)
        {
            SetStatus($"已切换到 {GetModeDisplayName(mode)}。", false);
        }
    }

    private async Task ConfirmPendingRequestAsync()
    {
        if (_pendingRequest is null)
        {
            return;
        }

        var request = _pendingRequest;
        _pendingRequest = null;
        _confirmPanel.Visible = false;
        SetStatus($"确认切换到 {GetModeDisplayName(request.RequestedMode)}...", false);
        var changed = await AgentCore.Instance.SwitchModeAsync(request.RequestedMode, request.Reason + ":confirmed", true);
        if (!changed)
        {
            SetStatus("模式切换未成功完成。", true);
        }
    }

    private void CancelPendingRequest()
    {
        _pendingRequest = null;
        _confirmPanel.Visible = false;
        SetStatus("已取消模式切换。", false);
    }

    private void OnModeChangeRequested(AgentModeChangeRequest request)
    {
        _pendingRequest = request;
        _confirmLabel.Text = $"即将从 {GetModeDisplayName(request.CurrentMode)} 切换到 {GetModeDisplayName(request.RequestedMode)}。\n原因：{request.Reason}\n是否继续？";
        _confirmPanel.Visible = request.RequiresConfirmation;
        SetStatus("等待确认模式切换。", false);
        Visible = true;
        Log.Info($"[AiBot.Agent] Mode panel received switch request: {request.CurrentMode} -> {request.RequestedMode}");
    }

    private void OnModeChanged(AgentMode mode)
    {
        UpdateCurrentMode(mode);
        _pendingRequest = null;
        _confirmPanel.Visible = false;
        SetStatus($"当前模式：{GetModeDisplayName(mode)}", false);
    }

    private void UpdateCurrentMode(AgentMode mode)
    {
        _currentModeLabel.Text = $"当前模式：{GetModeDisplayName(mode)}";
        foreach (var pair in _modeButtons)
        {
            pair.Value.Disabled = pair.Key == mode;
        }
    }

    private void ApplyRuntimeConfiguration()
    {
        if (_runtime is null)
        {
            return;
        }

        Visible = _runtime.Config.Ui.ShowModePanel && _runtime.Config.Ui.ModePanelStartVisible;
        _hintLabel.Text = BuildHotkeyHint();
    }

    private bool TryHandleModeHotkey(Key keycode)
    {
        if (_runtime is null)
        {
            return false;
        }

        foreach (var mode in new[] { AgentMode.FullAuto, AgentMode.SemiAuto, AgentMode.Assist, AgentMode.QnA })
        {
            if (!HotkeyMatches(_runtime.Config.Ui.ModeHotkeys.GetHotkey(mode), keycode))
            {
                continue;
            }

            Visible = true;
            TaskHelper.RunSafely(RequestModeSwitchAsync(mode));
            return true;
        }

        return false;
    }

    private string BuildHotkeyHint()
    {
        if (_runtime is null)
        {
            return "热键：F5/F6/F7/F8 切换模式";
        }

        var hotkeys = _runtime.Config.Ui.ModeHotkeys;
        var toggleSuffix = string.IsNullOrWhiteSpace(_runtime.Config.Ui.ModePanelHotkey)
            ? string.Empty
            : $"；{_runtime.Config.Ui.ModePanelHotkey} 显示/隐藏面板";

        return $"热键：{hotkeys.FullAuto} Full Auto，{hotkeys.SemiAuto} Semi Auto，{hotkeys.Assist} Assist，{hotkeys.QnA} QnA{toggleSuffix}";
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

    private void SetStatus(string text, bool isError)
    {
        _statusLabel.Text = text;
        _statusLabel.Modulate = isError ? new Color(1f, 0.7f, 0.7f) : Colors.White;
    }

    private static string GetModeDisplayName(AgentMode mode)
    {
        return mode switch
        {
            AgentMode.FullAuto => "Full Auto",
            AgentMode.SemiAuto => "Semi Auto",
            AgentMode.Assist => "Assist",
            AgentMode.QnA => "QnA",
            _ => mode.ToString()
        };
    }
}