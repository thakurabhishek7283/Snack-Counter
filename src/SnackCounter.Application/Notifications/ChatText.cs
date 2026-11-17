using System.Globalization;
using SnackCounter.Application.Abstractions;
using SnackCounter.Domain.Common;
using SnackCounter.Domain.Organisation;

namespace SnackCounter.Application.Notifications;

internal static class ChatText
{
    public const string AcceptPrefix = "accept:";
    public const string DeclinePrefix = "decline:";

    public static string LocalTime(DateTimeOffset instant, Office office) =>
        TimeZoneInfo.ConvertTime(instant, office.TimeZone).ToString("ddd dd MMM HH:mm", CultureInfo.InvariantCulture);

    public static ChatAction Accept(RefillId refill) => new("I'll buy it", AcceptPrefix + refill);
    public static ChatAction Decline(RefillId refill) => new("Can't this time", DeclinePrefix + refill);
}
