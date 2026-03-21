using System.Text;
using aibot.Scripts.Core;
using aibot.Scripts.Knowledge;

namespace aibot.Scripts.Agent.Tools;

public sealed class LookupRelicTool : RuntimeBackedToolBase
{
    public LookupRelicTool(AiBotRuntime runtime) : base(runtime)
    {
    }

    public override string Name => "lookup_relic";

    public override string Description => "从知识库按名称查询遗物信息。";

    public override Task<string> QueryAsync(string? parameters, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(parameters) || Runtime.KnowledgeBase is null)
        {
            return Task.FromResult("请提供遗物名称。当前知识库不可用时无法查询遗物。");
        }

        var analysis = Runtime.GetCurrentAnalysis();
        var relic = Runtime.KnowledgeBase.FindRelic(parameters.Trim(), analysis.CharacterId)
            ?? Runtime.KnowledgeBase.FindRelic(parameters.Trim());
        if (relic is null)
        {
            return Task.FromResult($"未在当前知识库中找到遗物：{parameters.Trim()}");
        }

        var builder = new StringBuilder();
        builder.AppendLine($"遗物：{relic.NameEn} / {relic.NameZh}");
        builder.AppendLine($"Slug：{relic.Slug}");
        if (!string.IsNullOrWhiteSpace(relic.DescriptionZh))
        {
            builder.AppendLine($"描述(ZH)：{KnowledgeTextFormatter.FormatRelicText(relic, relic.DescriptionZh)}");
        }
        if (!string.IsNullOrWhiteSpace(relic.DescriptionEn))
        {
            builder.AppendLine($"描述(EN)：{KnowledgeTextFormatter.FormatRelicText(relic, relic.DescriptionEn)}");
        }
        return Task.FromResult(builder.ToString().Trim());
    }
}
