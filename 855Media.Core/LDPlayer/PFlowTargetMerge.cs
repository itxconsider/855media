using System;

namespace _855Media.Core.LDPlayer;

public static class PFlowTargetMerge
{
    public static FacebookPageTarget Cached(FacebookPageTarget? saved, FacebookPageTarget incoming)
    {
        if (saved == null)
            return incoming;
        if (
            saved.DeviceName != incoming.DeviceName
            || saved.AppClone != incoming.AppClone
            || saved.UserUid != incoming.UserUid
            || saved.PageId != incoming.PageId
            || saved.Type != incoming.Type
        )
            throw new InvalidOperationException(
                "Cached target merge requires the same instance, app, owner and target."
            );
        var result = saved.CreateSnapshot();
        result.LivePageAccessVerified = incoming.LivePageAccessVerified;
        if (result.LivePageAccessVerified == false)
            result.IsSelected = false;
        string Prefer(string current, string previous) =>
            string.IsNullOrWhiteSpace(current) ? previous : current;
        result.PersonaUid = Prefer(incoming.PersonaUid, saved.PersonaUid);
        result.PageName = Prefer(incoming.PageName, saved.PageName);
        result.Email = Prefer(incoming.Email, saved.Email);
        result.ManagedBy = Prefer(incoming.ManagedBy, saved.ManagedBy);
        result.Birthdate = Prefer(incoming.Birthdate, saved.Birthdate);
        result.CreationDate =
            PFlowProfileDates.HasFullDate(saved.CreationDate)
            && !PFlowProfileDates.HasFullDate(incoming.CreationDate)
                ? saved.CreationDate
                : Prefer(incoming.CreationDate, saved.CreationDate);
        result.Location = Prefer(incoming.Location, saved.Location);
        result.Hometown = Prefer(incoming.Hometown, saved.Hometown);
        result.Work = Prefer(incoming.Work, saved.Work);
        result.Education = Prefer(incoming.Education, saved.Education);
        result.RelationshipStatus = Prefer(incoming.RelationshipStatus, saved.RelationshipStatus);
        result.Notifications = Prefer(incoming.Notifications, saved.Notifications);
        result.Reach = Prefer(incoming.Reach, saved.Reach);
        if (incoming.Followers > 0)
            result.Followers = incoming.Followers;
        return result;
    }
}
