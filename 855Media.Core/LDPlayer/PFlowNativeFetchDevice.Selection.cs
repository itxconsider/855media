using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

public sealed partial class PFlowNativeFetchDevice
{
    private PFlowFetchActor? _selectedActor;

    private bool HasSelection(PFlowFetchActor actor) =>
        _selectedActor?.Type == actor.Type
        && PFlowNameMatch.Same(_selectedActor.Name, actor.Name)
        && _verifiedActors?.Count(a =>
            a.Type == actor.Type && PFlowNameMatch.Same(a.Name, actor.Name)
        ) == 1;

    private bool OwnSelection(string xml, PFlowFetchActor actor)
    {
        if (!HasSelection(actor) || !PFlowPageAboutTransparency.Safe(xml))
            return false;
        var screen = AtxAgentClient.ParseBloksHierarchy(xml);
        return PFlowProfileDates.MatchesName(screen, actor.Name)
            && !AtxAgentClient.IsMenuPageInBloks(screen)
            && (
                actor.Type == "Page"
                    ? PFlowOwnProfileNavigation.IsOwnPageProfile(screen, actor.Name)
                    : actor.Type == "Profile" && PFlowNativeAccountNavigator.OwnProfile(xml)
            );
    }

    private async Task<bool> VerifySelectedProfileAsync(PFlowFetchActor actor, CancellationToken ct)
    {
        if (!HasSelection(actor))
            return false;
        for (var frame = 0; frame < 2; frame++)
        {
            try
            {
                await RequireAsync(ct);
            }
            catch
            {
                _selectedActor = null;
                throw;
            }
            var xml = await ld.DumpUiHierarchyAsync(index, path, ct) ?? "";
            if (!OwnSelection(xml, actor))
            {
                _selectedActor = null;
                return false;
            }
        }
        return true;
    }

    private async Task<bool> OpenSelectedProfileAsync(PFlowFetchActor actor, CancellationToken ct)
    {
        if (!HasSelection(actor))
            return false;
        // Native own-link lookup may leave a scrolled settings stack even on
        // unavailable links. Satisfy its Feed obligation before profile navigation;
        // a bottom settings action is never a navigation tab.
        await PrepareSwitcherAfterLinkLookupAsync(ct);
        await RequireAsync(ct);
        var opened = await PFlowOwnProfileNavigation.OpenAsync(
            ld,
            index,
            actor.Name,
            path,
            s => OwnSelection(s.RawXml ?? "", actor),
            ct,
            progress,
            navigateCurrentActor: true,
            canNavigate: xml => PFlowPageAboutTransparency.Safe(xml) && !IsProfileSettings(xml)
        );
        return opened && await VerifySelectedProfileAsync(actor, ct);
    }
}
