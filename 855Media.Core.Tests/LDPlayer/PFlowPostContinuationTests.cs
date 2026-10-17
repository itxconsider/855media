using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowPostContinuationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DirectAppConfirmationAllowsContinuationWithoutProfileLookup(
        bool wrongComposer
    )
    {
        var directory = Path.Combine(Path.GetTempPath(), "pflow-continuation-" + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(directory, "vms", "config"));
        Directory.CreateDirectory(Path.Combine(directory, "vms", "leidian35"));
        try
        {
            File.WriteAllText(
                Path.Combine(directory, "vms", "config", "leidian35.config"),
                "{\"propertySettings.phoneAndroidId\":\"synthetic\"}"
            );
            File.WriteAllText(
                Path.Combine(directory, "vms", "leidian35", "data.vmdk"),
                "synthetic"
            );
            var identity = PFlowDeviceRouting.ReadIdentity(directory, 35);
            PFlowJob NewJob()
            {
                var job = PFlowPostTargetSessionTests.Job();
                job.AccountBinding = job.AccountBinding! with { InstanceIdentity = identity };
                job.Size = 123;
                job.Sha256 = new string('a', 64);
                job.SourcePath = "synthetic.mp4";
                return job;
            }
            var first = NewJob();
            var ld = PFlowTests.FakeLd.Create(first);
            var device = (PFlowTests.FakeLd)(object)ld;
            var publisher = new PFlowAutoPostService(ld);
            var session = new PFlowPostTargetSession();
            var uidReads = 0;
            var profileOpens = 0;
            var betweenPosts = false;
            async Task Verify(PFlowJob job)
            {
                Assert.True(
                    await session.VerifyAsync(
                        job,
                        c =>
                            PFlowLiveUidVerification.RequireAsync(
                                new FacebookPageTarget
                                {
                                    PageId = job.TargetId,
                                    UserUid = job.OwnerUid,
                                    PersonaUid = job.PersonaUid,
                                    PageName = job.TargetName,
                                },
                                _ =>
                                {
                                    uidReads++;
                                    return Task.FromResult<PFlowGraphIdentity?>(
                                        new(job.TargetId, job.TargetName, job.OwnerUid, null)
                                    );
                                },
                                _ =>
                                    throw new InvalidOperationException(
                                        "Graph already verifies the target."
                                    ),
                                (_, _, _) => Task.FromResult(true),
                                c
                            ),
                        _ =>
                        {
                            profileOpens++;
                            Assert.False(
                                betweenPosts,
                                "Consecutive confirmed posts should not reopen the profile."
                            );
                            return Task.FromResult(true);
                        },
                        _ =>
                            Task.FromResult(
                                betweenPosts
                                    ? PFlowPostTargetSessionTests.Feed
                                    : PFlowPostTargetSessionTests.Own(job)
                            ),
                        default
                    )
                );
                job.State = PFlowState.IdentityVerified;
            }
            await Verify(first);
            await publisher.PublishAsync(first, _ => Task.CompletedTask, directory, default);
            Assert.Equal(PFlowState.Confirmed, first.State);
            Assert.False(first.ProfilePublicationCheckRequired);
            Assert.Equal(1, device.ShareTaps);
            Assert.False(first.RequiresPublicationReviewBeforeCleanup);

            betweenPosts = true;
            var second = NewJob();
            device.NextJob(second, PFlowPostTargetSessionTests.Feed);
            device.WrongActor = wrongComposer;
            await Verify(second);
            Assert.Equal(1, uidReads);
            Assert.Equal(1, profileOpens);
            if (wrongComposer)
            {
                var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    publisher.PublishAsync(second, _ => Task.CompletedTask, directory, default)
                );
                Assert.Contains("Composer identity", error.Message);
                Assert.Equal(0, device.ShareTaps);
            }
            else
            {
                await publisher.PublishAsync(second, _ => Task.CompletedTask, directory, default);
                Assert.Equal(PFlowState.Confirmed, second.State);
                Assert.NotNull(second.ConfirmedUtc);
                Assert.False(second.ProfilePublicationCheckRequired);
                Assert.False(second.RequiresPublicationReviewBeforeCleanup);
                Assert.Equal(1, device.ShareTaps);
                Assert.Contains("previous confirmed post", second.IdentityEvidence);
            }
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
