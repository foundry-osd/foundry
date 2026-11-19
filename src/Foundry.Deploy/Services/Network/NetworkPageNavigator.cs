// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Runtime.ExceptionServices;
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
/// Owned by the Deploy phase, which calls <see cref="CancelAndWaitAsync"/> on the UI thread as soon as the phase ends, before
/// it releases its content and ends its log scope. Everything runs on the UI thread, the cancellation of the host's token
/// included, so the host's token callbacks may touch WPF; the host's callback and the refresh resume on it.
/// </remarks>
internal sealed class NetworkPageNavigator : INetworkPageNavigator, IDisposable
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
    private Task? _inFlight;
    private bool _isOpen;
    private bool _isDisposed;

    /// <param name="openNetworkPage">The host callback, or <see langword="null"/> when the host cannot show the page.</param>
    /// <param name="keySession">Source of the key copy passed to the page while Deploy is unlocked.</param>
    /// <param name="themeService">Source of the theme passed to the page.</param>
    /// <param name="localizationService">Source of the culture passed to the page.</param>
    /// <param name="showDeployContent">Shows Deploy's content in the shell again; it must not initialize it a second time.</param>
    /// <param name="applyPreferences">Applies the returned theme and culture name through Deploy's preference path.</param>
    /// <param name="returnRefresh">Runs after the preferences are applied.</param>
    /// <param name="logger">Records the start and the outcome of each reopen.</param>
    public NetworkPageNavigator(
        Func<NetworkPageRequest, CancellationToken, Task<NetworkPageResult>>? openNetworkPage,
        IDeploymentSecretKeySession keySession,
        IThemeService themeService,
        ILocalizationService localizationService,
        Action showDeployContent,
        Action<string?, string?> applyPreferences,
        INetworkReturnRefresh returnRefresh,
        ILogger<NetworkPageNavigator> logger)
    {
        _openNetworkPage = openNetworkPage;
        _keySession = keySession ?? throw new ArgumentNullException(nameof(keySession));
        _themeService = themeService ?? throw new ArgumentNullException(nameof(themeService));
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
        _showDeployContent = showDeployContent ?? throw new ArgumentNullException(nameof(showDeployContent));
        _applyPreferences = applyPreferences ?? throw new ArgumentNullException(nameof(applyPreferences));
        _returnRefresh = returnRefresh ?? throw new ArgumentNullException(nameof(returnRefresh));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public bool IsAvailable => _openNetworkPage is not null;

    /// <inheritdoc />
    public bool IsOpen => _isOpen;

    /// <inheritdoc />
    public Task<bool> OpenAsync(CancellationToken cancellationToken)
    {
        if (_openNetworkPage is null || _isOpen || _isDisposed || _phaseCancellation.IsCancellationRequested
            || cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(false);
        }

        // Set before the run starts: a page that completes synchronously clears it again before the task is returned.
        _isOpen = true;
        Task<bool> run = RunAsync(_openNetworkPage, cancellationToken);
        // A page opened again from a dialog of the return refresh runs while the first run still waits for that refresh.
        _inFlight = _inFlight is { IsCompleted: false } previous ? Task.WhenAll(previous, run) : run;
        return run;
    }

    /// <inheritdoc />
    /// <remarks>Call it on the UI thread: the host's token callbacks run on the calling thread.</remarks>
    public async Task CancelAndWaitAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        try
        {
            _phaseCancellation.Cancel();
        }
        catch (AggregateException exception)
        {
            // A callback the host registered on the token failed (nested in the linked token's aggregate); the page may still
            // end on its own, so the wait goes on.
            _logger.LogWarning(
                "Network page cancellation failed. FailureType={FailureType}",
                exception.Flatten().InnerExceptions[0].GetType().Name);
        }

        if (_inFlight is { } inFlight)
        {
            // RunAsync reports its own failures and never throws; a nested reopen is included in the wait.
            await inFlight;
        }
    }

    /// <summary>Releases the cancellation source; later requests are refused. Idempotent.</summary>
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _phaseCancellation.Dispose();
    }

    private async Task<bool> RunAsync(
        Func<NetworkPageRequest, CancellationToken, Task<NetworkPageResult>> openNetworkPage,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(_phaseCancellation.Token, cancellationToken);
        CancellationToken token = linked.Token;
        NetworkPageRequest? request = null;
        bool isHandedOver = false;
        try
        {
            request = CreateRequest();
            _logger.LogInformation("Network page opened from Foundry Deploy. HasDeploymentKey={HasDeploymentKey}", request.DeploymentKey is not null);
            NetworkPageResult? result = null;
            Exception? pageFailure = null;
            try
            {
                // The host owns the request's key from here on, whatever the callback does.
                isHandedOver = true;
                result = await openNetworkPage(request, token);
            }
            catch (Exception exception)
            {
                pageFailure = exception;
            }

            try
            {
                _showDeployContent();
            }
            catch (Exception showFailure) when (pageFailure is not null)
            {
                // The exceptions are not attached because every log level is mirrored remotely.
                _logger.LogWarning(
                    "Network page failed and Foundry Deploy could not be shown again. FailureType={FailureType}, ShowFailureType={ShowFailureType}",
                    pageFailure.GetType().Name,
                    showFailure.GetType().Name);
                return false;
            }

            if (pageFailure is not null)
            {
                ExceptionDispatchInfo.Throw(pageFailure);
            }

            // A page that returns after the phase ended changes nothing in a Deploy that is closing.
            token.ThrowIfCancellationRequested();
            _applyPreferences(result!.ThemeMode, result.CultureName);
            // The page is closed once Deploy is shown again: a failure dialog of the refresh may offer to open it again.
            _isOpen = false;
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
            if (!isHandedOver)
            {
                request?.DeploymentKey?.Dispose();
            }
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
}
