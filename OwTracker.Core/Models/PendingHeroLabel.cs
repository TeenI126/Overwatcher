namespace OwTracker.Core.Models;

/// <summary>
/// A hero portrait crop awaiting (or having received) manual review, used to grow the
/// self-refining classifier's training set. See design §6.6.
/// </summary>
public class PendingHeroLabel
{
    public int Id { get; set; }
    public string CropPath { get; set; } = string.Empty; // path to saved image crop
    public string PredictedHero { get; set; } = string.Empty;
    public float Confidence { get; set; }
    public string? ConfirmedHero { get; set; }            // null until reviewed
    public bool Reviewed { get; set; }
    public DateTime CapturedAt { get; set; }
}
