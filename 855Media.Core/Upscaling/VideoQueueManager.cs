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

namespace _855Media.Core.Upscaling;

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

    private readonly VideoUpscaleService _upscaleService;
    private readonly Channel<UpscaleJob> _jobChannel;
    private readonly List<Task> _workerTasks = [];
    private readonly CancellationTokenSource _managerCts = new();
    private readonly SemaphoreSlim _pauseGate = new(1, 1);
    private readonly object _syncLock = new();

    private int _maxConcurrency = 2;
    private bool _isPaused;
    private bool _isDisposed;
    private bool _batchCompletionFired;

    public ObservableCollection<UpscaleJob> Jobs { get; } = [];

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
        && Jobs.Any(j =>
            j.Status == UpscaleJobStatus.Processing || j.Status == UpscaleJobStatus.Queued
        );

    public event EventHandler<UpscaleJob>? JobStarted;
    public event EventHandler<UpscaleJob>? JobCompleted;
    public event EventHandler<(UpscaleJob Job, Exception Error)>? JobFailed;
    public event EventHandler? BatchCompleted;

    public VideoQueueManager(VideoUpscaleService upscaleService, int initialConcurrency = 2)
    {
        _upscaleService = upscaleService;
        _maxConcurrency = Math.Clamp(initialConcurrency, 1, 4);

        // Bounded channel to enforce smooth flow control
        var channelOptions = new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false,
        };

        _jobChannel = Channel.CreateBounded<UpscaleJob>(channelOptions);

        AdjustWorkers();
    }

    public async Task EnqueueJobAsync(UpscaleJob job, CancellationToken cancellationToken = default)
    {
        lock (_syncLock)
        {
            _batchCompletionFired = false;
            job.Status = UpscaleJobStatus.Queued;
            job.Cts = new CancellationTokenSource();
            Jobs.Add(job);
        }

        // Asynchronously probe video metadata before execution
        _ = Task.Run(
            async () =>
            {
                try
                {
                    await _upscaleService.ProbeVideoAsync(job, cancellationToken);
                }
                catch
                {
                    // Non-fatal probe failure
                }
            },
            cancellationToken
        );

        await _jobChannel.Writer.WriteAsync(job, cancellationToken);
    }

    public async Task EnqueueBatchAsync(
        IEnumerable<UpscaleJob> batch,
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
                if (job.Status == UpscaleJobStatus.Queued)
                {
                    job.Status = UpscaleJobStatus.Paused;
                }
                else if (job.Status == UpscaleJobStatus.Processing)
                {
                    SuspendProcessSafely(job.ActiveProcess);
                    job.Status = UpscaleJobStatus.Paused;
                }
            }
        }
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
            foreach (var job in Jobs.Where(j => j.Status == UpscaleJobStatus.Paused))
            {
                if (job.ActiveProcess != null && !job.ActiveProcess.HasExited)
                {
                    ResumeProcessSafely(job.ActiveProcess);
                    job.Status = UpscaleJobStatus.Processing;
                }
                else
                {
                    job.Status = UpscaleJobStatus.Queued;
                }
            }
        }
    }

    public void CancelJob(UpscaleJob job)
    {
        lock (_syncLock)
        {
            if (
                job.Status
                is UpscaleJobStatus.Processing
                    or UpscaleJobStatus.Queued
                    or UpscaleJobStatus.Paused
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

                job.Status = UpscaleJobStatus.Canceled;
            }
        }
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
    }

    public async Task RetryJobAsync(UpscaleJob job)
    {
        if (job.Status is not (UpscaleJobStatus.Failed or UpscaleJobStatus.Canceled))
            return;

        job.Status = UpscaleJobStatus.Queued;
        job.Progress = 0;
        job.CurrentFrame = 0;
        job.ErrorMessage = null;
        job.Cts = new CancellationTokenSource();

        await _jobChannel.Writer.WriteAsync(job);
    }

    public void MoveUp(UpscaleJob job)
    {
        lock (_syncLock)
        {
            var index = Jobs.IndexOf(job);
            if (index > 0)
            {
                Jobs.Move(index, index - 1);
                job.Priority++;
            }
        }
    }

    public void MoveDown(UpscaleJob job)
    {
        lock (_syncLock)
        {
            var index = Jobs.IndexOf(job);
            if (index >= 0 && index < Jobs.Count - 1)
            {
                Jobs.Move(index, index + 1);
                job.Priority--;
            }
        }
    }

    public void ClearCompleted()
    {
        lock (_syncLock)
        {
            var completed = Jobs.Where(j =>
                    j.Status is UpscaleJobStatus.Complete or UpscaleJobStatus.Canceled
                )
                .ToList();
            foreach (var item in completed)
            {
                Jobs.Remove(item);
            }
        }
    }

    private void AdjustWorkers()
    {
        lock (_syncLock)
        {
            while (_workerTasks.Count < _maxConcurrency)
            {
                var workerId = _workerTasks.Count + 1;
                _workerTasks.Add(Task.Run(() => WorkerLoopAsync(workerId, _managerCts.Token)));
            }
        }
    }

    private async Task WorkerLoopAsync(int workerId, CancellationToken managerToken)
    {
        while (!managerToken.IsCancellationRequested)
        {
            try
            {
                // Wait if paused
                await _pauseGate.WaitAsync(managerToken);
                _pauseGate.Release();

                var job = await _jobChannel.Reader.ReadAsync(managerToken);

                // Skip cancelled or paused jobs
                if (job.Status is UpscaleJobStatus.Canceled or UpscaleJobStatus.Complete)
                    continue;

                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    managerToken,
                    job.Cts?.Token ?? CancellationToken.None
                );

                JobStarted?.Invoke(this, job);

                try
                {
                    await _upscaleService.ProcessJobAsync(job, linkedCts.Token);
                    JobCompleted?.Invoke(this, job);
                    CheckAndNotifyBatchCompletion();
                }
                catch (OperationCanceledException)
                {
                    job.Status = UpscaleJobStatus.Canceled;
                    CheckAndNotifyBatchCompletion();
                }
                catch (Exception ex)
                {
                    // Automatic retry mechanism for soft errors (up to 2 retries)
                    if (job.RetryCount < 2 && !IsHardFatalError(ex.Message))
                    {
                        job.RetryCount++;
                        job.Status = UpscaleJobStatus.Queued;
                        job.DetailedLog +=
                            $"{Environment.NewLine}[Warning] Soft error encountered. Scheduling retry {job.RetryCount}/2...";
                        await _jobChannel.Writer.WriteAsync(job, managerToken);
                    }
                    else
                    {
                        JobFailed?.Invoke(this, (job, ex));
                        CheckAndNotifyBatchCompletion();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Continue worker loop
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
                j.Status
                    is UpscaleJobStatus.Processing
                        or UpscaleJobStatus.Queued
                        or UpscaleJobStatus.Paused
            );

            if (
                !anyActive
                && Jobs.Count > 0
                && Jobs.Any(j => j.Status == UpscaleJobStatus.Complete)
            )
            {
                _batchCompletionFired = true;
                Task.Run(() => BatchCompleted?.Invoke(this, EventArgs.Empty));
            }
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;
        _isDisposed = true;

        _managerCts.Cancel();
        _jobChannel.Writer.TryComplete();
        _pauseGate.Dispose();
        _managerCts.Dispose();
    }
}
