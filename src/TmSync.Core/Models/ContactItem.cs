namespace TmSync.Core.Models;

/// <summary>Canonical representation of a contact shared by both sides of the sync.</summary>
public sealed class ContactItem
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Company { get; set; }
    public string? JobTitle { get; set; }
    public string? Email1 { get; set; }
    public string? Email2 { get; set; }
    public string? BusinessPhone { get; set; }
    public string? MobilePhone { get; set; }
    public string? HomePhone { get; set; }
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? Country { get; set; }
    public string? Notes { get; set; }
}
