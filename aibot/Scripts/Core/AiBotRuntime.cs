using System.Collections.Concurrent;
using Godot;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.AutoSlay.Helpers;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Events.Custom.CrystalSphere;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Potions;
using MegaCrit.Sts2.Core.Nodes.Relics;
using MegaCrit.Sts2.Core.Nodes.Rewards;
using MegaCrit.Sts2.Core.Nodes.RestSite;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using aibot.Scripts.Agent;
using aibot.Scripts.Config;
using aibot.Scripts.Decision;
using aibot.Scripts.Knowledge;
using aibot.Scripts.Localization;
using aibot.Scripts.Ui;

namespace aibot.Scripts.Core;

public sealed class AiBotRuntime : IDisposable
{
    private readonly SemaphoreSlim _tickGate = new(1, 1);
    private readonly ConcurrentDictionary<string, byte> _processedRoomKeys = new();
    private readonly ConcurrentDictionary<string, DateTime> _logThrottle = new();

    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;
    private IDisposable? _selectorScope;
    private string? _modDirectory;
    private DateTime _nextActionAtUtc = DateTime.MinValue;
    private AiBotDecisionPanel? _decisionPanel;

    public static AiBotRuntime Instance { get; } = new();

    public AiBotConfig Config { get; private set; } = new();

    public GuideKnowledgeBase? KnowledgeBase { get; private set; }

    public IAiDecisionEngine? DecisionEngine { get; private set; }

    public AiBotStateAnalyzer? StateAnalyzer { get; private set; }

    public RunAnalysis? CurrentAnalysis { get; private set; }

    public bool IsInitialized { get; private set; }

    public bool IsActive => _loopCts is { IsCancellationRequested: false };

    public event Action<AiBotLanguage>? UiLanguageChanged;

    private AiBotRuntime()
    {
    }

    public void Initialize(string modDirectory)
    {
        if (IsInitialized)
        {
            return;
        }

        _modDirectory = modDirectory;
        Config = AiBotConfigLoader.Load(modDirectory);
        KnowledgeBase = new GuideKnowledgeBase(modDirectory, Config);
        KnowledgeBase.Load();
        StateAnalyzer = new AiBotStateAnalyzer(KnowledgeBase);

        var heuristic = new GuideHeuristicDecisionEngine(KnowledgeBase);
        DeepSeekDecisionEngine? cloud = Config.CanUseCloud ? new DeepSeekDecisionEngine(Config, KnowledgeBase) : null;
        DecisionEngine = new HybridDecisionEngine(Config, heuristic, cloud);

        IsInitialized = true;
        AgentCore.Instance.Initialize(this);
        EnsureDecisionPanel();
        UpdateDecisionPanelVisibility();
        Log.Info($"[AiBot] Runtime initialized. CloudEnabled={Config.CanUseCloud}");
    }

    public void NotifyNewRunTask(Task<RunState> runTask)
    {
        if (!IsInitialized || !Config.Enabled || !Config.AutoTakeOverNewRun)
        {
            return;
        }

        TaskHelper.RunSafely(ActivateWhenTaskCompletesAsync(runTask, "new-run"));
    }

    public void NotifyLoadRunTask(Task loadTask)
    {
        if (!IsInitialized || !Config.Enabled || !Config.AutoTakeOverContinueRun)
        {
            return;
        }

        TaskHelper.RunSafely(ActivateWhenTaskCompletesAsync(loadTask, "continue-run"));
    }

    public RunAnalysis GetCurrentAnalysis()
    {
        if (CurrentAnalysis is not null)
        {
            return CurrentAnalysis;
        }

        var runState = RunManager.Instance.DebugOnlyGetState();
        if (runState is null || StateAnalyzer is null)
        {
            return new RunAnalysis(0, "Unknown", "Generalist", string.Empty, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);
        }

        CurrentAnalysis = StateAnalyzer.Analyze(runState);
        return CurrentAnalysis;
    }

    private async Task ActivateWhenTaskCompletesAsync(Task task, string reason)
    {
        try
        {
            await task;
            await WaitForRuntimeReadyAsync();
            Activate(reason);
        }
        catch (Exception ex)
        {
            Log.Error($"[AiBot] Failed to activate after {reason}: {ex}");
        }
    }

    public void Activate(string reason)
    {
        if (!IsInitialized)
        {
            return;
        }

        TaskHelper.RunSafely(AgentCore.Instance.ActivateDefaultModeAsync(reason));
    }

    internal void ActivateLegacyFullAuto(string reason)
    {
        if (!IsInitialized || DecisionEngine is null || StateAnalyzer is null)
        {
            return;
        }

        DeactivateLegacyFullAuto();

        var runState = RunManager.Instance.DebugOnlyGetState();
        if (runState is null)
        {
            Log.Warn($"[AiBot] Activate skipped for {reason}: run state is null.");
            return;
        }

        CurrentAnalysis = StateAnalyzer.Analyze(runState);
        _processedRoomKeys.Clear();
        AiBotDecisionFeed.Clear();
        _loopCts = new CancellationTokenSource();
        TryInstallSelector();
        _loopTask = TaskHelper.RunSafely(RunLoopAsync(_loopCts.Token));
        Log.Info($"[AiBot] Activated from {reason}. Character={CurrentAnalysis.CharacterName}, Build={CurrentAnalysis.RecommendedBuildName}");
        LogThrottled("activate-state", $"[AiBot] Activation state: room={runState.CurrentRoom?.RoomType}, visited={FormatMapCoords(runState.VisitedMapCoords)}, mapVisible={NMapScreen.Instance?.IsVisibleInTree() ?? false}", TimeSpan.FromSeconds(2));
    }

    public void Deactivate()
    {
        TaskHelper.RunSafely(AgentCore.Instance.DeactivateCurrentModeAsync());
    }

    internal void DeactivateLegacyFullAuto()
    {
        if (_loopCts is not null)
        {
            _loopCts.Cancel();
            _loopCts.Dispose();
            _loopCts = null;
        }

        _loopTask = null;
        _nextActionAtUtc = DateTime.MinValue;
        _processedRoomKeys.Clear();
        DisposeSelector();
        UpdateDecisionPanelVisibility();
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (!await _tickGate.WaitAsync(0, cancellationToken))
            {
                await Task.Delay(Config.PollIntervalMs, cancellationToken);
                continue;
            }

            try
            {
                if (NRun.Instance is null)
                {
                    LogThrottled("loop-wait-run", "[AiBot] Loop waiting: NRun.Instance is null.", TimeSpan.FromSeconds(2));
                    await Task.Delay(Config.PollIntervalMs, cancellationToken);
                    continue;
                }

                await TickOnceAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                Log.Error($"[AiBot] Tick failed: {ex}");
            }
            finally
            {
                _tickGate.Release();
            }

            await Task.Delay(Config.PollIntervalMs, cancellationToken);
        }
    }

    private async Task TickOnceAsync(CancellationToken cancellationToken)
    {
        var runState = RunManager.Instance.DebugOnlyGetState();
        if (runState is null || DecisionEngine is null || StateAnalyzer is null)
        {
            LogThrottled("tick-no-state", $"[AiBot] Tick skipped: runState={runState is not null}, decisionEngine={DecisionEngine is not null}, analyzer={StateAnalyzer is not null}", TimeSpan.FromSeconds(2));
            return;
        }

        CurrentAnalysis = StateAnalyzer.Analyze(runState);
        EnsureDecisionPanel();

        if (NMapScreen.Instance is not null)
        {
            LogThrottled(
                "tick-map-presence",
                $"[AiBot] Tick state: room={runState.CurrentRoom?.RoomType}, mapVisible={NMapScreen.Instance.IsVisibleInTree()}, mapOpen={NMapScreen.Instance.IsOpen}, mapTraveling={NMapScreen.Instance.IsTraveling}, visited={FormatMapCoords(runState.VisitedMapCoords)}",
                TimeSpan.FromSeconds(2));
        }

        if (await TryHandleOverlayAsync(cancellationToken))
        {
            return;
        }

        if (await TryHandleCombatAsync(runState, cancellationToken))
        {
            return;
        }

        if (NMapScreen.Instance is { IsOpen: true } && await TryHandleMapAsync(runState, cancellationToken))
        {
            return;
        }

        if (await TryHandleRoomAsync(runState, cancellationToken))
        {
            return;
        }

        await TryHandleMapAsync(runState, cancellationToken);
    }

    private async Task<bool> TryHandleOverlayAsync(CancellationToken cancellationToken)
    {
        var overlay = NOverlayStack.Instance?.Peek();
        switch (overlay)
        {
            case NChooseARelicSelection chooseRelicScreen:
                return await HandleChooseRelicScreenAsync(chooseRelicScreen, cancellationToken);
            case NCrystalSphereScreen crystalSphereScreen:
                return await HandleCrystalSphereScreenAsync(crystalSphereScreen, cancellationToken);
            case NCardRewardSelectionScreen cardRewardScreen:
                return await HandleCardRewardScreenAsync(cardRewardScreen, cancellationToken);
            case NChooseABundleSelectionScreen bundleScreen:
                return await HandleBundleSelectionScreenAsync(bundleScreen, cancellationToken);
            case NRewardsScreen rewardsScreen:
                return await HandleRewardsScreenAsync(rewardsScreen, cancellationToken);
            default:
                return false;
        }
    }

    private async Task<bool> HandleCardRewardScreenAsync(NCardRewardSelectionScreen screen, CancellationToken cancellationToken)
    {
        if (DecisionEngine is null)
        {
            return false;
        }

        var holders = UiHelper.FindAll<NCardHolder>(screen)
            .Where(holder => holder.CardModel is not null)
            .ToList();
        if (holders.Count == 0)
        {
            LogThrottled("overlay-cardreward-idle", "[AiBot] Overlay idle: card reward screen has no selectable cards.", TimeSpan.FromSeconds(2));
            return false;
        }

        var options = holders.Select(holder => holder.CardModel).Where(card => card is not null).Cast<CardModel>().ToList();
        var alternativeButtons = UiHelper.FindAll<NCardRewardAlternativeButton>(screen)
            .Select(button => new
            {
                Button = button,
                Label = GetRewardAlternativeLabel(button)
            })
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Label))
            .ToList();

        if (alternativeButtons.Count > 0)
        {
            var context = new AiCardSelectionContext(AiCardSelectionKind.CardReward, "Choose a card reward or reward alternative.", 0, 1, Cancelable: true, Zone: "reward-screen", Source: nameof(NCardRewardSelectionScreen), ExtraInfo: $"CardOptions={options.Count};AlternativeOptions={alternativeButtons.Count}");
            var alternatives = alternativeButtons
                .Select(entry => new DecisionOption(entry.Label!, entry.Label!, "reward alternative"))
                .ToList();
            var rewardChoice = await DecisionEngine.ChooseCardRewardChoiceAsync(context, options, alternatives, GetCurrentAnalysis(), cancellationToken);
            if (!string.IsNullOrWhiteSpace(rewardChoice.AlternativeOptionId))
            {
                var selectedAlternative = alternativeButtons.FirstOrDefault(entry => string.Equals(entry.Label, rewardChoice.AlternativeOptionId, StringComparison.OrdinalIgnoreCase));
                if (selectedAlternative is not null)
                {
                    await WaitForActionWindowAsync(Config.ScreenActionDelayMs, cancellationToken);
                    Log.Info($"[AiBot] Card reward decision: {rewardChoice.Reason}");
                    await UiHelper.Click(selectedAlternative.Button);
                    await WaitForActionQueueToDrainAsync(cancellationToken);
                    ApplyActionCooldown(Config.ScreenActionDelayMs);
                    return true;
                }
            }

            var selectedHolder = holders.FirstOrDefault(holder => holder.CardModel == rewardChoice.Card) ?? holders[0];
            Log.Info($"[AiBot] Card reward decision: {rewardChoice.Reason}");
            selectedHolder.EmitSignal(NCardHolder.SignalName.Pressed, selectedHolder);
            return true;
        }

        var decision = await DecisionEngine.ChooseCardRewardAsync(options, GetCurrentAnalysis(), cancellationToken);
        var chosenHolder = holders.FirstOrDefault(holder => holder.CardModel == decision.Card) ?? holders[0];
        Log.Info($"[AiBot] Card reward decision: {decision.Reason}");
        chosenHolder.EmitSignal(NCardHolder.SignalName.Pressed, chosenHolder);
        return true;
    }

    private async Task<bool> HandleBundleSelectionScreenAsync(NChooseABundleSelectionScreen screen, CancellationToken cancellationToken)
    {
        if (DecisionEngine is null)
        {
            return false;
        }

        var bundles = UiHelper.FindAll<NCardBundle>(screen)
            .Select((bundle, index) => new { Bundle = bundle, Index = index })
            .Where(entry => entry.Bundle.Bundle is { Count: > 0 })
            .ToList();
        if (bundles.Count == 0)
        {
            LogThrottled("overlay-bundle-idle", "[AiBot] Overlay idle: bundle selection screen has no bundles.", TimeSpan.FromSeconds(2));
            return false;
        }

        var context = new AiCardSelectionContext(AiCardSelectionKind.BundleChoice, "Choose one card bundle.", 1, 1, Cancelable: false, Zone: "bundle", Source: nameof(NChooseABundleSelectionScreen), ExtraInfo: $"BundleCount={bundles.Count}");
        var decision = await DecisionEngine.ChooseBundleAsync(
            context,
            bundles.Select(entry => new CardBundleOption(entry.Index, entry.Bundle.Bundle)).ToList(),
            GetCurrentAnalysis(),
            cancellationToken);

        var selectedBundle = bundles.FirstOrDefault(entry => entry.Index == decision.SelectedIndex)?.Bundle ?? bundles[0].Bundle;
        await WaitForActionWindowAsync(Config.ScreenActionDelayMs, cancellationToken);
        Log.Info($"[AiBot] Bundle decision: {decision.Reason}");
        await UiHelper.Click(selectedBundle.Hitbox);

        var confirmButton = UiHelper.FindFirst<NConfirmButton>(screen);
        if (confirmButton is not null)
        {
            await WaitForActionWindowAsync(Config.ScreenActionDelayMs, cancellationToken);
            await UiHelper.Click(confirmButton);
        }

        await WaitForActionQueueToDrainAsync(cancellationToken);
        ApplyActionCooldown(Config.ScreenActionDelayMs);
        return true;
    }

    private async Task<bool> HandleChooseRelicScreenAsync(NChooseARelicSelection screen, CancellationToken cancellationToken)
    {
        if (DecisionEngine is null)
        {
            return false;
        }

        var holders = UiHelper.FindAll<NRelicBasicHolder>(screen)
            .Where(holder => holder.Relic?.Model is not null)
            .ToList();
        if (holders.Count == 0)
        {
            LogThrottled("overlay-choose-relic-idle", "[AiBot] Overlay idle: choose relic screen has no relic holders.", TimeSpan.FromSeconds(2));
            return false;
        }

        var skipButton = UiHelper.FindFirst<NChoiceSelectionSkipButton>(screen);
        var relicModels = holders.Select(holder => holder.Relic.Model).ToList();
        var decision = await DecisionEngine.ChooseRelicAsync(relicModels, nameof(NChooseARelicSelection), skipButton is not null && skipButton.IsVisibleInTree() && skipButton.IsEnabled, GetCurrentAnalysis(), cancellationToken);

        if (decision.SkipSelection && skipButton is not null && skipButton.IsVisibleInTree() && skipButton.IsEnabled)
        {
            await WaitForActionWindowAsync(Config.ScreenActionDelayMs, cancellationToken);
            Log.Info($"[AiBot] Choose relic decision: {decision.Reason}");
            await UiHelper.Click(skipButton);
            await WaitForActionQueueToDrainAsync(cancellationToken);
            ApplyActionCooldown(Config.ScreenActionDelayMs);
            return true;
        }

        var chosenHolder = decision.Relic is not null
            ? holders.FirstOrDefault(holder => holder.Relic.Model == decision.Relic || holder.Relic.Model.Id == decision.Relic.Id) ?? holders[0]
            : holders[0];
        await WaitForActionWindowAsync(Config.ScreenActionDelayMs, cancellationToken);
        Log.Info($"[AiBot] Choose relic decision: {decision.Reason}");
        await UiHelper.Click(chosenHolder);
        await WaitForActionQueueToDrainAsync(cancellationToken);
        ApplyActionCooldown(Config.ScreenActionDelayMs);
        return true;
    }

    private async Task<bool> HandleCrystalSphereScreenAsync(NCrystalSphereScreen screen, CancellationToken cancellationToken)
    {
        if (DecisionEngine is null)
        {
            return false;
        }

        var proceedButton = screen.GetNodeOrNull<NProceedButton>("%ProceedButton");
        if (proceedButton is not null && proceedButton.IsEnabled)
        {
            await WaitForActionWindowAsync(Config.ScreenActionDelayMs, cancellationToken);
            Log.Info("[AiBot] Crystal Sphere decision: proceed.");
            await UiHelper.Click(proceedButton);
            await WaitForActionQueueToDrainAsync(cancellationToken);
            ApplyActionCooldown(Config.ScreenActionDelayMs);
            return true;
        }

        var cellsContainer = screen.GetNodeOrNull<Control>("%Cells");
        if (cellsContainer is null)
        {
            return false;
        }

        var hiddenCells = UiHelper.FindAll<NCrystalSphereCell>(cellsContainer)
            .Where(cell => cell.Visible && cell.Entity.IsHidden)
            .ToList();
        if (hiddenCells.Count == 0)
        {
            LogThrottled("overlay-crystal-sphere-idle", "[AiBot] Overlay idle: Crystal Sphere has no hidden cells.", TimeSpan.FromSeconds(2));
            return false;
        }

        var entityField = typeof(NCrystalSphereScreen).GetField("_entity", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var minigame = entityField?.GetValue(screen) as MegaCrit.Sts2.Core.Events.Custom.CrystalSphereEvent.CrystalSphereMinigame;
        if (minigame is null)
        {
            return false;
        }

        var decision = await DecisionEngine.ChooseCrystalSphereActionAsync(minigame, GetCurrentAnalysis(), cancellationToken);
        var bigButton = screen.GetNodeOrNull<NDivinationButton>("%BigDivinationButton");
        var smallButton = screen.GetNodeOrNull<NDivinationButton>("%SmallDivinationButton");
        var desiredToolButton = decision.UseBigDivination ? bigButton : smallButton;
        if (desiredToolButton is not null && desiredToolButton.IsVisibleInTree() && desiredToolButton.IsEnabled)
        {
            await WaitForActionWindowAsync(Config.ScreenActionDelayMs, cancellationToken);
            await UiHelper.Click(desiredToolButton);
        }

        var selectedCell = hiddenCells.FirstOrDefault(cell => cell.Entity.X == decision.X && cell.Entity.Y == decision.Y) ?? hiddenCells[0];
        await WaitForActionWindowAsync(Config.ScreenActionDelayMs, cancellationToken);
        Log.Info($"[AiBot] Crystal Sphere decision: {decision.Reason}");
        selectedCell.EmitSignal(NClickableControl.SignalName.Released, selectedCell);
        await WaitForActionQueueToDrainAsync(cancellationToken);
        ApplyActionCooldown(Config.ScreenActionDelayMs);
        return true;
    }

    private async Task<bool> HandleRewardsScreenAsync(NRewardsScreen screen, CancellationToken cancellationToken)
    {
        if (DecisionEngine is null)
        {
            return false;
        }

        var player = LocalContext.GetMe(RunManager.Instance.DebugOnlyGetState());
        var allButtons = UiHelper.FindAll<NRewardButton>(screen).ToList();
        var skippedButtons = GetSkippedRewardButtons(screen);
        var pendingButtons = allButtons
            .Where(button => !skippedButtons.Contains(button))
            .ToList();
        var buttons = pendingButtons
            .Where(button => button.IsEnabled)
            .Where(button => button.Visible && button.IsVisibleInTree())
            .ToList();
        if (buttons.Count > 0)
        {
            var decision = await DecisionEngine.ChooseRewardAsync(buttons, player?.HasOpenPotionSlots ?? false, GetCurrentAnalysis(), cancellationToken);
            if (decision.Button is not null)
            {
                await WaitForActionWindowAsync(Config.ScreenActionDelayMs, cancellationToken);
                Log.Info($"[AiBot] Rewards decision: {decision.Reason}");
                await UiHelper.Click(decision.Button);
                await WaitForActionQueueToDrainAsync(cancellationToken);
                ApplyActionCooldown(Config.ScreenActionDelayMs);
                return true;
            }
        }

        if (pendingButtons.Count > 0)
        {
            LogThrottled("overlay-rewards-waiting", $"[AiBot] Rewards waiting: {pendingButtons.Count} reward button(s) still exist but are not interactable yet.", TimeSpan.FromSeconds(2));
            return false;
        }

        var proceedButton = UiHelper.FindFirst<NProceedButton>(screen);
        if (proceedButton is not null && proceedButton.IsEnabled)
        {
            await WaitForActionWindowAsync(Config.ScreenActionDelayMs, cancellationToken);
            await UiHelper.Click(proceedButton);
            await WaitForActionQueueToDrainAsync(cancellationToken);
            ApplyActionCooldown(Config.ScreenActionDelayMs);
            return true;
        }

        LogThrottled("overlay-rewards-idle", "[AiBot] Overlay idle: rewards screen has no enabled rewards or proceed button.", TimeSpan.FromSeconds(2));
        return false;
    }

    private async Task<bool> TryHandleCombatAsync(RunState runState, CancellationToken cancellationToken)
    {
        if (DecisionEngine is null || !CombatManager.Instance.IsInProgress || !CombatManager.Instance.IsPlayPhase)
        {
            return false;
        }

        var player = LocalContext.GetMe(runState);
        if (!CombatActionGuard.CanTakeLocalTurnActions(player))
        {
            return false;
        }

        var localPlayer = player!;
        var hand = PileType.Hand.GetPile(localPlayer).Cards.ToList();
        var playable = hand.Where(CanAutoPlayCard).ToList();
        var enemies = localPlayer.Creature.CombatState?.HittableEnemies?.Where(enemy => enemy.IsAlive).ToList() ?? new List<Creature>();

        var usablePotions = localPlayer.Potions
            .Where(IsPotionUsableInCombat)
            .ToList();

        if (usablePotions.Count > 0)
        {
            var potionDecision = await DecisionEngine.ChoosePotionUseAsync(localPlayer, usablePotions, playable, enemies, GetCurrentAnalysis(), cancellationToken);
            if (potionDecision.Potion is not null)
            {
                var target = potionDecision.Target ?? ChoosePotionTarget(potionDecision.Potion, localPlayer, enemies);
                if (!RequiresPotionTarget(potionDecision.Potion) || target is not null)
                {
                    await WaitForActionWindowAsync(Config.CombatActionDelayMs, cancellationToken);
                    Log.Info($"[AiBot] Consumable decision: {potionDecision.Reason}");
                    potionDecision.Potion.EnqueueManualUse(target);
                    await WaitForActionQueueToDrainAsync(cancellationToken);
                    ApplyActionCooldown(Config.CombatActionDelayMs);
                    return true;
                }
            }
        }

        var decision = await DecisionEngine.ChooseCombatActionAsync(localPlayer, playable, enemies, GetCurrentAnalysis(), cancellationToken);

        if (decision.EndTurn || decision.Card is null)
        {
            await WaitForActionWindowAsync(Config.CombatActionDelayMs, cancellationToken);
            Log.Info($"[AiBot] Combat decision: ending turn. {decision.Reason}");
            if (!CombatActionGuard.QueueEndTurn(localPlayer))
            {
                Log.Warn("[AiBot] Combat decision wanted to end turn, but the local player could not enqueue an end-turn action.");
                return false;
            }
            await WaitForActionQueueToDrainAsync(cancellationToken);
            ApplyActionCooldown(Config.CombatActionDelayMs);
            return true;
        }

        await WaitForActionWindowAsync(Config.CombatActionDelayMs, cancellationToken);
        Log.Info($"[AiBot] Combat decision: {decision.Reason}");
        var combatTarget = decision.Target ?? ChooseCombatTarget(decision.Card, localPlayer, enemies);
        if (!decision.Card.TryManualPlay(combatTarget))
        {
            var targetText = combatTarget is null ? "null" : $"{combatTarget.Name}#{combatTarget.CombatId}";
            Log.Warn($"[AiBot] Manual play failed for {decision.Card.Title}. targetType={decision.Card.TargetType}, target={targetText}. Ending turn instead.");
            CombatActionGuard.QueueEndTurn(localPlayer);
        }

        await WaitForActionQueueToDrainAsync(cancellationToken);
        ApplyActionCooldown(Config.CombatActionDelayMs);

        return true;
    }

    private async Task<bool> TryHandleRoomAsync(RunState runState, CancellationToken cancellationToken)
    {
        var currentRoom = runState.CurrentRoom;
        if (currentRoom is null)
        {
            return false;
        }

        var roomKey = $"{runState.CurrentActIndex}:{runState.ActFloor}:{currentRoom.RoomType}";
        switch (currentRoom.RoomType)
        {
            case RoomType.Monster:
            case RoomType.Elite:
            case RoomType.Boss:
                return await HandleCombatRoomAsync(roomKey, cancellationToken);
            case RoomType.Shop:
                return await HandleShopAsync(roomKey, cancellationToken);
            case RoomType.RestSite:
                return await HandleRestSiteAsync(roomKey, runState, cancellationToken);
            case RoomType.Treasure:
                return await HandleTreasureAsync(roomKey, cancellationToken);
            case RoomType.Event:
                return await HandleEventAsync(roomKey, cancellationToken);
            default:
                return false;
        }
    }

    private async Task<bool> HandleCombatRoomAsync(string roomKey, CancellationToken cancellationToken)
    {
        if (CombatManager.Instance.IsInProgress)
        {
            return false;
        }

        var room = GetAbsoluteNodeOrNull<NCombatRoom>("/root/Game/RootSceneContainer/Run/RoomContainer/CombatRoom");
        if (room?.ProceedButton is null || !room.ProceedButton.IsEnabled || !room.IsVisibleInTree())
        {
            return false;
        }

        await WaitForActionWindowAsync(Config.ScreenActionDelayMs, cancellationToken);
        Log.Info($"[AiBot] Leaving combat room: {roomKey}");
        await UiHelper.Click(room.ProceedButton);
        await WaitForActionQueueToDrainAsync(cancellationToken);
        ApplyActionCooldown(Config.ScreenActionDelayMs);
        _processedRoomKeys.TryAdd(roomKey, 0);
        return true;
    }

    private async Task<bool> HandleShopAsync(string roomKey, CancellationToken cancellationToken)
    {
        if (DecisionEngine is null)
        {
            return false;
        }

        var room = GetAbsoluteNodeOrNull<NMerchantRoom>("/root/Game/RootSceneContainer/Run/RoomContainer/MerchantRoom");
        if (room is null || room.Inventory is null)
        {
            return false;
        }

        var runState = RunManager.Instance.DebugOnlyGetState();
        var player = LocalContext.GetMe(runState);
        if (player is null)
        {
            return false;
        }

        if (!room.Inventory.IsOpen)
        {
            if (_processedRoomKeys.ContainsKey(roomKey))
            {
                if (room.ProceedButton is not null && room.ProceedButton.IsEnabled)
                {
                    await WaitForActionWindowAsync(Config.ScreenActionDelayMs, cancellationToken);
                    Log.Info("[AiBot] Leaving shop after finishing purchases.");
                    await UiHelper.Click(room.ProceedButton);
                    await WaitForActionQueueToDrainAsync(cancellationToken);
                    ApplyActionCooldown(Config.ScreenActionDelayMs);
                    return true;
                }

                return false;
            }

            if (room.MerchantButton is not null && room.MerchantButton.IsEnabled)
            {
                await WaitForActionWindowAsync(Config.ScreenActionDelayMs, cancellationToken);
                room.OpenInventory();
                ApplyActionCooldown(Config.ScreenActionDelayMs);
                return true;
            }

            if (room.ProceedButton is not null && room.ProceedButton.IsEnabled)
            {
                await WaitForActionWindowAsync(Config.ScreenActionDelayMs, cancellationToken);
                Log.Info("[AiBot] Leaving shop.");
                await UiHelper.Click(room.ProceedButton);
                await WaitForActionQueueToDrainAsync(cancellationToken);
                ApplyActionCooldown(Config.ScreenActionDelayMs);
                return true;
            }

            return false;
        }

        var inventory = room.Inventory.Inventory;
        if (inventory is null)
        {
            return false;
        }

        var options = inventory.AllEntries
            .Where(entry => entry.IsStocked && entry.EnoughGold)
            .Where(entry => player.HasOpenPotionSlots || entry is not MerchantPotionEntry)
            .ToList();

        var decision = await DecisionEngine.ChooseShopPurchaseAsync(options, player.Gold, player.HasOpenPotionSlots, GetCurrentAnalysis(), cancellationToken);
        if (decision.Entry is not null)
        {
            await WaitForActionWindowAsync(Config.ScreenActionDelayMs, cancellationToken);
            Log.Info($"[AiBot] Shop decision: {decision.Reason}");
            var purchased = await TryPurchaseMerchantEntryAsync(decision.Entry, inventory);
            await WaitForActionQueueToDrainAsync(cancellationToken);
            ApplyActionCooldown(Config.ScreenActionDelayMs);
            return purchased;
        }

        var backButton = UiHelper.FindAll<NBackButton>(room.Inventory).FirstOrDefault(button => button.IsVisibleInTree() && button.IsEnabled);
        if (backButton is not null)
        {
            await WaitForActionWindowAsync(Config.ScreenActionDelayMs, cancellationToken);
            Log.Info($"[AiBot] Shop decision: {decision.Reason}");
            _processedRoomKeys.TryAdd(roomKey, 0);
            await UiHelper.Click(backButton);
            ApplyActionCooldown(Config.ScreenActionDelayMs);
            return true;
        }

        return false;
    }

    private async Task<bool> HandleRestSiteAsync(string roomKey, RunState runState, CancellationToken cancellationToken)
    {
        var room = GetAbsoluteNodeOrNull<NRestSiteRoom>("/root/Game/RootSceneContainer/Run/RoomContainer/RestSiteRoom");
        if (room is null)
        {
            return false;
        }

        if (!_processedRoomKeys.ContainsKey(roomKey))
        {
            var buttons = UiHelper.FindAll<NRestSiteButton>(room)
                .Where(button => button.IsEnabled && button.Visible)
                .ToList();
            if (buttons.Count == 0)
            {
                return false;
            }

            var player = LocalContext.GetMe(runState);
            var options = buttons.Select(button => button.Option).ToList();
            var decision = player is null || DecisionEngine is null
                ? new RestDecision(options.FirstOrDefault(), "Fallback rest-site choice.")
                : await DecisionEngine.ChooseRestSiteOptionAsync(player, options, GetCurrentAnalysis(), cancellationToken);
            var chosen = decision.Option is not null
                ? buttons.FirstOrDefault(button => button.Option.OptionId == decision.Option.OptionId) ?? buttons[0]
                : buttons[0];
            _processedRoomKeys.TryAdd(roomKey, 0);
            await WaitForActionWindowAsync(Config.ScreenActionDelayMs, cancellationToken);
            Log.Info($"[AiBot] Rest site option: {chosen.Option.GetType().Name}. {decision.Reason}");
            await UiHelper.Click(chosen);
            await WaitForActionQueueToDrainAsync(cancellationToken);
            ApplyActionCooldown(Config.ScreenActionDelayMs);
            return true;
        }

        if (room.ProceedButton.IsEnabled)
        {
            await WaitForActionWindowAsync(Config.ScreenActionDelayMs, cancellationToken);
            await UiHelper.Click(room.ProceedButton);
            await WaitForActionQueueToDrainAsync(cancellationToken);
            ApplyActionCooldown(Config.ScreenActionDelayMs);
            return true;
        }

        return false;
    }

    private async Task<bool> HandleTreasureAsync(string roomKey, CancellationToken cancellationToken)
    {
        var room = GetAbsoluteNodeOrNull<NTreasureRoom>("/root/Game/RootSceneContainer/Run/RoomContainer/TreasureRoom");
        if (room is null)
        {
            return false;
        }

        if (!_processedRoomKeys.ContainsKey(roomKey))
        {
            var chest = room.GetNodeOrNull<NClickableControl>("Chest");
            if (chest is not null)
            {
                _processedRoomKeys.TryAdd(roomKey, 0);
                await WaitForActionWindowAsync(Config.ScreenActionDelayMs, cancellationToken);
                Log.Info("[AiBot] Opening treasure chest.");
                await UiHelper.Click(chest);
                await WaitForActionQueueToDrainAsync(cancellationToken);
                ApplyActionCooldown(Config.ScreenActionDelayMs);
                return true;
            }
        }

        var relicHolders = UiHelper.FindAll<NTreasureRoomRelicHolder>(room)
            .Where(holder => holder.IsEnabled && holder.Visible)
            .ToList();
        if (relicHolders.Count > 0)
        {
            var relicModels = relicHolders.Select(holder => holder.Relic.Model).ToList();
            var decision = DecisionEngine is null
                ? new RelicChoiceDecision(relicModels.FirstOrDefault(), false, "Fallback treasure relic choice.")
                : await DecisionEngine.ChooseRelicAsync(relicModels, "Treasure Room", false, GetCurrentAnalysis(), cancellationToken);
            var chosenHolder = decision.Relic is not null
                ? relicHolders.FirstOrDefault(holder => holder.Relic.Model == decision.Relic || holder.Relic.Model.Id == decision.Relic.Id) ?? relicHolders[0]
                : relicHolders[0];
            await WaitForActionWindowAsync(Config.ScreenActionDelayMs, cancellationToken);
            Log.Info($"[AiBot] Treasure relic decision: {decision.Reason}");
            await UiHelper.Click(chosenHolder);
            await WaitForActionQueueToDrainAsync(cancellationToken);
            ApplyActionCooldown(Config.ScreenActionDelayMs);
            return true;
        }

        if (room.ProceedButton.IsEnabled)
        {
            await WaitForActionWindowAsync(Config.ScreenActionDelayMs, cancellationToken);
            await UiHelper.Click(room.ProceedButton);
            await WaitForActionQueueToDrainAsync(cancellationToken);
            ApplyActionCooldown(Config.ScreenActionDelayMs);
            return true;
        }

        return false;
    }

    private async Task<bool> HandleEventAsync(string roomKey, CancellationToken cancellationToken)
    {
        if (NMapScreen.Instance is { IsOpen: true })
        {
            return false;
        }

        var eventRoom = GetAbsoluteNodeOrNull<NEventRoom>("/root/Game/RootSceneContainer/Run/RoomContainer/EventRoom");
        if (eventRoom is null || !eventRoom.Visible || !eventRoom.IsVisibleInTree())
        {
            return false;
        }

        var options = UiHelper.FindAll<NEventOptionButton>(eventRoom)
            .Where(button => button.IsEnabled && !button.Option.IsLocked)
            .ToList();
        if (options.Count > 0)
        {
            var eventModel = options[0].Event;
            var decision = DecisionEngine is null
                ? new EventDecision(options.FirstOrDefault(button => !button.Option.IsProceed)?.Option ?? options[0].Option, "Fallback event choice.")
                : await DecisionEngine.ChooseEventOptionAsync(eventModel, options.Select(button => button.Option).ToList(), GetCurrentAnalysis(), cancellationToken);
            var preferred = decision.Option is not null
                ? options.FirstOrDefault(button => button.Option == decision.Option || button.Option.TextKey == decision.Option.TextKey) ?? options[0]
                : options[0];
            await WaitForActionWindowAsync(Config.ScreenActionDelayMs, cancellationToken);
            Log.Info($"[AiBot] Event decision: {decision.Reason}");
            await UiHelper.Click(preferred);
            await WaitForActionQueueToDrainAsync(cancellationToken);
            ApplyActionCooldown(Config.ScreenActionDelayMs);
            return true;
        }

        if (_processedRoomKeys.TryAdd(roomKey, 0))
        {
            var proceed = UiHelper.FindFirst<NProceedButton>(eventRoom);
            if (proceed is not null && proceed.IsEnabled)
            {
                await WaitForActionWindowAsync(Config.ScreenActionDelayMs, cancellationToken);
                await UiHelper.Click(proceed);
                await WaitForActionQueueToDrainAsync(cancellationToken);
                ApplyActionCooldown(Config.ScreenActionDelayMs);
                return true;
            }
        }

        return false;
    }

    private async Task<bool> TryHandleMapAsync(RunState runState, CancellationToken cancellationToken)
    {
        if (DecisionEngine is null)
        {
            LogThrottled("map-skip-engine", "[AiBot] Map skipped: DecisionEngine is null.", TimeSpan.FromSeconds(2));
            return false;
        }

        if (NMapScreen.Instance is null)
        {
            LogThrottled("map-skip-instance", "[AiBot] Map skipped: NMapScreen.Instance is null.", TimeSpan.FromSeconds(2));
            return false;
        }

        if (!NMapScreen.Instance.IsVisibleInTree())
        {
            LogThrottled("map-skip-hidden", $"[AiBot] Map skipped: map not visible. room={runState.CurrentRoom?.RoomType}", TimeSpan.FromSeconds(2));
            return false;
        }

        if (NMapScreen.Instance.IsTraveling)
        {
            LogThrottled("map-skip-traveling", "[AiBot] Map skipped: map is traveling.", TimeSpan.FromSeconds(2));
            return false;
        }

        var player = LocalContext.GetMe(runState);
        if (player is null)
        {
            LogThrottled("map-skip-player", "[AiBot] Map skipped: local player is null.", TimeSpan.FromSeconds(2));
            return false;
        }

        var allMapPoints = UiHelper.FindAll<NMapPoint>(NMapScreen.Instance).ToList();
        if (allMapPoints.Count == 0)
        {
            LogThrottled("map-skip-nodes", "[AiBot] Map skipped: no NMapPoint nodes found.", TimeSpan.FromSeconds(2));
            return false;
        }

        var pointLookup = allMapPoints.ToDictionary(point => point.Point.coord, point => point);
        var travelableNodes = allMapPoints
            .Where(node => node.State == MapPointState.Travelable)
            .OrderBy(node => node.Point.coord.row)
            .ThenBy(node => node.Point.coord.col)
            .ToList();

        var candidateNodes = ResolveCandidateMapNodes(runState, allMapPoints, pointLookup)
            .Where(node => node.State == MapPointState.Travelable)
            .OrderBy(node => node.Point.coord.col)
            .ToList();

        if (candidateNodes.Count == 0)
        {
            candidateNodes = travelableNodes;
        }

        if (candidateNodes.Count == 0)
        {
            if (Config.Logging.Verbose)
            {
                Log.Info($"[AiBot] Map skipped: no travelable candidates. Visited={FormatMapCoords(runState.VisitedMapCoords)}, TotalNodes={allMapPoints.Count}");
            }

            return false;
        }

        var candidatePoints = candidateNodes.Select(node => node.Point).ToList();
        var decision = await DecisionEngine.ChooseMapPointAsync(candidatePoints, player.Creature.CurrentHp, player.Creature.MaxHp, player.Gold, GetCurrentAnalysis(), cancellationToken);
        var node = decision.Point is not null && pointLookup.TryGetValue(decision.Point.coord, out var selectedNode)
            ? selectedNode
            : candidateNodes[0];

        if (Config.Logging.Verbose)
        {
            Log.Info($"[AiBot] Map state: visited={FormatMapCoords(runState.VisitedMapCoords)}, travelable={FormatMapCoords(travelableNodes.Select(node => node.Point.coord))}, candidates={FormatMapCoords(candidateNodes.Select(node => node.Point.coord))}, selected={node.Point.coord}");
        }

        var enabled = await WaitUntilAsync(() => node.IsEnabled, TimeSpan.FromSeconds(3), cancellationToken);
        if (!enabled)
        {
            Log.Warn($"[AiBot] Map node not enabled in time: {node.Point.coord}");
            return false;
        }

        await WaitForMapActionWindowAsync(cancellationToken);
        Log.Info($"[AiBot] Map decision: {decision.Reason}");
        var roomEnteredTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnRoomEntered() => roomEnteredTcs.TrySetResult();
        RunManager.Instance.RoomEntered += OnRoomEntered;
        try
        {
            await UiHelper.Click(node);
            try
            {
                await roomEnteredTcs.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            }
            catch (TimeoutException)
            {
                Log.Warn($"[AiBot] Map selection timed out waiting for room entry from {node.Point.coord}");
            }
        }
        finally
        {
            RunManager.Instance.RoomEntered -= OnRoomEntered;
        }

        ApplyActionCooldown(Config.MapActionDelayMs);
        return true;
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

    private static bool CanAutoPlayCard(CardModel card)
    {
        AbstractModel? preventer;
        UnplayableReason reason;
        return card.CanPlay(out reason, out preventer);
    }

    private static async Task WaitForRuntimeReadyAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            if (NRun.Instance is not null && RunManager.Instance.DebugOnlyGetState() is not null)
            {
                await Task.Delay(250);
                return;
            }

            await Task.Delay(250);
        }
    }

    private async Task WaitForActionWindowAsync(int delayMs, CancellationToken cancellationToken)
    {
        await WaitForActionQueueToDrainAsync(cancellationToken);

        var remaining = _nextActionAtUtc - DateTime.UtcNow;
        if (remaining > TimeSpan.Zero)
        {
            await Task.Delay(remaining, cancellationToken);
        }

        if (delayMs > 0)
        {
            await Task.Delay(delayMs, cancellationToken);
        }
    }

    private async Task WaitForMapActionWindowAsync(CancellationToken cancellationToken)
    {
        var remaining = _nextActionAtUtc - DateTime.UtcNow;
        if (remaining > TimeSpan.Zero)
        {
            await Task.Delay(remaining, cancellationToken);
        }

        if (Config.MapActionDelayMs > 0)
        {
            await Task.Delay(Config.MapActionDelayMs, cancellationToken);
        }
    }

    private static bool IsPotionUsableInCombat(PotionModel potion)
    {
        return potion is not null
            && !potion.IsQueued
            && !potion.HasBeenRemovedFromState
            && potion.PassesCustomUsabilityCheck
            && potion.Usage is PotionUsage.CombatOnly or PotionUsage.AnyTime;
    }

    private static bool RequiresPotionTarget(PotionModel potion)
    {
        return potion.TargetType is TargetType.AnyEnemy or TargetType.AnyAlly or TargetType.AnyPlayer or TargetType.Self or TargetType.TargetedNoCreature;
    }

    private static Creature? ChooseCombatTarget(CardModel card, Player player, IReadOnlyList<Creature> enemies)
    {
        return card.TargetType switch
        {
            TargetType.AnyEnemy => enemies.Where(enemy => enemy.IsAlive).OrderBy(enemy => enemy.CurrentHp).ThenBy(enemy => enemy.Block).FirstOrDefault(),
            TargetType.AnyAlly => GetCombatAllies(player).OrderBy(ally => ally.CurrentHp / (float)Math.Max(1, ally.MaxHp)).ThenBy(ally => ally.CurrentHp).FirstOrDefault(),
            _ => null
        };
    }

    private static string? GetRewardAlternativeLabel(NCardRewardAlternativeButton button)
    {
        var label = button.GetNodeOrNull<MegaLabel>("Label");
        return label?.Text?.Trim();
    }

    private static HashSet<NRewardButton> GetSkippedRewardButtons(NRewardsScreen screen)
    {
        var field = typeof(NRewardsScreen).GetField("_skippedRewardButtons", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (field?.GetValue(screen) is IEnumerable<Control> controls)
        {
            return controls.OfType<NRewardButton>().ToHashSet();
        }

        return new HashSet<NRewardButton>();
    }

    private static Creature? ChoosePotionTarget(PotionModel potion, Player player, IReadOnlyList<Creature> enemies)
    {
        return potion.TargetType switch
        {
            TargetType.AnyEnemy => enemies.Where(enemy => enemy.IsAlive).OrderBy(enemy => enemy.CurrentHp).FirstOrDefault(),
            TargetType.AnyAlly => GetCombatAllies(player).OrderBy(ally => ally.CurrentHp / (float)Math.Max(1, ally.MaxHp)).ThenBy(ally => ally.CurrentHp).FirstOrDefault(),
            TargetType.AnyPlayer or TargetType.Self => player.Creature,
            _ => null
        };
    }

    private static IEnumerable<Creature> GetCombatAllies(Player player)
    {
        yield return player.Creature;

        if (player.PlayerCombatState is null)
        {
            yield break;
        }

        foreach (var pet in player.PlayerCombatState.Pets.Where(pet => pet.IsAlive))
        {
            yield return pet;
        }
    }

    private static async Task<bool> TryPurchaseMerchantEntryAsync(MerchantEntry entry, MerchantInventory inventory)
    {
        return entry switch
        {
            MerchantCardRemovalEntry removalEntry => await removalEntry.OnTryPurchaseWrapper(inventory, false, false),
            _ => await entry.OnTryPurchaseWrapper(inventory)
        };
    }

    private static async Task WaitForActionQueueToDrainAsync(CancellationToken cancellationToken)
    {
        var actionExecutor = RunManager.Instance.ActionExecutor;
        if (actionExecutor is not null)
        {
            await actionExecutor.FinishedExecutingActions().WaitAsync(cancellationToken);
        }
    }

    private void ApplyActionCooldown(int delayMs)
    {
        _nextActionAtUtc = DateTime.UtcNow.AddMilliseconds(Math.Max(0, delayMs));
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (condition())
            {
                return true;
            }

            await Task.Delay(100, cancellationToken);
        }

        return condition();
    }

    private static string FormatMapCoords(IEnumerable<MapCoord> coords)
    {
        return string.Join(", ", coords.Select(coord => $"({coord.row},{coord.col})"));
    }

    private void LogThrottled(string key, string message, TimeSpan interval)
    {
        var now = DateTime.UtcNow;
        if (_logThrottle.TryGetValue(key, out var last) && now - last < interval)
        {
            return;
        }

        _logThrottle[key] = now;
        Log.Info(message);
    }

    private static T? GetAbsoluteNodeOrNull<T>(string path) where T : class
    {
        var root = ((SceneTree)Engine.GetMainLoop()).Root;
        return root.GetNodeOrNull(path) as T;
    }

    private void TryInstallSelector()
    {
        DisposeSelector();
        if (DecisionEngine is null)
        {
            return;
        }

        try
        {
            _selectorScope = CardSelectCmd.UseSelector(new AiBotCardSelector(GetCurrentAnalysis, DecisionEngine));
        }
        catch (Exception ex)
        {
            Log.Warn($"[AiBot] Could not install card selector: {ex.Message}");
        }
    }

    private void DisposeSelector()
    {
        _selectorScope?.Dispose();
        _selectorScope = null;
    }

    private void EnsureDecisionPanel()
    {
        if (!Config.ShowDecisionPanel)
        {
            UpdateDecisionPanelVisibility();
            return;
        }

        var root = ((SceneTree)Engine.GetMainLoop()).Root;
        if (_decisionPanel is null || !GodotObject.IsInstanceValid(_decisionPanel) || _decisionPanel.GetParent() is null)
        {
            _decisionPanel = new AiBotDecisionPanel(Config.DecisionPanelMaxEntries)
            {
                Name = "AiBotDecisionPanel"
            };
            root.AddChild(_decisionPanel);
        }

        _decisionPanel.Visible = true;
        _decisionPanel.SetMaxEntries(Config.DecisionPanelMaxEntries);
    }

    private void UpdateDecisionPanelVisibility()
    {
        if (_decisionPanel is not null && GodotObject.IsInstanceValid(_decisionPanel))
        {
            _decisionPanel.Visible = Config.ShowDecisionPanel && IsInitialized;
        }
    }

    public void Dispose()
    {
        Deactivate();
        if (DecisionEngine is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    public void SetUiLanguage(AiBotLanguage language)
    {
        if (Config.Ui.GetLanguage() == language)
        {
            return;
        }

        Config.Ui.SetLanguage(language);
        PersistConfig();
        UiLanguageChanged?.Invoke(language);
    }

    private void PersistConfig()
    {
        if (string.IsNullOrWhiteSpace(_modDirectory))
        {
            return;
        }

        try
        {
            AiBotConfigLoader.Save(Path.Combine(_modDirectory, "config.json"), Config);
        }
        catch (Exception ex)
        {
            Log.Warn($"[AiBot] Failed to persist config: {ex.Message}");
        }
    }
}
