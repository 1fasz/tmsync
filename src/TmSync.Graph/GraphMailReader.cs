using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using TmSync.Core.Abstractions;
using TmSync.Core.Models;
using MessageDeltaResponse = Microsoft.Graph.Users.Item.MailFolders.Item.Messages.Delta.DeltaGetResponse;

namespace TmSync.Graph;

/// <summary>Reads new mail from a mailbox folder (read-only) for email journaling.</summary>
public sealed class GraphMailReader : IGraphMailReader
{
    private const string PreferText = "outlook.body-content-type=\"text\"";

    private readonly GraphServiceClient _client;

    public GraphMailReader(GraphServiceClient client) => _client = client;

    public async Task<GraphDelta<JournalEmail>> GetNewMessagesAsync(string mailbox, string folder, string? deltaToken, CancellationToken ct)
    {
        var changes = new List<GraphRecord<JournalEmail>>();
        string? nextToken = null;

        try
        {
            var builder = _client.Users[mailbox].MailFolders[folder].Messages.Delta;

            MessageDeltaResponse? page = deltaToken is null
                ? await builder.GetAsDeltaGetResponseAsync(rc =>
                {
                    rc.QueryParameters.Select = ["subject", "from", "toRecipients", "ccRecipients", "sentDateTime", "internetMessageId", "body"];
                    rc.Headers.Add("Prefer", PreferText);
                }, ct)
                : await builder.WithUrl(deltaToken).GetAsDeltaGetResponseAsync(rc => rc.Headers.Add("Prefer", PreferText), ct);

            while (page != null)
            {
                foreach (var m in page.Value ?? [])
                {
                    if (m.Id is null || GraphHelpers.IsRemoved(m)) continue;
                    changes.Add(new GraphRecord<JournalEmail>(
                        m.Id, GraphHelpers.ToUtc(m.SentDateTime), false, MapToItem(m)));
                }

                if (page.OdataNextLink != null)
                {
                    page = await builder.WithUrl(page.OdataNextLink)
                        .GetAsDeltaGetResponseAsync(rc => rc.Headers.Add("Prefer", PreferText), ct);
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

        return new GraphDelta<JournalEmail>(changes, nextToken);
    }

    private static JournalEmail MapToItem(Message m) => new()
    {
        InternetMessageId = m.InternetMessageId,
        Subject = m.Subject,
        From = m.From?.EmailAddress?.Address,
        To = JoinAddresses(m.ToRecipients),
        Cc = JoinAddresses(m.CcRecipients),
        SentUtc = GraphHelpers.ToUtc(m.SentDateTime),
        BodyText = m.Body?.Content
    };

    private static string? JoinAddresses(List<Recipient>? recipients)
    {
        var addresses = recipients?
            .Select(r => r.EmailAddress?.Address)
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .ToList();
        return addresses is { Count: > 0 } ? string.Join("; ", addresses) : null;
    }
}
