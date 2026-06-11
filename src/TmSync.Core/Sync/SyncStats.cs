namespace TmSync.Core.Sync;

public sealed class SyncStats
{
    public int CreatedInGraph { get; set; }
    public int CreatedInTm { get; set; }
    public int UpdatedInGraph { get; set; }
    public int UpdatedInTm { get; set; }
    public int DeletedInGraph { get; set; }
    public int DeletedInTm { get; set; }
    public int Matched { get; set; }
    public int Conflicts { get; set; }
    public int Errors { get; set; }

    public bool HasActivity =>
        CreatedInGraph + CreatedInTm + UpdatedInGraph + UpdatedInTm +
        DeletedInGraph + DeletedInTm + Matched + Conflicts + Errors > 0;

    public override string ToString() =>
        $"toM365: +{CreatedInGraph} ~{UpdatedInGraph} -{DeletedInGraph} | " +
        $"toTM: +{CreatedInTm} ~{UpdatedInTm} -{DeletedInTm} | " +
        $"matched: {Matched}, conflicts: {Conflicts}, errors: {Errors}";
}
