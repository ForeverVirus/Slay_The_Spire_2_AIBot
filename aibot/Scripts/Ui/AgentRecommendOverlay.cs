using Godot;
using MegaCrit.Sts2.Core.AutoSlay.Helpers;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.Events.Custom.CrystalSphere;
using MegaCrit.Sts2.Core.Nodes.Relics;
using MegaCrit.Sts2.Core.Nodes.Rewards;
using MegaCrit.Sts2.Core.Nodes.RestSite;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Runs;
using aibot.Scripts.Agent;
using aibot.Scripts.Core;
using aibot.Scripts.Decision;

namespace aibot.Scripts.Ui;

public sealed partial class AgentRecommendOverlay : CanvasLayer
{
    private static AgentRecommendOverlay? _instance;

    private readonly List<RecommendationBadge> _badges = new();
    private AiBotRuntime? _runtime;
    private string _lastSignature = string.Empty;
    private bool _refreshInFlight;
    private double _lastRefreshTime;

    public AgentRecommendOverlay()
    {
        Layer = 215;
        ProcessMode = ProcessModeEnum.Always;
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
        _instance = new AgentRecommendOverlay
        {
            Name = "AgentRecommendOverlay",
            _runtime = runtime
        };
        root.AddChild(_instance);
    }

    public static void ShowOverlay()
    {
        if (_instance is null)
        {
            return;
        }

        if (_instance._runtime?.Config.Ui.ShowRecommendOverlay == false)
        {
            _instance.Visible = false;
            return;
        }

        _instance.Visible = true;
    }

    public static void HideOverlay()
    {
        if (_instance is null)
        {
            return;
        }

        _instance.Visible = false;
        _instance.ClearBadges();
    }

    public override void _Process(double delta)
    {
        base._Process(delta);

        UpdateBadgePositions();

        if (!Visible || _runtime is null)
        {
            return;
        }

        if (AgentCore.Instance.CurrentMode != AgentMode.Assist)
        {
            if (_badges.Count > 0)
            {
                ClearBadges();
            }

            return;
        }

        if (_refreshInFlight || Time.GetTicksMsec() - _lastRefreshTime < 650)
        {
            return;
        }

        var signature = BuildSignature();
        if (signature == _lastSignature)
        {
            return;
        }

        _lastSignature = signature;
        _lastRefreshTime = Time.GetTicksMsec();
        _refreshInFlight = true;
        TaskHelper.RunSafely(RefreshAsync());
    }

    private async Task RefreshAsync()
    {
        try
        {
            if (_runtime?.DecisionEngine is null)
            {
                ClearBadges();
                return;
            }

            var added = await TryRefreshOverlayRecommendationAsync()
                || await TryRefreshCombatRecommendationAsync()
                || await TryRefreshShopRecommendationAsync()
                || await TryRefreshRestSiteRecommendationAsync()
                || await TryRefreshEventRecommendationAsync()
                || await TryRefreshMapRecommendationAsync();

            if (!added)
            {
                ClearBadges();
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"[AiBot.Agent] Recommend overlay refresh failed: {ex.Message}");
            ClearBadges();
        }
        finally
        {
            _refreshInFlight = false;
        }
    }

    private async Task<bool> TryRefreshOverlayRecommendationAsync()
    {
        var overlay = NOverlayStack.Instance?.Peek();
        switch (overlay)
        {
            case NCardRewardSelectionScreen rewardScreen:
                return await RefreshCardRewardRecommendationAsync(rewardScreen);
            case NChooseABundleSelectionScreen bundleScreen:
                return await RefreshBundleRecommendationAsync(bundleScreen);
            case NChooseARelicSelection relicScreen:
                return await RefreshRelicRecommendationAsync(relicScreen);
            case NCrystalSphereScreen crystalSphereScreen:
                return await RefreshCrystalSphereRecommendationAsync(crystalSphereScreen);
            case NRewardsScreen rewardsScreen:
                return await RefreshRewardsRecommendationAsync(rewardsScreen);
            default:
                return false;
        }
    }

    private async Task<bool> RefreshCardRewardRecommendationAsync(NCardRewardSelectionScreen screen)
    {
        if (_runtime?.DecisionEngine is null)
        {
            return false;
        }

        var holders = UiHelper.FindAll<NCardHolder>(screen)
            .Where(holder => holder.CardModel is not null && holder.IsVisibleInTree())
            .ToList();
        if (holders.Count == 0)
        {
            return false;
        }

        var analysis = _runtime.GetCurrentAnalysis();
        var options = holders.Select(holder => holder.CardModel).Where(card => card is not null).Cast<CardModel>().ToList();
        var decision = await _runtime.DecisionEngine.ChooseCardRewardAsync(options, analysis, CancellationToken.None);
        var target = decision.Card is not null
            ? holders.FirstOrDefault(holder => holder.CardModel == decision.Card || holder.CardModel?.Id == decision.Card.Id)
            : holders.FirstOrDefault();
        if (target is null)
        {
            return false;
        }

        ReplaceWithSingleBadge(target, decision.Reason, "卡牌奖励推荐");
        return true;
    }

    private async Task<bool> RefreshRelicRecommendationAsync(NChooseARelicSelection screen)
    {
        if (_runtime?.DecisionEngine is null)
        {
            return false;
        }

        var holders = UiHelper.FindAll<NRelicBasicHolder>(screen)
            .Where(holder => holder.Relic?.Model is not null && holder.IsVisibleInTree())
            .ToList();
        if (holders.Count == 0)
        {
            return false;
        }

        var analysis = _runtime.GetCurrentAnalysis();
        var options = holders.Select(holder => holder.Relic.Model).ToList();
        var decision = await _runtime.DecisionEngine.ChooseRelicAsync(options, nameof(NChooseARelicSelection), false, analysis, CancellationToken.None);
        var target = decision.Relic is not null
            ? holders.FirstOrDefault(holder => holder.Relic.Model == decision.Relic || holder.Relic.Model.Id == decision.Relic.Id)
            : holders.FirstOrDefault();
        if (target is null)
        {
            return false;
        }

        ReplaceWithSingleBadge(target, decision.Reason, "遗物推荐");
        return true;
    }

    private async Task<bool> RefreshBundleRecommendationAsync(NChooseABundleSelectionScreen screen)
    {
        if (_runtime?.DecisionEngine is null)
        {
            return false;
        }

        var bundles = UiHelper.FindAll<NCardBundle>(screen)
            .Select((bundle, index) => new { Bundle = bundle, Index = index })
            .Where(entry => entry.Bundle.Bundle is { Count: > 0 } && entry.Bundle.IsVisibleInTree())
            .ToList();
        if (bundles.Count == 0)
        {
            return false;
        }

        var context = new AiCardSelectionContext(AiCardSelectionKind.BundleChoice, "Choose one card bundle.", 1, 1, Cancelable: false, Zone: "bundle", Source: nameof(NChooseABundleSelectionScreen), ExtraInfo: $"BundleCount={bundles.Count}");
        var decision = await _runtime.DecisionEngine.ChooseBundleAsync(
            context,
            bundles.Select(entry => new CardBundleOption(entry.Index, entry.Bundle.Bundle)).ToList(),
            _runtime.GetCurrentAnalysis(),
            CancellationToken.None);

        var target = bundles.FirstOrDefault(entry => entry.Index == decision.SelectedIndex)?.Bundle ?? bundles[0].Bundle;
        ReplaceWithSingleBadge(target, decision.Reason, "Bundle 推荐");
        return true;
    }

    private async Task<bool> RefreshCrystalSphereRecommendationAsync(NCrystalSphereScreen screen)
    {
        if (_runtime?.DecisionEngine is null)
        {
            return false;
        }

        var proceedButton = screen.GetNodeOrNull<NProceedButton>("%ProceedButton");
        if (proceedButton is not null && proceedButton.IsEnabled)
        {
            ReplaceWithSingleBadge(proceedButton, "当前可以直接结束 Crystal Sphere 选择。", "Crystal Sphere 推荐");
            return true;
        }

        var cellsContainer = screen.GetNodeOrNull<Control>("%Cells");
        if (cellsContainer is null)
        {
            return false;
        }

        var hiddenCells = UiHelper.FindAll<NCrystalSphereCell>(cellsContainer)
            .Where(cell => cell.Visible && cell.Entity.IsHidden && cell.IsVisibleInTree())
            .ToList();
        if (hiddenCells.Count == 0)
        {
            return false;
        }

        var entityField = typeof(NCrystalSphereScreen).GetField("_entity", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var minigame = entityField?.GetValue(screen) as MegaCrit.Sts2.Core.Events.Custom.CrystalSphereEvent.CrystalSphereMinigame;
        if (minigame is null)
        {
            return false;
        }

        var decision = await _runtime.DecisionEngine.ChooseCrystalSphereActionAsync(minigame, _runtime.GetCurrentAnalysis(), CancellationToken.None);
        var target = hiddenCells.FirstOrDefault(cell => cell.Entity.X == decision.X && cell.Entity.Y == decision.Y) ?? hiddenCells[0];
        var reason = decision.Reason + (decision.UseBigDivination ? "（推荐大范围占卜）" : "（推荐小范围占卜）");
        ReplaceWithSingleBadge(target, reason, "Crystal Sphere 推荐");
        return true;
    }

    private async Task<bool> RefreshRewardsRecommendationAsync(NRewardsScreen screen)
    {
        if (_runtime?.DecisionEngine is null)
        {
            return false;
        }

        var player = LocalContext.GetMe(RunManager.Instance.DebugOnlyGetState());
        var allButtons = UiHelper.FindAll<NRewardButton>(screen).ToList();
        var buttons = allButtons
            .Where(button => button.IsEnabled)
            .Where(button => button.Visible && button.IsVisibleInTree())
            .ToList();
        if (buttons.Count == 0)
        {
            return false;
        }

        var decision = await _runtime.DecisionEngine.ChooseRewardAsync(buttons, player?.HasOpenPotionSlots ?? false, _runtime.GetCurrentAnalysis(), CancellationToken.None);
        var target = decision.Button ?? buttons[0];
        ReplaceWithSingleBadge(target, decision.Reason, "奖励领取推荐");
        return true;
    }

    private async Task<bool> TryRefreshMapRecommendationAsync()
    {
        if (_runtime?.DecisionEngine is null || NMapScreen.Instance is null || !NMapScreen.Instance.IsVisibleInTree() || NMapScreen.Instance.IsTraveling)
        {
            return false;
        }

        var runState = RunManager.Instance.DebugOnlyGetState();
        if (runState is null)
        {
            return false;
        }

        var player = LocalContext.GetMe(runState);
        if (player is null)
        {
            return false;
        }

        var allMapPoints = UiHelper.FindAll<NMapPoint>(NMapScreen.Instance).ToList();
        if (allMapPoints.Count == 0)
        {
            return false;
        }

        var pointLookup = allMapPoints.ToDictionary(point => point.Point.coord, point => point);
        var candidateNodes = ResolveCandidateMapNodes(runState, allMapPoints, pointLookup)
            .Where(node => node.State == MapPointState.Travelable && node.IsVisibleInTree())
            .OrderBy(node => node.Point.coord.col)
            .ToList();

        if (candidateNodes.Count == 0)
        {
            candidateNodes = allMapPoints
                .Where(node => node.State == MapPointState.Travelable && node.IsVisibleInTree())
                .OrderBy(node => node.Point.coord.row)
                .ThenBy(node => node.Point.coord.col)
                .ToList();
        }

        if (candidateNodes.Count == 0)
        {
            return false;
        }

        var analysis = _runtime.GetCurrentAnalysis();
        var decision = await _runtime.DecisionEngine.ChooseMapPointAsync(
            candidateNodes.Select(node => node.Point).ToList(),
            player.Creature.CurrentHp,
            player.Creature.MaxHp,
            player.Gold,
            analysis,
            CancellationToken.None);

        var target = decision.Point is not null && pointLookup.TryGetValue(decision.Point.coord, out var nodeTarget)
            ? nodeTarget
            : candidateNodes[0];

        ReplaceWithSingleBadge(target, decision.Reason, "路线推荐");
        return true;
    }

    private async Task<bool> TryRefreshCombatRecommendationAsync()
    {
        if (_runtime?.DecisionEngine is null || !CombatManager.Instance.IsInProgress || !CombatManager.Instance.IsPlayPhase)
        {
            return false;
        }

        var runState = RunManager.Instance.DebugOnlyGetState();
        var player = LocalContext.GetMe(runState);
        if (player is null)
        {
            return false;
        }

        var hand = PileType.Hand.GetPile(player).Cards.ToList();
        var playable = hand.Where(CanRecommendPlayCard).ToList();
        if (playable.Count == 0)
        {
            return false;
        }

        var visibleHolders = UiHelper.FindAll<NCardHolder>(((SceneTree)Engine.GetMainLoop()).Root)
            .Where(holder => holder.CardModel is not null && holder.IsVisibleInTree())
            .ToList();
        if (visibleHolders.Count == 0)
        {
            return false;
        }

        var enemies = player.Creature.CombatState?.HittableEnemies?.Where(enemy => enemy.IsAlive).ToList() ?? new List<Creature>();
        var analysis = _runtime.GetCurrentAnalysis();
        var decision = await _runtime.DecisionEngine.ChooseCombatActionAsync(player, playable, enemies, analysis, CancellationToken.None);
        if (decision.EndTurn || decision.Card is null)
        {
            return false;
        }

        var target = visibleHolders.FirstOrDefault(holder => holder.CardModel == decision.Card || holder.CardModel?.Id == decision.Card.Id);
        if (target is null)
        {
            return false;
        }

        ReplaceWithSingleBadge(target, decision.Reason, "战斗手牌推荐");
        return true;
    }

    private async Task<bool> TryRefreshShopRecommendationAsync()
    {
        if (_runtime?.DecisionEngine is null)
        {
            return false;
        }

        var room = GetAbsoluteNodeOrNull<NMerchantRoom>("/root/Game/RootSceneContainer/Run/RoomContainer/MerchantRoom");
        if (room?.Inventory is null || !room.Inventory.IsVisibleInTree() || !room.Inventory.IsOpen || room.Inventory.Inventory is null)
        {
            return false;
        }

        var runState = RunManager.Instance.DebugOnlyGetState();
        var player = LocalContext.GetMe(runState);
        if (player is null)
        {
            return false;
        }

        var inventory = room.Inventory.Inventory;
        var options = inventory.AllEntries
            .Where(entry => entry.IsStocked && entry.EnoughGold)
            .Where(entry => player.HasOpenPotionSlots || entry is not MerchantPotionEntry)
            .ToList();
        if (options.Count == 0)
        {
            return false;
        }

        var decision = await _runtime.DecisionEngine.ChooseShopPurchaseAsync(options, player.Gold, player.HasOpenPotionSlots, _runtime.GetCurrentAnalysis(), CancellationToken.None);
        if (decision.Entry is null)
        {
            return false;
        }

        var slot = room.Inventory.GetAllSlots().FirstOrDefault(candidate => candidate.Entry == decision.Entry);
        if (slot is null || !slot.IsVisibleInTree())
        {
            return false;
        }

        ReplaceWithSingleBadge(slot, decision.Reason, "商店推荐");
        return true;
    }

    private async Task<bool> TryRefreshRestSiteRecommendationAsync()
    {
        var room = GetAbsoluteNodeOrNull<NRestSiteRoom>("/root/Game/RootSceneContainer/Run/RoomContainer/RestSiteRoom");
        if (room is null || _runtime?.DecisionEngine is null)
        {
            return false;
        }

        var runState = RunManager.Instance.DebugOnlyGetState();
        var player = LocalContext.GetMe(runState);
        if (player is null)
        {
            return false;
        }

        var buttons = UiHelper.FindAll<NRestSiteButton>(room)
            .Where(button => button.IsEnabled && button.Visible && button.IsVisibleInTree())
            .ToList();
        if (buttons.Count == 0)
        {
            return false;
        }

        var options = buttons.Select(button => button.Option).ToList();
        var decision = await _runtime.DecisionEngine.ChooseRestSiteOptionAsync(player, options, _runtime.GetCurrentAnalysis(), CancellationToken.None);
        var target = decision.Option is not null
            ? buttons.FirstOrDefault(button => button.Option.OptionId == decision.Option.OptionId)
            : buttons.FirstOrDefault();
        if (target is null)
        {
            return false;
        }

        ReplaceWithSingleBadge(target, decision.Reason, "休息点推荐");
        return true;
    }

    private async Task<bool> TryRefreshEventRecommendationAsync()
    {
        var eventRoom = GetAbsoluteNodeOrNull<NEventRoom>("/root/Game/RootSceneContainer/Run/RoomContainer/EventRoom");
        if (eventRoom is null || !eventRoom.Visible || !eventRoom.IsVisibleInTree() || _runtime?.DecisionEngine is null)
        {
            return false;
        }

        var buttons = UiHelper.FindAll<NEventOptionButton>(eventRoom)
            .Where(button => button.IsEnabled && !button.Option.IsLocked && button.IsVisibleInTree())
            .ToList();
        if (buttons.Count == 0)
        {
            return false;
        }

        var eventModel = buttons[0].Event;
        var decision = await _runtime.DecisionEngine.ChooseEventOptionAsync(eventModel, buttons.Select(button => button.Option).ToList(), _runtime.GetCurrentAnalysis(), CancellationToken.None);
        var target = decision.Option is not null
            ? buttons.FirstOrDefault(button => button.Option == decision.Option || button.Option.TextKey == decision.Option.TextKey)
            : buttons.FirstOrDefault();
        if (target is null)
        {
            return false;
        }

        ReplaceWithSingleBadge(target, decision.Reason, "事件选项推荐");
        return true;
    }

    private void ReplaceWithSingleBadge(Control target, string reason, string category)
    {
        ClearBadges();

        var badge = new RecommendationBadge(target, reason, category);
        _badges.Add(badge);
        AddChild(badge.Container);
        badge.UpdatePosition();
    }

    private void ClearBadges()
    {
        foreach (var badge in _badges)
        {
            if (GodotObject.IsInstanceValid(badge.Container))
            {
                badge.Container.QueueFree();
            }
        }

        _badges.Clear();
    }

    private void UpdateBadgePositions()
    {
        for (var index = _badges.Count - 1; index >= 0; index--)
        {
            var badge = _badges[index];
            if (!badge.IsValid())
            {
                if (GodotObject.IsInstanceValid(badge.Container))
                {
                    badge.Container.QueueFree();
                }

                _badges.RemoveAt(index);
                continue;
            }

            badge.UpdatePosition();
        }
    }

    private string BuildSignature()
    {
        var overlay = NOverlayStack.Instance?.Peek();
        if (overlay is NCardRewardSelectionScreen rewardScreen)
        {
            var names = UiHelper.FindAll<NCardHolder>(rewardScreen)
                .Where(holder => holder.CardModel is not null)
                .Select(holder => holder.CardModel!.Title.ToString())
                .ToList();
            return "card-reward:" + string.Join("|", names);
        }

        if (overlay is NChooseARelicSelection relicScreen)
        {
            var names = UiHelper.FindAll<NRelicBasicHolder>(relicScreen)
                .Where(holder => holder.Relic?.Model is not null)
                .Select(holder => holder.Relic.Model.Title.GetFormattedText())
                .ToList();
            return "relic:" + string.Join("|", names);
        }

        if (overlay is NChooseABundleSelectionScreen bundleScreen)
        {
            var bundles = UiHelper.FindAll<NCardBundle>(bundleScreen)
                .Where(bundle => bundle.Bundle is { Count: > 0 })
                .Select(bundle => string.Join(",", bundle.Bundle.Select(card => card.Id.ToString())))
                .ToList();
            return "bundle:" + string.Join("|", bundles);
        }

        if (overlay is NCrystalSphereScreen crystalSphereScreen)
        {
            var cellsContainer = crystalSphereScreen.GetNodeOrNull<Control>("%Cells");
            var cells = cellsContainer is null
                ? new List<string>()
                : UiHelper.FindAll<NCrystalSphereCell>(cellsContainer)
                    .Where(cell => cell.Visible && cell.Entity.IsHidden)
                    .Select(cell => $"{cell.Entity.X}:{cell.Entity.Y}")
                    .ToList();
            return "crystal:" + string.Join("|", cells);
        }

        if (overlay is NRewardsScreen rewardsScreen)
        {
            var rewards = UiHelper.FindAll<NRewardButton>(rewardsScreen)
                .Where(button => button.Visible && button.IsVisibleInTree())
                .Select(button => button.Reward?.GetType().Name ?? "reward")
                .ToList();
            return "rewards:" + string.Join("|", rewards);
        }

        if (NMapScreen.Instance is not null && NMapScreen.Instance.IsVisibleInTree() && !NMapScreen.Instance.IsTraveling)
        {
            var coords = UiHelper.FindAll<NMapPoint>(NMapScreen.Instance)
                .Where(node => node.State == MapPointState.Travelable)
                .Select(node => $"{node.Point.coord.row}:{node.Point.coord.col}:{node.Point.PointType}")
                .ToList();
            return "map:" + string.Join("|", coords);
        }

        if (CombatManager.Instance.IsInProgress && CombatManager.Instance.IsPlayPhase)
        {
            var combatCards = UiHelper.FindAll<NCardHolder>(((SceneTree)Engine.GetMainLoop()).Root)
                .Where(holder => holder.CardModel is not null && holder.IsVisibleInTree())
                .Select(holder => holder.CardModel!.Id.ToString())
                .ToList();
            return "combat:" + string.Join("|", combatCards);
        }

        var merchantRoom = GetAbsoluteNodeOrNull<NMerchantRoom>("/root/Game/RootSceneContainer/Run/RoomContainer/MerchantRoom");
        if (merchantRoom?.Inventory is not null && merchantRoom.Inventory.IsOpen && merchantRoom.Inventory.IsVisibleInTree())
        {
            var entries = merchantRoom.Inventory.GetAllSlots()
                .Where(slot => slot.Entry is not null && slot.Entry.IsStocked)
                .Select(slot => slot.Entry.GetType().Name + ":" + slot.Entry.Cost)
                .ToList();
            return "shop:" + string.Join("|", entries);
        }

        var restRoom = GetAbsoluteNodeOrNull<NRestSiteRoom>("/root/Game/RootSceneContainer/Run/RoomContainer/RestSiteRoom");
        if (restRoom is not null)
        {
            var restOptions = UiHelper.FindAll<NRestSiteButton>(restRoom)
                .Where(button => button.IsEnabled && button.Visible && button.IsVisibleInTree())
                .Select(button => button.Option.OptionId)
                .ToList();
            if (restOptions.Count > 0)
            {
                return "rest:" + string.Join("|", restOptions);
            }
        }

        var eventRoom = GetAbsoluteNodeOrNull<NEventRoom>("/root/Game/RootSceneContainer/Run/RoomContainer/EventRoom");
        if (eventRoom is not null && eventRoom.Visible && eventRoom.IsVisibleInTree())
        {
            var eventOptions = UiHelper.FindAll<NEventOptionButton>(eventRoom)
                .Where(button => button.IsEnabled && !button.Option.IsLocked && button.IsVisibleInTree())
                .Select(button => button.Option.TextKey)
                .ToList();
            if (eventOptions.Count > 0)
            {
                return "event:" + string.Join("|", eventOptions);
            }
        }

        return string.Empty;
    }

    private static bool CanRecommendPlayCard(CardModel card)
    {
        AbstractModel? preventer;
        UnplayableReason reason;
        return card.CanPlay(out reason, out preventer);
    }

    private static T? GetAbsoluteNodeOrNull<T>(string path) where T : class
    {
        var root = ((SceneTree)Engine.GetMainLoop()).Root;
        return root.GetNodeOrNull(path) as T;
    }

    private static List<NMapPoint> ResolveCandidateMapNodes(RunState runState, List<NMapPoint> allMapPoints, Dictionary<MapCoord, NMapPoint> pointLookup)
    {
        if (runState.VisitedMapCoords.Count == 0)
        {
            return allMapPoints
                .Where(point => point.Point.coord.row == 0)
                .OrderBy(point => point.Point.coord.col)
                .ToList();
        }

        var lastCoord = runState.VisitedMapCoords[^1];
        var lastNode = allMapPoints.FirstOrDefault(point => point.Point.coord.Equals(lastCoord));
        if (lastNode is null)
        {
            return new List<NMapPoint>();
        }

        return lastNode.Point.Children
            .Select(child => pointLookup.TryGetValue(child.coord, out var childNode) ? childNode : null)
            .Where(childNode => childNode is not null)
            .Cast<NMapPoint>()
            .OrderBy(point => point.Point.coord.col)
            .ToList();
    }

    private sealed class RecommendationBadge
    {
        private readonly WeakReference<Control> _targetRef;

        public RecommendationBadge(Control target, string reason, string category)
        {
            _targetRef = new WeakReference<Control>(target);
            Container = new PanelContainer
            {
                MouseFilter = Control.MouseFilterEnum.Ignore,
                TooltipText = $"{category}\n{reason}",
                Modulate = new Color(1f, 0.95f, 0.55f)
            };

            var margin = new MarginContainer();
            margin.AddThemeConstantOverride("margin_left", 6);
            margin.AddThemeConstantOverride("margin_right", 6);
            margin.AddThemeConstantOverride("margin_top", 2);
            margin.AddThemeConstantOverride("margin_bottom", 2);
            Container.AddChild(margin);

            var label = new Label
            {
                Text = "推荐",
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
            margin.AddChild(label);
        }

        public PanelContainer Container { get; }

        public bool IsValid()
        {
            if (!_targetRef.TryGetTarget(out var target) || !GodotObject.IsInstanceValid(target))
            {
                return false;
            }

            return target.IsVisibleInTree();
        }

        public void UpdatePosition()
        {
            if (!_targetRef.TryGetTarget(out var target) || !GodotObject.IsInstanceValid(target))
            {
                return;
            }

            var x = target.GlobalPosition.X + Mathf.Max(0f, target.Size.X - 52f);
            var y = target.GlobalPosition.Y - 18f;
            Container.Position = new Vector2(x, y);
        }
    }
}