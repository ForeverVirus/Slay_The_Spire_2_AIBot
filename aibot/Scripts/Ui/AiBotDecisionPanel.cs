using System.Collections.Concurrent;
using System.Text;
using Godot;
using aibot.Scripts.Agent;

namespace aibot.Scripts.Ui;

public sealed partial class AiBotDecisionPanel : CanvasLayer
{
    private readonly ConcurrentQueue<AiBotDecisionFeedEntry> _pendingEntries = new();
    private readonly List<AiBotDecisionFeedEntry> _entries = new();

    private readonly PanelContainer _panel;
    private readonly Label _title;
    private readonly Label _currentMode;
    private readonly VBoxContainer _body;
    private readonly RichTextLabel _content;
    private readonly Button _toggleButton;
    private int _maxEntries;
    private bool _isCollapsed;

    public AiBotDecisionPanel(int maxEntries = 16)
    {
        Layer = 200;
        ProcessMode = ProcessModeEnum.Always;
        _maxEntries = Math.Max(4, maxEntries);

        _panel = new PanelContainer();
        _panel.AnchorLeft = 1f;
        _panel.AnchorRight = 1f;
        _panel.AnchorTop = 0f;
        _panel.AnchorBottom = 0f;
        _panel.OffsetLeft = -460f;
        _panel.OffsetRight = -12f;
        _panel.OffsetTop = 12f;
        _panel.OffsetBottom = 620f;
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

        var header = new HBoxContainer();
        layout.AddChild(header);

        _title = new Label
        {
            Text = "AiBot Decisions",
            HorizontalAlignment = HorizontalAlignment.Left,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        header.AddChild(_title);

        _currentMode = new Label
        {
            Text = "Mode: Full Auto",
            HorizontalAlignment = HorizontalAlignment.Right
        };
        header.AddChild(_currentMode);

        _toggleButton = new Button
        {
            Text = "最小化"
        };
        _toggleButton.Pressed += ToggleCollapsed;
        header.AddChild(_toggleButton);

        _body = new VBoxContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        layout.AddChild(_body);

        _content = new RichTextLabel
        {
            BbcodeEnabled = false,
            ScrollActive = true,
            FitContent = false,
            SelectionEnabled = true,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Text = "Waiting for decisions..."
        };
        _body.AddChild(_content);
    }

    public override void _Ready()
    {
        base._Ready();
        AiBotDecisionFeed.EntryAdded += OnEntryAdded;
        AgentCore.Instance.ModeChanged += OnModeChanged;
        foreach (var entry in AiBotDecisionFeed.GetEntries())
        {
            _entries.Add(entry);
        }

        UpdateModeLabel(AgentCore.Instance.CurrentMode);
        ApplyCollapsedState();
        RefreshText();
    }

    public override void _ExitTree()
    {
        AiBotDecisionFeed.EntryAdded -= OnEntryAdded;
        AgentCore.Instance.ModeChanged -= OnModeChanged;
        base._ExitTree();
    }

    public override void _Process(double delta)
    {
        var changed = false;
        while (_pendingEntries.TryDequeue(out var entry))
        {
            _entries.Add(entry);
            changed = true;
        }

        if (changed)
        {
            while (_entries.Count > _maxEntries)
            {
                _entries.RemoveAt(0);
            }

            RefreshText();
        }
    }

    public void SetMaxEntries(int maxEntries)
    {
        _maxEntries = Math.Max(4, maxEntries);
        while (_entries.Count > _maxEntries)
        {
            _entries.RemoveAt(0);
        }

        RefreshText();
    }

    private void OnEntryAdded(AiBotDecisionFeedEntry entry)
    {
        _pendingEntries.Enqueue(entry);
    }

    private void OnModeChanged(AgentMode mode)
    {
        UpdateModeLabel(mode);
    }

    private void RefreshText()
    {
        if (_entries.Count == 0)
        {
            _content.Text = "Waiting for decisions...";
            return;
        }

        var builder = new StringBuilder();
        foreach (var entry in _entries.OrderByDescending(e => e.Timestamp))
        {
            builder.Append('[').Append(entry.Timestamp.ToString("HH:mm:ss")).Append("] ");
            builder.Append('[').Append(entry.Source).Append("] ");
            builder.Append('[').Append(entry.Category).Append("] ").AppendLine(entry.Summary);
            builder.AppendLine(entry.Details);
            builder.AppendLine();
        }

        _content.Text = builder.ToString().TrimEnd();
        _content.ScrollToLine(0);
    }

    private void ToggleCollapsed()
    {
        _isCollapsed = !_isCollapsed;
        ApplyCollapsedState();
    }

    private void ApplyCollapsedState()
    {
        _body.Visible = !_isCollapsed;
        _toggleButton.Text = _isCollapsed ? "展开" : "最小化";
        _panel.OffsetBottom = _isCollapsed ? 64f : 620f;
    }

    private void UpdateModeLabel(AgentMode mode)
    {
        _currentMode.Text = $"Mode: {GetModeDisplayName(mode)}";
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