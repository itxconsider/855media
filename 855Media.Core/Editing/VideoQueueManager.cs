using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using _855Media.Core.Utils;

namespace _855Media.Core.Editing;

public class VideoQueueManager : IDisposable
{
    [DllImport("ntdll.dll", EntryPoint = "NtSuspendProcess", SetLastError = true)]
    private static extern int NtSuspendProcess(IntPtr processHandle);

    [DllImport("ntdll.dll", EntryPoint = "NtResumeProcess", SetLastError = true)]
    private static extern int NtResumeProcess(IntPtr processHandle);

    private static void SuspendProcessSafely(Process? process)
    {
        if (process == null)
            return;
        try
        {
            if (!process.HasExited && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                NtSuspendProcess(process.Handle);
            }
        }
        catch
        {
            // Ignore process handle errors if already exiting
        }
    }

    private static void ResumeProcessSafely(Process? process)
    {
        if (process == null)
            return;
        try
        {
            if (!process.HasExited && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                NtResumeProcess(process.Handle);
            }
        }
        catch
        {
            // Ignore process handle errors
        }
    }

    private readonly VideoEditorService _editorService;
    public VideoEditorService EditService => _editorService;
    private readonly Channel<bool> _signalChannel;
    private readonly HashSet<Guid> _activeJobIds = [];
    private readonly List<Task> _workerTasks = [];
    private readonly CancellationTokenSource _managerCts = new();
    private readonly SemaphoreSlim _pauseGate = new(1, 1);
    private readonly object _syncLock = new();

    private int _maxConcurrency = 2;
    private bool _isPaused;
    private bool _isDisposed;
    private bool _batchCompletionFired;

    public ObservableCollection<EditJob> Jobs { get; } = [];

    public int MaxConcurrency
    {
        get => _maxConcurrency;
        set
        {
            if (value < 1)
                value = 1;
            if (value > 4)
                value = 4;
            _maxConcurrency = value;
            AdjustWorkers();
        }
    }

    public bool IsPaused
    {
        get => _isPaused;
        private set => _isPaused = value;
    }

    public bool IsRunning =>
        !_managerCts.IsCancellationRequested
        && Jobs.Any(j => j.Status == EditJobStatus.Processing || j.Status == EditJobStatus.Queued);

    public event EventHandler<EditJob>? JobStarted;
    public event EventHandler<EditJob>? JobCompleted;
    public event EventHandler<(EditJob Job, Exception Error)>? JobFailed;
    public event EventHandler? BatchCompleted;
    public Func<bool>? CanStartJob { get; set; }

    public void RefreshSubscriptionAccess()
    {
        for (var i = 0; i < _maxConcurrency; i++)
            SignalWork();
    }

    public VideoQueueManager(VideoEditorService editService, int initialConcurrency = 2)
    {
        _editorService = editService;
        _maxConcurrency = Math.Clamp(initialConcurrency, 1, 4);

        var channelOptions = new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false,
        };
        _signalChannel = Channel.CreateUnbounded<bool>(channelOptions);

        AdjustWorkers();
        _ = RunElapsedTimerAsync(_managerCts.Token);
    }

    private async Task RunElapsedTimerAsync(CancellationToken token)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            while (
                !token.IsCancellationRequested
                && await timer.WaitForNextTickAsync(token).ConfigureAwait(false)
            )
            {
                lock (_syncLock)
                {
                    foreach (var job in Jobs)
                    {
                        if (job.Status == EditJobStatus.Processing && job.StartTime.HasValue)
                        {
                            job.ElapsedTime = DateTimeOffset.Now - job.StartTime.Value;
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Graceful shutdown
        }
        catch
        {
            // Suppress unexpected timer errors
        }
    }

    private void SignalWork()
    {
        _signalChannel.Writer.TryWrite(true);
    }

    public async Task InitializeFromDiskAsync()
    {
        var savedJobs = await QueuePersistenceService.LoadQueueAsync();
        if (savedJobs.Count == 0)
            return;

        bool hasQueued = false;
        lock (_syncLock)
        {
            foreach (var job in savedJobs)
            {
                if (!Jobs.Any(j => j.Id == job.Id))
                {
                    Jobs.Add(job);
                }
            }

            RecalculatePriorities();

            if (Jobs.Any(j => j.Status == EditJobStatus.Paused))
            {
                _isPaused = true;
            }
            else if (Jobs.Any(j => j.Status == EditJobStatus.Queued))
            {
                hasQueued = true;
            }
        }

        if (hasQueued && !_isPaused)
        {
            SignalWork();
        }
    }

    public void ScheduleSaveQueue()
    {
        _ = Task.Run(async () =>
        {
            List<EditJob> snapshot;
            lock (_syncLock)
            {
                snapshot = [.. Jobs];
            }
            await QueuePersistenceService.SaveQueueAsync(snapshot);
        });
    }

    public async Task EnqueueJobAsync(EditJob job, CancellationToken cancellationToken = default)
    {
        lock (_syncLock)
        {
            _batchCompletionFired = false;
            job.Status = EditJobStatus.Queued;
            job.Cts = new CancellationTokenSource();
            Jobs.Add(job);
            RecalculatePriorities();
        }

        ScheduleSaveQueue();

        // Asynchronously probe video metadata before execution
        _ = Task.Run(
            async () =>
            {
                try
                {
                    await _editorService.ProbeVideoAsync(job, cancellationToken);
                }
                catch
                {
                    // Non-fatal probe failure
                }
                finally
                {
                    SignalWork();
                }
            },
            cancellationToken
        );

        SignalWork();
    }

    public async Task EnqueueBatchAsync(
        IEnumerable<EditJob> batch,
        CancellationToken cancellationToken = default
    )
    {
        foreach (var job in batch)
        {
            await EnqueueJobAsync(job, cancellationToken);
        }
    }

    public async Task PauseAsync()
    {
        if (_isPaused)
            return;

        await _pauseGate.WaitAsync();
        _isPaused = true;

        lock (_syncLock)
        {
            foreach (var job in Jobs)
            {
                if (job.Status == EditJobStatus.Queued)
                {
                    job.Status = EditJobStatus.Paused;
                }
                else if (job.Status == EditJobStatus.Processing)
                {
                    SuspendProcessSafely(job.ActiveProcess);
                    job.Status = EditJobStatus.Paused;
                }
            }
        }

        ScheduleSaveQueue();
    }

    public void Resume()
    {
        if (!_isPaused)
            return;

        _isPaused = false;
        if (_pauseGate.CurrentCount == 0)
        {
            _pauseGate.Release();
        }

        lock (_syncLock)
        {
            foreach (var job in Jobs.Where(j => j.Status == EditJobStatus.Paused))
            {
                if (job.ActiveProcess != null && !job.ActiveProcess.HasExited)
                {
                    ResumeProcessSafely(job.ActiveProcess);
                    job.Status = EditJobStatus.Processing;
                }
                else
                {
                    job.Status = EditJobStatus.Queued;
                }
            }
        }

        ScheduleSaveQueue();
        SignalWork();
    }

    public void CancelJob(EditJob job)
    {
        lock (_syncLock)
        {
            _activeJobIds.Remove(job.Id);

            if (
                job.Status
                is EditJobStatus.Processing
                    or EditJobStatus.Queued
                    or EditJobStatus.Paused
            )
            {
                ResumeProcessSafely(job.ActiveProcess);

                try
                {
                    job.Cts?.Cancel();
                }
                catch
                {
                    // Ignore cancellation dispatch issues
                }

                try
                {
                    if (job.ActiveProcess != null && !job.ActiveProcess.HasExited)
                    {
                        job.ActiveProcess.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                    // Ignore process kill errors
                }

                job.Status = EditJobStatus.Canceled;
            }
        }

        ScheduleSaveQueue();
        SignalWork();
    }

    public void CancelAll()
    {
        lock (_syncLock)
        {
            foreach (var job in Jobs)
            {
                CancelJob(job);
            }
        }

        ScheduleSaveQueue();
    }

    public async Task RetryJobAsync(EditJob job)
    {
        if (job.Status is not (EditJobStatus.Failed or EditJobStatus.Canceled))
            return;

        lock (_syncLock)
        {
            _activeJobIds.Remove(job.Id);
            job.Status = EditJobStatus.Queued;
            job.Progress = 0;
            job.CurrentFrame = 0;
            job.ErrorMessage = null;
            job.RetryCount = 0;
            job.Cts = new CancellationTokenSource();
            RecalculatePriorities();
        }

        ScheduleSaveQueue();
        SignalWork();
    }

    public void MoveUp(EditJob job)
    {
        lock (_syncLock)
        {
            var index = Jobs.IndexOf(job);
            if (index > 0)
            {
                Jobs.Move(index, index - 1);
                RecalculatePriorities();
            }
        }
        SignalWork();
    }

    public void MoveDown(EditJob job)
    {
        lock (_syncLock)
        {
            var index = Jobs.IndexOf(job);
            if (index >= 0 && index < Jobs.Count - 1)
            {
                Jobs.Move(index, index + 1);
                RecalculatePriorities();
            }
        }
        SignalWork();
    }

    private void RecalculatePriorities()
    {
        int count = Jobs.Count;
        for (int i = 0; i < count; i++)
        {
            Jobs[i].Priority = count - i;
        }
    }

    public void ClearCompleted()
    {
        lock (_syncLock)
        {
            var completed = Jobs.Where(j =>
                    j.Status is EditJobStatus.Complete or EditJobStatus.Canceled
                )
                .ToList();
            foreach (var item in completed)
            {
                _activeJobIds.Remove(item.Id);
                Jobs.Remove(item);
            }
            RecalculatePriorities();
        }

        ScheduleSaveQueue();
    }

    private void AdjustWorkers()
    {
        lock (_syncLock)
        {
            _workerTasks.RemoveAll(t => t.IsCompleted);
            while (_workerTasks.Count < _maxConcurrency)
            {
                var workerId = _workerTasks.Count + 1;
                _workerTasks.Add(Task.Run(() => WorkerLoopAsync(workerId, _managerCts.Token)));
            }
        }
        SignalWork();
    }

    private async Task WorkerLoopAsync(int workerId, CancellationToken managerToken)
    {
        while (!managerToken.IsCancellationRequested)
        {
            if (workerId > _maxConcurrency)
            {
                // Worker gracefully exits when concurrency limit is lowered
                break;
            }

            try
            {
                // Wait if paused
                await _pauseGate.WaitAsync(managerToken);
                _pauseGate.Release();

                // Await next work signal
                await _signalChannel.Reader.ReadAsync(managerToken);

                if (workerId > _maxConcurrency)
                {
                    break;
                }

                EditJob? job = null;
                lock (_syncLock)
                {
                    if (!_isPaused && (CanStartJob?.Invoke() ?? true))
                    {
                        job = Jobs.Where(j =>
                                j.Status == EditJobStatus.Queued && !_activeJobIds.Contains(j.Id)
                            )
                            .OrderByDescending(j => j.Priority)
                            .FirstOrDefault();

                        if (job != null)
                        {
                            _activeJobIds.Add(job.Id);
                            job.Status = EditJobStatus.Processing;
                            job.StartTime ??= DateTimeOffset.Now;
                        }
                    }
                }

                if (job == null)
                    continue;

                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    managerToken,
                    job.Cts?.Token ?? CancellationToken.None
                );

                try
                {
                    JobStarted?.Invoke(this, job);
                    linkedCts.Token.ThrowIfCancellationRequested();
                    await _editorService.ProcessJobAsync(job, linkedCts.Token);
                    JobCompleted?.Invoke(this, job);
                    CheckAndNotifyBatchCompletion();
                    ScheduleSaveQueue();
                }
                catch (Exception ex)
                    when (ex is OperationCanceledException || linkedCts.IsCancellationRequested)
                {
                    job.Status = EditJobStatus.Canceled;
                    CheckAndNotifyBatchCompletion();
                    ScheduleSaveQueue();
                }
                catch (Exception ex)
                {
                    // Automatic retry mechanism for soft errors (up to 2 retries)
                    if (job.RetryCount < 2 && !IsHardFatalError(ex.Message))
                    {
                        job.RetryCount++;
                        job.Status = EditJobStatus.Queued;
                        job.DetailedLog +=
                            $"{Environment.NewLine}[Warning] Soft error encountered. Scheduling retry {job.RetryCount}/2...";
                        ScheduleSaveQueue();
                        SignalWork();
                    }
                    else
                    {
                        job.Status = EditJobStatus.Failed;
                        job.ErrorMessage ??= ex.Message;
                        JobFailed?.Invoke(this, (job, ex));
                        CheckAndNotifyBatchCompletion();
                        ScheduleSaveQueue();
                    }
                }
                finally
                {
                    lock (_syncLock)
                    {
                        _activeJobIds.Remove(job.Id);
                    }

                    // If more queued jobs remain, pulse work signal
                    if (Jobs.Any(j => j.Status == EditJobStatus.Queued))
                    {
                        SignalWork();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                await Task.Delay(500, managerToken);
            }
        }
    }

    private static bool IsHardFatalError(string message) =>
        message.Contains("out of memory", StringComparison.OrdinalIgnoreCase)
        || message.Contains("VK_ERROR_DEVICE_LOST", StringComparison.OrdinalIgnoreCase)
        || message.Contains("device lost", StringComparison.OrdinalIgnoreCase);

    private void CheckAndNotifyBatchCompletion()
    {
        lock (_syncLock)
        {
            if (_batchCompletionFired)
                return;

            bool anyActive = Jobs.Any(j =>
                j.Status is EditJobStatus.Processing or EditJobStatus.Queued or EditJobStatus.Paused
            );

            if (!anyActive && Jobs.Count > 0 && Jobs.Any(j => j.Status == EditJobStatus.Complete))
            {
                _batchCompletionFired = true;
                Task.Run(() => BatchCompleted?.Invoke(this, EventArgs.Empty));
            }
        }
    }

    public void StopAllJobs()
    {
        _managerCts.Cancel();
        foreach (var job in Jobs)
        {
            try
            {
                job.Cts?.Cancel();
                if (job.ActiveProcess is { HasExited: false } proc)
                {
                    proc.Kill(entireProcessTree: true);
                }
            }
            catch { }
        }
        ChildProcessTracker.KillAll();
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;
        _isDisposed = true;

        StopAllJobs();
        _signalChannel.Writer.TryComplete();
        _pauseGate.Dispose();
        _managerCts.Dispose();
    }
}
