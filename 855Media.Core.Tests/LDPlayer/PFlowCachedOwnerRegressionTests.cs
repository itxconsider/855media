using System.Reflection;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowCachedOwnerRegressionTests
{
    [Theory]
    [InlineData("0")]
    [InlineData("false")]
    [InlineData("1")]
    public async Task UnderlyingOwnerCannotBecomePageFromStaleDelegation(string pageFlag)
    {
        var ld = DispatchProxy.Create<ILDPlayerService, RootDevice>();
        var fake = (RootDevice)(object)ld;
        fake.PageFlag = pageFlag;
        var result = await new PFlowAccountDiscoveryService(
            ld
        ).ExtractFacebookAccountsDirectFromRootAdbAsync(2);
        var profile = Assert.Single(result, a => a.Type == "Profile");
        Assert.Equal("100008345077125", profile.Uid);
        Assert.Equal(profile.Uid, profile.PageId);
        Assert.Equal(profile.Uid, profile.UserUid);
        Assert.True(profile.IsActive);
        var page = Assert.Single(result, a => a.Type == "Page");
        Assert.Equal("61588056487279", page.Uid);
        Assert.Equal("1007497349108596", page.PageId);
        Assert.Equal(profile.Uid, page.UserUid);
        Assert.False(page.IsActive);
    }

    [Fact]
    public async Task FalsePageFlagAloneDoesNotTurnOwnerIntoPage()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, RootDevice>();
        var fake = (RootDevice)(object)ld;
        fake.ExplicitUnderlying = false;
        fake.StaleDelegation = false;
        var result = await new PFlowAccountDiscoveryService(
            ld
        ).ExtractFacebookAccountsDirectFromRootAdbAsync(2);
        Assert.Single(result, a => a.Type == "Profile" && a.Uid == "100008345077125");
    }

    [Fact]
    public async Task AmbiguousCachedDelegationWithoutUnderlyingOwnerIsNotPromotedToPersonal()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, RootDevice>();
        ((RootDevice)(object)ld).ExplicitUnderlying = false;
        var result = await new PFlowAccountDiscoveryService(
            ld
        ).ExtractFacebookAccountsDirectFromRootAdbAsync(2);
        Assert.DoesNotContain(result, a => a.Type == "Profile");
    }

    public class RootDevice : DispatchProxy
    {
        public string PageFlag = "false";
        public bool ExplicitUnderlying = true,
            StaleDelegation = true;

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            foreach (var arg in args ?? [])
                if (arg is CancellationToken ct)
                    ct.ThrowIfCancellationRequested();
            if (method!.Name != "ExecuteAdbAsync")
                throw new NotSupportedException(method.Name);
            var cmd = (string)args![1]!;
            var value =
                cmd.EndsWith("su 0 id") ? "uid=0(root)"
                : cmd.Contains("ls -1") ? "logged_in_100008345077125\nlogged_in_61588056487279"
                : cmd.EndsWith("/underlying_account")
                    ? (
                        ExplicitUnderlying
                            ? "\u0001underlying_account_uid\u0001100008345077125\n"
                            : ""
                    )
                : cmd.EndsWith("/authentication") ? "user_id\n100008345077125\n"
                : cmd.EndsWith("/logged_in_100008345077125")
                    ? "name\nGerardo Lopez\nis_page\n"
                        + PageFlag
                        + "\n"
                        + (StaleDelegation ? "delegate_page_id\n935789272945529\n" : "")
                : cmd.EndsWith("/logged_in_61588056487279")
                    ? "name\nKC Waterheaters\nis_page\n1\ndelegate_page_id\n1007497349108596\nuser_id\n100008345077125\n"
                : "";
            return Task.FromResult(value);
        }
    }
}
