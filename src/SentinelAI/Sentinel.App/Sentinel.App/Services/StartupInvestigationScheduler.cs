/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Schedules expensive investigation work after the UI has had time to render.
    /// This keeps startup responsive while allowing deeper evidence collection to
    /// continue in the background.
    /// </summary>
    public sealed class StartupInvestigationScheduler
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        private CancellationTokenSource? _cancellation;

        public bool IsRunning { get; private set; }

        public async Task RunDeferredAsync(
            Func<CancellationToken, Task> investigation,
            TimeSpan delay,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(investigation);

            if (delay < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(delay));
            }

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                _cancellation?.Cancel();
                _cancellation?.Dispose();
                _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            }
            finally
            {
                _gate.Release();
            }

            CancellationToken token = _cancellation.Token;

            try
            {
                IsRunning = true;

                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, token).ConfigureAwait(false);
                }

                await investigation(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // A newer startup investigation or application shutdown superseded this run.
            }
            finally
            {
                IsRunning = false;
            }
        }

        public void Cancel()
        {
            _cancellation?.Cancel();
        }
    }
}
