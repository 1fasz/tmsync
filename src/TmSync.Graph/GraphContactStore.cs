using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using TmSync.Core.Abstractions;
using TmSync.Core.Models;
using ContactDeltaResponse = Microsoft.Graph.Users.Item.Contacts.Delta.DeltaGetResponse;

namespace TmSync.Graph;

/// <summary>Syncs the user's default Office 365 contacts folder via Microsoft Graph delta queries.</summary>
public sealed class GraphContactStore : IGraphModuleStore<ContactItem>
{
    private readonly GraphServiceClient _client;

    public GraphContactStore(GraphServiceClient client) => _client = client;

    public async Task<GraphDelta<ContactItem>> GetChangesAsync(string mailbox, string? deltaToken, CancellationToken ct)
    {
        var changes = new List<GraphRecord<ContactItem>>();
        string? nextToken = null;

        try
        {
            ContactDeltaResponse? page = deltaToken is null
                ? await _client.Users[mailbox].Contacts.Delta.GetAsDeltaGetResponseAsync(cancellationToken: ct)
                : await _client.Users[mailbox].Contacts.Delta.WithUrl(deltaToken).GetAsDeltaGetResponseAsync(cancellationToken: ct);

            while (page != null)
            {
                foreach (var c in page.Value ?? [])
                {
                    if (c.Id is null) continue;
                    if (GraphHelpers.IsRemoved(c))
                    {
                        changes.Add(new GraphRecord<ContactItem>(c.Id, DateTime.UtcNow, true, null));
                        continue;
                    }
                    changes.Add(new GraphRecord<ContactItem>(
                        c.Id, GraphHelpers.ToUtc(c.LastModifiedDateTime), false, MapToItem(c)));
                }

                if (page.OdataNextLink != null)
                {
                    page = await _client.Users[mailbox].Contacts.Delta.WithUrl(page.OdataNextLink)
                        .GetAsDeltaGetResponseAsync(cancellationToken: ct);
                }
                else
                {
                    nextToken = page.OdataDeltaLink;
                    page = null;
                }
            }
        }
        catch (ODataError e)
        {
            throw GraphHelpers.Translate(e);
        }

        return new GraphDelta<ContactItem>(changes, nextToken);
    }

    public async Task<string> CreateAsync(string mailbox, ContactItem item, CancellationToken ct)
    {
        try
        {
            var created = await _client.Users[mailbox].Contacts.PostAsync(MapToContact(item), cancellationToken: ct);
            return created!.Id!;
        }
        catch (ODataError e) { throw GraphHelpers.Translate(e); }
    }

    public async Task UpdateAsync(string mailbox, string graphId, ContactItem item, CancellationToken ct)
    {
        try
        {
            await _client.Users[mailbox].Contacts[graphId].PatchAsync(MapToContact(item), cancellationToken: ct);
        }
        catch (ODataError e) { throw GraphHelpers.Translate(e); }
    }

    public async Task DeleteAsync(string mailbox, string graphId, CancellationToken ct)
    {
        try
        {
            await _client.Users[mailbox].Contacts[graphId].DeleteAsync(cancellationToken: ct);
        }
        catch (ODataError e) when (e.ResponseStatusCode == 404)
        {
        }
        catch (ODataError e) { throw GraphHelpers.Translate(e); }
    }

    private static ContactItem MapToItem(Contact c) => new()
    {
        FirstName = c.GivenName,
        LastName = c.Surname,
        Company = c.CompanyName,
        JobTitle = c.JobTitle,
        Email1 = c.EmailAddresses?.ElementAtOrDefault(0)?.Address,
        Email2 = c.EmailAddresses?.ElementAtOrDefault(1)?.Address,
        BusinessPhone = c.BusinessPhones?.FirstOrDefault(),
        MobilePhone = c.MobilePhone,
        HomePhone = c.HomePhones?.FirstOrDefault(),
        Street = c.BusinessAddress?.Street,
        City = c.BusinessAddress?.City,
        State = c.BusinessAddress?.State,
        PostalCode = c.BusinessAddress?.PostalCode,
        Country = c.BusinessAddress?.CountryOrRegion,
        Notes = c.PersonalNotes
    };

    private static Contact MapToContact(ContactItem item)
    {
        var emails = new List<EmailAddress>();
        if (!string.IsNullOrWhiteSpace(item.Email1)) emails.Add(new EmailAddress { Address = item.Email1 });
        if (!string.IsNullOrWhiteSpace(item.Email2)) emails.Add(new EmailAddress { Address = item.Email2 });

        return new Contact
        {
            GivenName = item.FirstName,
            Surname = item.LastName,
            CompanyName = item.Company,
            JobTitle = item.JobTitle,
            EmailAddresses = emails,
            BusinessPhones = string.IsNullOrWhiteSpace(item.BusinessPhone) ? [] : [item.BusinessPhone],
            MobilePhone = item.MobilePhone,
            HomePhones = string.IsNullOrWhiteSpace(item.HomePhone) ? [] : [item.HomePhone],
            BusinessAddress = new PhysicalAddress
            {
                Street = item.Street,
                City = item.City,
                State = item.State,
                PostalCode = item.PostalCode,
                CountryOrRegion = item.Country
            },
            PersonalNotes = item.Notes
        };
    }
}
