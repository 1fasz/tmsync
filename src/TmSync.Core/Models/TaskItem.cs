namespace TmSync.Core.Models;

/// <summary>Canonical representation of a task/ToDo shared by both sides of the sync.</summary>
public sealed class TaskItem
{
    public string? Subject { get; set; }
    public string? Notes { get; set; }
    public DateTime? DueDateUtc { get; set; }
    public bool Completed { get; set; }
    public DateTime? CompletedUtc { get; set; }

    /// <summary>0 = low, 1 = normal, 2 = high.</summary>
    public int Priority { get; set; } = 1;
}
