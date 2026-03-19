using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.TestSupport;
using aibot.Scripts.Decision;

namespace aibot.Scripts.Core;

public sealed class AiBotCardSelector : ICardSelector
{
    private readonly Func<RunAnalysis> _analysisFactory;
    private readonly IAiDecisionEngine _decisionEngine;

    public AiBotCardSelector(Func<RunAnalysis> analysisFactory, IAiDecisionEngine decisionEngine)
    {
        _analysisFactory = analysisFactory;
        _decisionEngine = decisionEngine;
    }

    public async Task<IEnumerable<CardModel>> GetSelectedCards(IEnumerable<CardModel> options, int minSelect, int maxSelect)
    {
        var list = options.ToList();
        if (list.Count == 0)
        {
            return Array.Empty<CardModel>();
        }

        var analysis = _analysisFactory();
        var picked = new List<CardModel>();
        var remaining = new List<CardModel>(list);
        var count = Math.Max(minSelect, Math.Min(maxSelect, list.Count));

        while (picked.Count < count && remaining.Count > 0)
        {
            var decision = await _decisionEngine.ChooseCardRewardAsync(remaining, analysis, CancellationToken.None);
            var choice = decision.Card ?? remaining[0];
            picked.Add(choice);
            remaining.Remove(choice);
        }

        Log.Info($"[AiBot] Card selector picked {picked.Count} card(s).");
        return picked;
    }

    public CardModel? GetSelectedCardReward(IReadOnlyList<CardCreationResult> options, IReadOnlyList<CardRewardAlternative> alternatives)
    {
        if (options.Count == 0)
        {
            return null;
        }

        var analysis = _analysisFactory();
        var cards = options.Select(option => option.Card).ToList();
        var decision = _decisionEngine.ChooseCardRewardAsync(cards, analysis, CancellationToken.None).GetAwaiter().GetResult();
        return decision.Card ?? cards[0];
    }
}
