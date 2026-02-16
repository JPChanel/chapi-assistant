using System;

namespace Chapi.Infrastructure.Common;

public static class TimeHelper
{
    public static string GetRelativeDate(DateTime dateTime)
    {
        var timeSpan = DateTime.Now - dateTime;

        if (timeSpan <= TimeSpan.FromSeconds(60))
            return "just now";

        if (timeSpan <= TimeSpan.FromMinutes(60))
            return timeSpan.Minutes > 1 ? $"{timeSpan.Minutes} minutes ago" : "1 minute ago";

        if (timeSpan <= TimeSpan.FromHours(24))
            return timeSpan.Hours > 1 ? $"{timeSpan.Hours} hours ago" : "1 hour ago";

        if (timeSpan <= TimeSpan.FromDays(30))
            return timeSpan.Days > 1 ? $"{timeSpan.Days} days ago" : "1 day ago";

        if (timeSpan <= TimeSpan.FromDays(365))
            return timeSpan.Days > 30 ? $"{timeSpan.Days / 30} months ago" : "1 month ago";

        return timeSpan.Days > 365 ? $"{timeSpan.Days / 365} years ago" : "1 year ago";
    }
}
