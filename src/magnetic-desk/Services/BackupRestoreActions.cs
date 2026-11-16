using MagneticDesk.Contracts;
namespace MagneticDesk.Services;
public sealed class BackupRestoreActions
{
    private readonly object _operationGate = new();

    private readonly HashSet<Task> _pending = [];

    private readonly CancellationTokenSource _lifetime = new();


    private readonly MagneticDeskDataBackupService _local;

    private readonly Func<Task> _shutdownForRestart;

    private readonly Func<AppRelaunchScheduleResult> _scheduleRelaunch;

    private Task? _stopTask;

    private bool _stopping;

    private bool _preparedScopedRestore;

    private bool _preservePendingRestore;


    internal BackupRestoreActions(MagneticDeskDataBackupService local, Func<Task> shutdownForRestart,
        Func<AppRelaunchScheduleResult>? scheduleRelaunch = null)
    {
        _local = local;
        _shutdownForRestart = shutdownForRestart;
        _scheduleRelaunch = scheduleRelaunch ?? AppRelaunchService.ScheduleAfterCurrentProcessExit;
    }


    public Task<MagneticDeskRestorePreparation> PrepareScopedRestoreAsync(string archivePath,
        BackupArchiveDomain scope, CancellationToken cancellationToken = default) =>
        TrackAsync(async token =>
        {
            MagneticDeskRestorePreparation preparation =
                await _local.PrepareScopedRestoreAsync(archivePath, scope, token);
            lock (_operationGate) _preparedScopedRestore = true;
            return preparation;
        }, cancellationToken);


    public Task CancelPendingRestoreAsync(CancellationToken cancellationToken = default) =>
        TrackAsync(async token =>
        {
            await _local.CancelPendingRestoreAsync(token);
            lock (_operationGate) _preparedScopedRestore = false;
            return true;
        }, cancellationToken);


    public Task<bool> SetPendingRestoreItemReplaceModeAsync(bool replace,
        CancellationToken cancellationToken = default) =>
        TrackAsync(token => _local.SetPendingRestoreItemReplaceModeAsync(replace, token),
            cancellationToken);


    public AppRelaunchScheduleResult ScheduleRelaunch()
    {
        lock (_operationGate)
        {
            if (_stopping) return AppRelaunchScheduleResult.Failed("MagneticDesk is shutting down.");
            // Mark the handoff before the restart callback enters App shutdown.
            AppRelaunchScheduleResult result = _scheduleRelaunch();
            if (result.Started) _preservePendingRestore = true;
            return result;
        }
    }


    public Task ShutdownForRestartAsync() => _shutdownForRestart();


    


    public Task StopAsync()
    {
        TaskCompletionSource completion;
        Task[] pending;
        lock (_operationGate)
        {
            if (_stopTask is not null) return _stopTask;
            _stopping = true;
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _stopTask = completion.Task;
            pending = _pending.ToArray();
        }
        _ = StopCoreAsync(pending, completion);
        return completion.Task;
    }


    private async Task StopCoreAsync(Task[] pending, TaskCompletionSource completion)
    {
        try
        {
            _lifetime.Cancel();
            try { await Task.WhenAll(pending); } catch { }

            bool cancelPendingRestore;
            lock (_operationGate)
            {
                // A scoped restore prepared here is abandoned on ordinary
                // shutdown; a confirmed relaunch must keep its marker.
                cancelPendingRestore = _preparedScopedRestore && !_preservePendingRestore;
            }
            if (cancelPendingRestore) await _local.CancelPendingRestoreAsync();
            completion.TrySetResult();
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
        }
        finally
        {
            _lifetime.Dispose();
        }
    }


    private Task<T> TrackAsync<T>(Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        TaskCompletionSource<T> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationTokenSource linked;
        lock (_operationGate)
        {
            if (_stopping) return Task.FromCanceled<T>(new CancellationToken(true));
            linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, _lifetime.Token);
            // Register before starting work so StopAsync cannot miss a fast exit.
            _pending.Add(completion.Task);
        }
        _ = ExecuteAsync();
        return completion.Task;

        async Task ExecuteAsync()
        {
            try
            {
                completion.TrySetResult(await action(linked.Token));
            }
            catch (OperationCanceledException ex)
            {
                completion.TrySetCanceled(ex.CancellationToken);
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
            finally
            {
                lock (_operationGate) _pending.Remove(completion.Task);
                linked.Dispose();
            }
        }
    }

}
