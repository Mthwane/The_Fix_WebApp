namespace FashionFix.Web.Services.Audit;

/// <summary>Audit rows are stored in UTC; staff read them in South African Standard Time (UTC+2, no daylight saving).</summary>
public static class AuditTime
{
    public static readonly TimeSpan SastOffset = TimeSpan.FromHours(2);

    public static DateTime ToSast(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).Add(SastOffset);

    /// <summary>Shift start/open time for display, always in SAST (never the server's local zone). Today's shifts show just the
    /// time; a shift opened on an earlier SAST day also shows the date, so a shift left open overnight can't masquerade as
    /// "today at 09:36".</summary>
    public static string ShiftStamp(DateTime utc)
    {
        var sast = ToSast(utc);
        return sast.Date == ToSast(DateTime.UtcNow).Date ? sast.ToString("HH:mm") : sast.ToString("dd MMM HH:mm");
    }

    /// <summary>True when a shift was opened on an earlier SAST calendar day and is still open.</summary>
    public static bool IsStaleShift(DateTime openedUtc) => ToSast(openedUtc).Date < ToSast(DateTime.UtcNow).Date;

    /// <summary>A date typed in the filter bar (a SAST calendar day) -> the UTC instant that day starts.</summary>
    public static DateTime SastDayStartToUtc(DateTime sastDate) => sastDate.Date - SastOffset;
}
