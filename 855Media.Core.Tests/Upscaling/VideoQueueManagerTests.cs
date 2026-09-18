using System;
using System.Threading.Tasks;
using _855Media.Core.Upscaling;
using Xunit;

namespace _855Media.Core.Tests.Upscaling;

public class VideoQueueManagerTests : IDisposable
{
    private readonly VideoUpscaleService _service = new();
    private readonly string _tempQueueFile;

    public VideoQueueManagerTests()
    {
        _tempQueueFile = Path.Combine(Path.GetTempPath(), $"test_queue_{Guid.NewGuid():N}.json");
        QueuePersistenceService.CustomQueueFilePath = _tempQueueFile;
    }

    public void Dispose()
    {
        QueuePersistenceService.CustomQueueFilePath = null;
        if (File.Exists(_tempQueueFile))
        {
            try
            {
                File.Delete(_tempQueueFile);
            }
            catch { }
        }
    }

    [Fact]
    public async Task EnqueueJobAsync_AddsJobToCollectionAndAssignsPriority()
    {
        using var queue = new VideoQueueManager(_service, initialConcurrency: 2);
        await queue.PauseAsync();

        var job1 = new UpscaleJob { FilePath = "job1.mp4" };
        var job2 = new UpscaleJob { FilePath = "job2.mp4" };

        await queue.EnqueueJobAsync(job1);
        await queue.EnqueueJobAsync(job2);

        Assert.Equal(2, queue.Jobs.Count);
        Assert.Equal(UpscaleJobStatus.Queued, job1.Status);
        Assert.Equal(UpscaleJobStatus.Queued, job2.Status);
        Assert.True(job1.Priority > job2.Priority);
    }

    [Fact]
    public async Task MoveUp_MoveDown_AdjustsJobPriorities()
    {
        using var queue = new VideoQueueManager(_service, initialConcurrency: 2);
        var job1 = new UpscaleJob { FilePath = "job1.mp4" };
        var job2 = new UpscaleJob { FilePath = "job2.mp4" };

        await queue.EnqueueJobAsync(job1);
        await queue.EnqueueJobAsync(job2);

        int initialJob1Priority = job1.Priority;
        int initialJob2Priority = job2.Priority;

        // Move job2 up
        queue.MoveUp(job2);
        Assert.True(job2.Priority > job1.Priority);

        // Move job2 down
        queue.MoveDown(job2);
        Assert.True(job1.Priority > job2.Priority);
    }

    [Fact]
    public async Task CancelJob_SetsStatusToCanceled()
    {
        using var queue = new VideoQueueManager(_service, initialConcurrency: 2);
        var job = new UpscaleJob { FilePath = "test.mp4" };

        await queue.EnqueueJobAsync(job);
        queue.CancelJob(job);

        Assert.Equal(UpscaleJobStatus.Canceled, job.Status);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(3, 3)]
    [InlineData(10, 4)]
    public void MaxConcurrency_ClampsValueBetweenOneAndFour(int input, int expected)
    {
        using var queue = new VideoQueueManager(_service, initialConcurrency: input);
        Assert.Equal(expected, queue.MaxConcurrency);

        queue.MaxConcurrency = input;
        Assert.Equal(expected, queue.MaxConcurrency);
    }
}
