using System;
using System.Text.RegularExpressions;

namespace _855Media.Core.LDPlayer;

// Applies to ADB and ATX, including calls outside a Fetch invocation.
public static class PFlowDeviceAccessPolicy
{
    // Only the exact read in a matching opted-in Fetch invocation may bypass
    // the general ADB restriction. ATX shell/file access retains the global rule.
    internal static void RequireAdbCommand(
        ILDPlayerService ld,
        int index,
        string? path,
        string command
    )
    {
        if (!PFlowNativeFetchScope.AllowsGraphSessionRead(ld, index, path, command))
            RequireUnprivileged(command);
        PFlowNativeFetchScope.RequireAllowed(ld, index, path, command);
    }

    public static void RequireUnprivileged(string command)
    {
        if (
            Regex.IsMatch(
                command,
                @"(?:^|[^\w.-])(?:su|run-as)(?:$|[^\w.-])",
                RegexOptions.IgnoreCase
            )
            || Regex.IsMatch(
                command.TrimStart(),
                @"^(?:root|remount|disable-verity)(?:\s|$)",
                RegexOptions.IgnoreCase
            )
            || Regex.IsMatch(
                command,
                @"/data/(?:data|user|user_de|system|misc)(?:/|\b)",
                RegexOptions.IgnoreCase
            )
        )
            throw new NotSupportedException(
                "Privileged commands and private Android storage access were removed. Use native UI operations."
            );
    }
}
