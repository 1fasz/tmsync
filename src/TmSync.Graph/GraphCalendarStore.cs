using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using TmSync.Core.Abstractions;
using TmSync.Core.Config;
using TmSync.Core.Models;
using CalendarDeltaResponse = Microsoft.Graph.Users.Item.CalendarView.Delta.DeltaGetResponse;

namespace TmSync.Graph;

/// <summary>
/// Syncs the user's default Office 365 calendar via Microsoft Graph calendarView delta queries
/// over a configurable rolling window.
/// </summary>
public sealed class GraphCalendarStore : IGraphModuleStore<CalendarItem>
{
    private const string PreferUtc = "outlook.timezone=\"UTC\", outlook.body-content-type=\"text\"";

    private readonly GraphServiceClient _client;
    private readonly TmSyncOptions _options;

    public GraphCalendarStore(GraphServiceClient client, TmSyncOptions options)
    {
        _client = client;
        _options = options;
    }

    public async Task<GraphDelta<CalendarItem>> GetChangesAsync(string mailbox, string? deltaToken, CancellationToken ct)
    {
        var changes = new List<GraphRecord<CalendarItem>>();
        string? nextToken = null;

        try
        {
            CalendarDeltaResponse? page;
            if (deltaToken is null)
            {
                var start = DateTime.UtcNow.AddDays(-_options.Calendar.PastDays);
                var end = DateTime.UtcNow.AddDays(_options.Calendar.FutureDays);
                page = await _client.Users[mailbox].CalendarView.Delta.GetAsDeltaGetResponseAsync(rc =>
                {
                    rc.QueryParameters.StartDateTime = start.ToString("o");
                    rc.QueryParameters.EndDateTime = end.ToString("o");
                    rc.Headers.Add("Prefer", PreferUtc);
                }, ct);
            }
            else
            {
                page = await _client.Users[mailbox].CalendarView.Delta.WithUrl(deltaToken)
                    .GetAsDeltaGetResponseAsync(rc => rc.Headers.Add("Prefer", PreferUtc), ct);
            }

            while (page != null)
            {
                foreach (var ev in page.Value ?? [])
                {
                    if (ev.Id is null) continue;
                    if (GraphHelpers.IsRemoved(ev) || ev.IsCancelled == true)
                    {
                        changes.Add(new GraphRecord<CalendarItem>(ev.Id, DateTime.UtcNow, true, null));
                        continue;
                    }
                    changes.Add(new GraphRecord<CalendarItem>(
                        ev.Id,
                        GraphHelpers.ToUtc(ev.LastModifiedDateTime),
                        false,
                        MapToItem(ev)));
                }

                if (page.OdataNextLink != null)
                {
                    page = await _client.Users[mailbox].CalendarView.Delta.WithUrl(page.OdataNextLink)
                        .GetAsDeltaGetResponseAsync(rc => rc.Headers.Add("Prefer", PreferUtc), ct);
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

        return new GraphDelta<CalendarItem>(changes, nextToken);
    }

    public async Task<string> CreateAsync(string mailbox, CalendarItem item, CancellationToken ct)
    {
        try
        {
            var created = await _client.Users[mailbox].Events.PostAsync(MapToEvent(item), cancellationToken: ct);
            return created!.Id!;
        }
        catch (ODataError e) { throw GraphHelpers.Translate(e); }
    }

    public async Task UpdateAsync(string mailbox, string graphId, CalendarItem item, CancellationToken ct)
    {
        try
        {
            await _client.Users[mailbox].Events[graphId].PatchAsync(MapToEvent(item), cancellationToken: ct);
        }
        catch (ODataError e) { throw GraphHelpers.Translate(e); }
    }

    public async Task DeleteAsync(string mailbox, string graphId, CancellationToken ct)
    {
        try
        {
            await _client.Users[mailbox].Events[graphId].DeleteAsync(cancellationToken: ct);
        }
        catch (ODataError e) when (e.ResponseStatusCode == 404)
        {
            // Already gone on the M365 side.
        }
        catch (ODataError e) { throw GraphHelpers.Translate(e); }
    }

    private static CalendarItem MapToItem(Event ev) => new()
    {
        Subject = ev.Subject,
        BodyText = ev.Body?.Content?.Trim(),
        Location = ev.Location?.DisplayName,
        StartUtc = GraphHelpers.ParseUtc(ev.Start),
        EndUtc = GraphHelpers.ParseUtc(ev.End),
        AllDay = ev.IsAllDay == true,
        ReminderMinutes = ev.IsReminderOn == true ? ev.ReminderMinutesBeforeStart : null
    };

    private static Event MapToEvent(CalendarItem item) => new()
    {
        Subject = item.Subject,
        Body = new ItemBody { ContentType = BodyType.Text, Content = item.BodyText ?? "" },
        Location = new Location { DisplayName = item.Location ?? "" },
        Start = GraphHelpers.ToGraphUtc(item.StartUtc),
        End = GraphHelpers.ToGraphUtc(item.EndUtc),
        IsAllDay = item.AllDay,
        IsReminderOn = item.ReminderMinutes.HasValue,
        ReminderMinutesBeforeStart = item.ReminderMinutes ?? 0
    };
}
