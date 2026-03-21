using System.Text;
using aibot.Scripts.Core;
using aibot.Scripts.Knowledge;

namespace aibot.Scripts.Agent.Tools;

public sealed class LookupCardTool : RuntimeBackedToolBase
{
    public LookupCardTool(AiBotRuntime runtime) : base(runtime)
    {
    }

    public override string Name => "lookup_card";

    public override string Description => "从知识库按名称查询卡牌信息。";

    public override Task<string> QueryAsync(string? parameters, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(parameters) || Runtime.KnowledgeBase is null)
        {
            return Task.FromResult("请提供卡牌名称。当前知识库不可用时无法查询卡牌。");
        }

        var analysis = Runtime.GetCurrentAnalysis();
        var card = Runtime.KnowledgeBase.FindCard(parameters.Trim(), analysis.CharacterId)
            ?? Runtime.KnowledgeBase.FindCard(parameters.Trim());
        if (card is null)
        {
            return Task.FromResult($"未在当前知识库中找到卡牌：{parameters.Trim()}");
        }

        var builder = new StringBuilder();
        builder.AppendLine($"卡牌：{card.NameEn} / {card.NameZh}");
        builder.AppendLine($"Slug：{card.Slug}");
        builder.AppendLine($"类型：{card.CardType ?? "Unknown"}");
        if (!string.IsNullOrWhiteSpace(card.DescriptionZh))
        {
            builder.AppendLine($"描述(ZH)：{KnowledgeTextFormatter.FormatCardText(card, card.DescriptionZh)}");
        }
        if (!string.IsNullOrWhiteSpace(card.DescriptionEn))
        {
            builder.AppendLine($"描述(EN)：{KnowledgeTextFormatter.FormatCardText(card, card.DescriptionEn)}");
        }
        return Task.FromResult(builder.ToString().Trim());
    }
}
