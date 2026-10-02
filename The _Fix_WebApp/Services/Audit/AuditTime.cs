namespace FashionFix.Web.Services.Audit;

/// <summary>Audit rows are stored in UTC; staff read them in South African Standard Time (UTC+2, no daylight saving).</summary>
public static class AuditTime
{
    public static readonly TimeSpan SastOffset = TimeSpan.FromHours(2);

    public static DateTime ToSast(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).Add(SastOffset);

    /// <summary>A date typed in the filter bar (a SAST calendar day) -> the UTC instant that day starts.</summary>
    public static DateTime SastDayStartToUtc(DateTime sastDate) => sastDate.Date - SastOffset;
}
