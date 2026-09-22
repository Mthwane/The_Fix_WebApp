using System.ComponentModel.DataAnnotations;

namespace FashionFix.Web.Models.Entities;

public enum ShiftStatus { Open, Closed }

/// <summary>
/// A single till operator's cash shift. Nothing in the schema modelled this before - every
/// "shift" was implicit and unaudited, so there was no way to reconcile expected vs actual
/// cash. Opened with a counted float, closed with a counted float; the expected close amount
/// is computed (see IDashboardService.GetShiftSectionAsync), never stored, so it can never
/// drift out of sync with the Orders it's derived from.
/// </summary>
public class ShiftSession
{
    [Key]
    public int ShiftSessionId { get; set; }

    public string UserId { get; set; } = string.Empty;
    public ApplicationUser User { get; set; } = null!;

    public decimal OpeningFloat { get; set; }
    public decimal? ClosingFloat { get; set; }

    public DateTime DateOpened { get; set; } = DateTime.UtcNow;
    public DateTime? DateClosed { get; set; }

    public ShiftStatus Status { get; set; } = ShiftStatus.Open;

    [MaxLength(500)]
    public string? Notes { get; set; }
}
