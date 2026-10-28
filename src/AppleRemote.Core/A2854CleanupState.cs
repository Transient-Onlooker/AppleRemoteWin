// SPDX-License-Identifier: AGPL-3.0-or-later
namespace AppleRemote;

public enum A2854CleanupState { None, Pending, Succeeded, Failed }

public static class A2854CleanupPolicy
{
    public static A2854CleanupState Assess(Task? cleanup) => cleanup switch
    {
        null => A2854CleanupState.None,
        { IsCompleted: false } => A2854CleanupState.Pending,
        { IsCompletedSuccessfully: true } => A2854CleanupState.Succeeded,
        _ => A2854CleanupState.Failed
    };

    public static bool CanStart(A2854CleanupState state) =>
        state is A2854CleanupState.None or A2854CleanupState.Succeeded;
}
