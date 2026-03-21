using MegaCrit.Sts2.Core.Logging;
using aibot.Scripts.Decision;
using aibot.Scripts.Core;
using aibot.Scripts.Agent.Skills;
using aibot.Scripts.Ui;

namespace aibot.Scripts.Agent.Handlers;

public sealed class SemiAutoModeHandler : IAgentModeHandler
{
    private const string ConfirmCommand = "确认执行";
    private const string CancelCommand = "取消执行";

    private readonly AiBotRuntime _runtime;
    private readonly string _activationReason;
    private readonly IntentParser _intentParser;
    private PendingSkillExecution? _pendingExecution;

    public SemiAutoModeHandler(AiBotRuntime runtime, string activationReason)
    {
        _runtime = runtime;
        _activationReason = activationReason;
        _intentParser = new IntentParser(runtime, AgentCore.Instance.Registry);
    }

    public AgentMode Mode => AgentMode.SemiAuto;

    public Task OnActivateAsync(CancellationToken cancellationToken)
    {
        _runtime.DeactivateLegacyFullAuto();
        AgentChatDialog.EnsureCreated(_runtime);
        AgentChatDialog.ShowForMode(Mode, "半自动模式已开启：你可以输入查询或游戏内操作指令。");
        AgentChatDialog.ClearPendingAction();
        Log.Info($"[AiBot.Agent] SemiAuto mode entered. Reason={_activationReason}");
        return Task.CompletedTask;
    }

    public Task OnDeactivateAsync()
    {
        _pendingExecution = null;
        AgentChatDialog.ClearPendingAction();
        AgentChatDialog.HideDialog();
        return Task.CompletedTask;
    }

    public Task OnTickAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public async Task<string> OnUserInputAsync(string input, CancellationToken cancellationToken)
    {
        if (TryParsePendingCommand(input, out var pendingCommand))
        {
            return pendingCommand == PendingCommandKind.Confirm
                ? await ConfirmPendingExecutionAsync(cancellationToken)
                : CancelPendingExecution();
        }

        var intent = await _intentParser.ParseWithFallbackAsync(input, cancellationToken);
        if (intent.Kind == ParsedIntentKind.Unknown)
        {
            var help = "未识别你的指令。你可以尝试：查看卡组、查看遗物、分析局势、打出 Strike、使用药水、结束回合、领取奖励。";
            AiBotDecisionFeed.Publish(new DecisionTrace("SemiAuto", "Parser", "无法识别输入", input));
            return help;
        }

        if (intent.Kind == ParsedIntentKind.Tool)
        {
            var tool = AgentCore.Instance.Registry.FindToolByName(intent.Name);
            if (tool is null)
            {
                return $"未找到可用工具：{intent.Name}";
            }

            var result = await tool.QueryAsync(intent.RawArgument, cancellationToken);
            AiBotDecisionFeed.Publish(new DecisionTrace("SemiAuto", "Tool", $"调用工具 {tool.Name}", result));
            return result;
        }

        var skill = AgentCore.Instance.Registry.FindSkillByName(intent.Name);
        if (skill is null)
        {
            return $"未找到可用技能：{intent.Name}";
        }

        if (!skill.CanExecute())
        {
            var cannot = $"当前无法执行技能：{skill.Description}";
            AiBotDecisionFeed.Publish(new DecisionTrace("SemiAuto", "Skill", $"技能不可执行 {skill.Name}", input));
            return cannot;
        }

        _pendingExecution = BuildPendingExecution(skill, intent, input);
        AgentChatDialog.ShowPendingAction(_pendingExecution.Prompt);
        AiBotDecisionFeed.Publish(new DecisionTrace("SemiAuto", "SkillPending", $"等待确认技能 {skill.Name}", _pendingExecution.Prompt));
        return _pendingExecution.Prompt;
    }

    private async Task<string> ConfirmPendingExecutionAsync(CancellationToken cancellationToken)
    {
        if (_pendingExecution is null)
        {
            return "当前没有待确认的操作。";
        }

        var pending = _pendingExecution;
        _pendingExecution = null;
        AgentChatDialog.ClearPendingAction();
        var execution = await pending.Skill.ExecuteAsync(pending.Intent.Parameters, cancellationToken);
        var details = string.IsNullOrWhiteSpace(execution.Details)
            ? execution.Summary
            : execution.Summary + "\n" + execution.Details;
        AiBotDecisionFeed.Publish(new DecisionTrace("SemiAuto", execution.Success ? "Skill" : "SkillFailed", $"执行技能 {pending.Skill.Name}", details));
        return details;
    }

    private string CancelPendingExecution()
    {
        if (_pendingExecution is null)
        {
            return "当前没有待确认的操作。";
        }

        var pending = _pendingExecution;
        _pendingExecution = null;
        AgentChatDialog.ClearPendingAction();
        var message = $"已取消执行：{pending.Skill.Description}";
        AiBotDecisionFeed.Publish(new DecisionTrace("SemiAuto", "SkillCanceled", $"取消技能 {pending.Skill.Name}", pending.RawInput));
        return message;
    }

    private PendingSkillExecution BuildPendingExecution(IAgentSkill skill, ParsedIntent intent, string input)
    {
        var parameterSummary = DescribeParameters(intent.Parameters);
        var summary = string.IsNullOrWhiteSpace(parameterSummary)
            ? $"准备执行：{skill.Description}"
            : $"准备执行：{skill.Description}\n目标参数：{parameterSummary}";
        var prompt = summary + $"\n请输入“{ConfirmCommand}”继续，或输入“{CancelCommand}”取消。";
        return new PendingSkillExecution(skill, intent, input, prompt);
    }

    private static bool TryParsePendingCommand(string input, out PendingCommandKind command)
    {
        var normalized = (input ?? string.Empty).Trim();
        if (string.Equals(normalized, ConfirmCommand, StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "确认", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "yes", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "y", StringComparison.OrdinalIgnoreCase))
        {
            command = PendingCommandKind.Confirm;
            return true;
        }

        if (string.Equals(normalized, CancelCommand, StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "取消", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "no", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "n", StringComparison.OrdinalIgnoreCase))
        {
            command = PendingCommandKind.Cancel;
            return true;
        }

        command = PendingCommandKind.None;
        return false;
    }

    private static string DescribeParameters(AgentSkillParameters? parameters)
    {
        if (parameters is null)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(parameters.CardName))
        {
            parts.Add($"卡牌={parameters.CardName}");
        }

        if (!string.IsNullOrWhiteSpace(parameters.TargetName))
        {
            parts.Add($"目标={parameters.TargetName}");
        }

        if (!string.IsNullOrWhiteSpace(parameters.PotionName))
        {
            parts.Add($"药水={parameters.PotionName}");
        }

        if (parameters.MapCol.HasValue || parameters.MapRow.HasValue)
        {
            parts.Add($"地图位置=({parameters.MapRow?.ToString() ?? "?"},{parameters.MapCol?.ToString() ?? "?"})");
        }

        if (!string.IsNullOrWhiteSpace(parameters.OptionId))
        {
            parts.Add($"选项={parameters.OptionId}");
        }

        if (!string.IsNullOrWhiteSpace(parameters.ItemName))
        {
            parts.Add($"物品={parameters.ItemName}");
        }

        if (parameters.BundleIndex.HasValue)
        {
            parts.Add($"Bundle={parameters.BundleIndex.Value}");
        }

        if (parameters.GridX.HasValue || parameters.GridY.HasValue)
        {
            parts.Add($"格子=({parameters.GridX?.ToString() ?? "?"},{parameters.GridY?.ToString() ?? "?"})");
        }

        if (parameters.UseBigDivination.HasValue)
        {
            parts.Add(parameters.UseBigDivination.Value ? "占卜=大范围" : "占卜=小范围");
        }

        return string.Join("，", parts);
    }

    public void Dispose()
    {
        _intentParser.Dispose();
    }

    private enum PendingCommandKind
    {
        None,
        Confirm,
        Cancel
    }

    private sealed record PendingSkillExecution(
        IAgentSkill Skill,
        ParsedIntent Intent,
        string RawInput,
        string Prompt);
}
