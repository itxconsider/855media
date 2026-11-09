using System.Xml.Linq;

namespace _855Media.Core.LDPlayer;

public static partial class FacebookLayoutRegistry
{
    // This component proves a selected name, not an actor type. Callers retain
    // their complete-list membership/type proof and process/foreground guards.
    private static XElement? SelectedActorNode(FacebookLayoutObservation observation)
    {
        if (string.IsNullOrWhiteSpace(observation.Context.TargetName))
            return null;
        var row = PFlowPersonaSwitcherService.FreshSelectedNativeRow(observation.Xml);
        return
            row != null
            && PFlowNameMatch.Same(
                PFlowPersonaSwitcherService.NativeRowName(row),
                observation.Context.TargetName
            )
            ? row
            : null;
    }
}
