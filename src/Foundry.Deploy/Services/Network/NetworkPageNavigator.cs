// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using Foundry.Core.Services.Runtime;
using Foundry.Deploy.Services.Security;
using Foundry.Wpf.Localization;
using Foundry.Wpf.Theme;
using Microsoft.Extensions.Logging;

namespace Foundry.Deploy.Services.Network;

/// <summary>
/// Asks the host to show the Foundry Connect page through <see cref="DeployPhaseOptions.OpenNetworkPage"/>, then shows Deploy's
/// content again, applies the theme and language chosen on the page and runs the <see cref="INetworkReturnRefresh"/>.
/// </summary>
/// <remarks>
/// Owned by the Deploy phase: the end of the phase (<see cref="DeployPhaseLifetime"/>) cancels a page in flight, and the phase
/// awaits <see cref="CancelAndWaitAsync"/> before it releases its content and ends its log scope. Used on the UI thread; the
/// host's callback and the refresh resume on it.
/// </remarks>
internal sealed class NetworkPageNavigator : INetworkPageNavigator
{
    private readonly Func<NetworkPageRequest, CancellationToken, Task<NetworkPageResult>>? _openNetworkPage;
    private readonly IDeploymentSecretKeySession _keySession;
    private readonly IThemeService _themeService;
    private readonly ILocalizationService _localizationService;
    private readonly Action _showDeployContent;
    private readonly Action<string?, string?> _applyPreferences;
    private readonly INetworkReturnRefresh _returnRefresh;
    private readonly ILogger<NetworkPageNavigator> _logger;
    private readonly CancellationTokenSource _phaseCancellation = new();
    private Task<bool>? _inFlight;
    private bool _isOpen;

    /// <param name="openNetworkPage">The host callback, or <see langword="null"/> when the host cannot show the page.</param>
    /// <param name="keySession">Source of the key copy passed to the page while Deploy is unlocked.</param>
    /// <param name="themeService">Source of the theme passed to the page.</param>
    /// <param name="localizationService">Source of the culture passed to the page.</param>
    /// <param name="showDeployContent">Shows Deploy's content in the shell again; it must not initialize it a second time.</param>
    /// <param name="applyPreferences">Applies the returned theme and culture name through Deploy's preference path.</param>
    /// <param name="returnRefresh">Runs after the preferences are applied.</param>
    /// <param name="lifetime">The phase lifetime; its end cancels a page in flight.</param>
    /// <param name="logger">Records the start and the outcome of each reopen.</param>
    public NetworkPageNavigator(
        Func<NetworkPageRequest, CancellationToken, Task<NetworkPageResult>>? openNetworkPage,
        IDeploymentSecretKeySession keySession,
        IThemeService themeService,
        ILocalizationService localizationService,
        Action showDeployContent,
        Action<string?, string?> applyPreferences,
        INetworkReturnRefresh returnRefresh,
        DeployPhaseLifetime lifetime,
        ILogger<NetworkPageNavigator> logger)
    {
        ArgumentNullException.ThrowIfNull(lifetime);
        _openNetworkPage = openNetworkPage;
        _keySession = keySession ?? throw new ArgumentNullException(nameof(keySession));
        _themeService = themeService ?? throw new ArgumentNullException(nameof(themeService));
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
        _showDeployContent = showDeployContent ?? throw new ArgumentNullException(nameof(showDeployContent));
        _applyPreferences = applyPreferences ?? throw new ArgumentNullException(nameof(applyPreferences));
        _returnRefresh = returnRefresh ?? throw new ArgumentNullException(nameof(returnRefresh));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _ = lifetime.Completion.ContinueWith(
            static (_, state) => ((NetworkPageNavigator)state!).CancelPhase(),
            this,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    /// <inheritdoc />
    public bool IsAvailable => _openNetworkPage is not null;

    /// <inheritdoc />
    public bool IsOpen => _isOpen;

    /// <inheritdoc />
    public Task<bool> OpenAsync(CancellationToken cancellationToken)
    {
        if (_openNetworkPage is null || _isOpen || _phaseCancellation.IsCancellationRequested || cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(false);
        }

        // Set before the run starts: a page that completes synchronously clears it again before the task is returned.
        _isOpen = true;
        Task<bool> run = RunAsync(_openNetworkPage, cancellationToken);
        _inFlight = run;
        return run;
    }

    /// <inheritdoc />
    public async Task CancelAndWaitAsync()
    {
        CancelPhase();
        if (_inFlight is { } inFlight)
        {
            // RunAsync reports its own failures and never throws.
            await inFlight;
        }
    }

    private async Task<bool> RunAsync(
        Func<NetworkPageRequest, CancellationToken, Task<NetworkPageResult>> openNetworkPage,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(_phaseCancellation.Token, cancellationToken);
        CancellationToken token = linked.Token;
        try
        {
            NetworkPageRequest request = CreateRequest();
            _logger.LogInformation("Network page opened from Foundry Deploy. HasDeploymentKey={HasDeploymentKey}", request.DeploymentKey is not null);
            NetworkPageResult result;
            try
            {
                // The host owns the request's key from here on, whatever the callback does.
                result = await openNetworkPage(request, token);
            }
            finally
            {
                _showDeployContent();
            }

            // A page that returns after the phase ended changes nothing in a Deploy that is closing.
            token.ThrowIfCancellationRequested();
            _applyPreferences(result.ThemeMode, result.CultureName);
            await _returnRefresh.RefreshAsync(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            _logger.LogInformation("Network page closed. Outcome={Outcome}", "Cancelled");
            return false;
        }
        catch (Exception exception)
        {
            // The exception is not attached because every log level is mirrored remotely.
            _logger.LogWarning(
                "Network page failed; Foundry Deploy is shown again. FailureType={FailureType}",
                exception.GetType().Name);
            return false;
        }
        finally
        {
            _isOpen = false;
        }

        _logger.LogInformation("Network page closed. Outcome={Outcome}", "Returned");
        return true;
    }

    // The preferences are read before the key is copied, so a failure never leaves an undisposed key copy behind.
    private NetworkPageRequest CreateRequest()
    {
        string themeMode = _themeService.CurrentTheme.ToString();
        string cultureName = _localizationService.CurrentCulture.Name;
        DeploymentKeyTransfer? deploymentKey = null;
        if (_keySession.IsUnlocked)
        {
            byte[] copy = _keySession.GetKeyCopy();
            try
            {
                deploymentKey = DeploymentKeyTransfer.FromKey(copy);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(copy);
            }
        }

        return new NetworkPageRequest(deploymentKey, themeMode, cultureName);
    }

    private void CancelPhase()
    {
        try
        {
            _phaseCancellation.Cancel();
        }
        catch (AggregateException)
        {
            // A callback the host registered on the token failed; the host reports its own failures, and ending the phase
            // must not fail because of it.
        }
    }
}
