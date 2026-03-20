using MegaCrit.Sts2.Core.Logging;
using aibot.Scripts.Agent.Handlers;
using aibot.Scripts.Core;

namespace aibot.Scripts.Agent;

public sealed class AgentCore
{
    private readonly SemaphoreSlim _modeGate = new(1, 1);
    private readonly Dictionary<AgentMode, Func<string, IAgentModeHandler>> _handlerFactories = new();

    private AiBotRuntime? _runtime;
    private IAgentModeHandler? _currentHandler;

    public static AgentCore Instance { get; } = new();

    public bool IsInitialized { get; private set; }

    public AgentMode CurrentMode { get; private set; } = AgentMode.FullAuto;

    public event Action<AgentModeChangeRequest>? ModeChangeRequested;

    public event Action<AgentMode>? ModeChanged;

    private AgentCore()
    {
    }

    public void Initialize(AiBotRuntime runtime)
    {
        if (IsInitialized)
        {
            return;
        }

        _runtime = runtime;
        CurrentMode = runtime.Config.Agent.GetDefaultMode();
        _handlerFactories[AgentMode.FullAuto] = reason => new FullAutoModeHandler(runtime, reason);
        _handlerFactories[AgentMode.SemiAuto] = reason => new SemiAutoModeHandler(runtime, reason);
        _handlerFactories[AgentMode.Assist] = reason => new AssistModeHandler(runtime, reason);
        _handlerFactories[AgentMode.QnA] = reason => new QnAModeHandler(runtime, reason);
        IsInitialized = true;
        Log.Info($"[AiBot.Agent] Initialized. DefaultMode={CurrentMode}");
    }

    public Task ActivateDefaultModeAsync(string reason)
    {
        var mode = _runtime?.Config.Agent.GetDefaultMode() ?? AgentMode.FullAuto;
        return SwitchModeAsync(mode, reason, force: true);
    }

    public async Task<bool> SwitchModeAsync(AgentMode requestedMode, string reason, bool force = false)
    {
        if (!IsInitialized || _runtime is null)
        {
            return false;
        }

        await _modeGate.WaitAsync();
        try
        {
            var currentMode = _currentHandler?.Mode ?? CurrentMode;
            var requiresConfirmation = _runtime.Config.Agent.ConfirmOnModeSwitch && currentMode != requestedMode;
            if (requiresConfirmation && !force)
            {
                ModeChangeRequested?.Invoke(new AgentModeChangeRequest(currentMode, requestedMode, reason, true));
                Log.Info($"[AiBot.Agent] Mode switch requested: {currentMode} -> {requestedMode}. Reason={reason}");
                return false;
            }

            if (_currentHandler is not null && _currentHandler.Mode == requestedMode)
            {
                return true;
            }

            if (_currentHandler is not null)
            {
                await _currentHandler.OnDeactivateAsync();
                _currentHandler.Dispose();
                _currentHandler = null;
            }

            var handler = _handlerFactories[requestedMode](reason);
            await handler.OnActivateAsync(CancellationToken.None);
            _currentHandler = handler;
            CurrentMode = requestedMode;
            ModeChanged?.Invoke(requestedMode);
            Log.Info($"[AiBot.Agent] Mode activated: {requestedMode}. Reason={reason}");
            return true;
        }
        finally
        {
            _modeGate.Release();
        }
    }

    public async Task DeactivateCurrentModeAsync()
    {
        if (!IsInitialized)
        {
            return;
        }

        await _modeGate.WaitAsync();
        try
        {
            if (_currentHandler is null)
            {
                return;
            }

            await _currentHandler.OnDeactivateAsync();
            _currentHandler.Dispose();
            _currentHandler = null;
            Log.Info("[AiBot.Agent] Current mode deactivated.");
        }
        finally
        {
            _modeGate.Release();
        }
    }

    public async Task<string> SubmitUserInputAsync(string input, CancellationToken cancellationToken = default)
    {
        if (!IsInitialized || _currentHandler is null)
        {
            return "Agent 尚未激活。";
        }

        return await _currentHandler.OnUserInputAsync(input, cancellationToken);
    }
}
