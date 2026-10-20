using _855Media.Localization;
using _855Media.Services;
using _855Media.ViewModels.Components;

// Exercise the real view model without starting the app, downloading files,
// changing user settings, or creating a subscription/entitlement bypass.
using var localization = new LocalizationManager(new SettingsService());
DownloadViewModel NewDownload() => new(null!, null!, localization);

void Check(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
    Console.WriteLine("PASS: " + message);
}

async Task ExpectCancellation(Task work, string message)
{
    try
    {
        await work.WaitAsync(TimeSpan.FromSeconds(5));
    }
    catch (OperationCanceledException)
    {
        Console.WriteLine("PASS: " + message);
        return;
    }
    throw new InvalidOperationException(message);
}

using (var download = NewDownload())
{
    var original = download.CancellationToken;
    download.Dispose();
    Check(download.CancellationToken == original, "token getter remains safe after disposal");
    Check(original.IsCancellationRequested, "disposing a removed row cancels its attempt");
    // Reproduce the exact cancellation read in the dashboard's exception handler.
    Exception error = new IOException("download failure fixture");
    var isCancellation =
        error is OperationCanceledException || download.CancellationToken.IsCancellationRequested;
    Check(isCancellation, "error handler classifies disposed row without ObjectDisposedException");
    download.Dispose();
    download.CancelCommand.Execute(null);
    Check(
        download.CancellationToken.IsCancellationRequested,
        "repeated disposal and cancel are safe"
    );
}

using (var download = NewDownload())
{
    var token = download.CancellationToken;
    var pending = Task.Delay(Timeout.InfiniteTimeSpan, token);
    download.Dispose();
    await ExpectCancellation(pending, "disposal wakes pending async work");
}

using (var download = NewDownload())
{
    download.Status = DownloadStatus.Failed;
    var oldAttempt = download.CancellationToken;
    var autoRetryDelay = Task.Delay(Timeout.InfiniteTimeSpan, oldAttempt);
    var retries = 0;
    download.RetryRequested += (_, _) => retries++;
    download.RetryCommand.Execute(null);
    Check(retries == 1 && download.Status == DownloadStatus.Enqueued, "manual retry enqueues once");
    Check(oldAttempt.IsCancellationRequested, "manual retry cancels the previous attempt");
    Check(
        download.CancellationToken != oldAttempt
            && !download.CancellationToken.IsCancellationRequested,
        "retry receives a fresh uncanceled token"
    );
    await ExpectCancellation(
        autoRetryDelay,
        "manual retry interrupts the old automatic retry delay"
    );
}

using (var download = NewDownload())
{
    var oldAttempt = download.CancellationToken;
    download.CancelCommand.Execute(null);
    download.Status = DownloadStatus.Canceled;
    download.RetryCommand.Execute(null);
    Check(
        oldAttempt.IsCancellationRequested && !download.CancellationToken.IsCancellationRequested,
        "retry after cancellation preserves old attempt cancellation"
    );
}

using (var download = NewDownload())
{
    download.Status = DownloadStatus.Failed;
    var retries = 0;
    download.RetryRequested += (_, _) => retries++;
    download.Dispose();
    var oldAttempt = download.CancellationToken;
    download.ResetCancellationToken();
    download.RetryCommand.Execute(null);
    Check(
        download.CancellationToken == oldAttempt
            && download.CancellationToken.IsCancellationRequested,
        "reset cannot revive a disposed row"
    );
    Check(
        !download.RetryCommand.CanExecute(null) && retries == 0,
        "removed row cannot request retry"
    );
}

Console.WriteLine("All download lifecycle regression checks passed.");
