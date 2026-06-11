using System.Collections.Concurrent;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using TmSync.Core.Abstractions;
using TmSync.Core.Models;
using GraphTaskStatus = Microsoft.Graph.Models.TaskStatus;
using TaskDeltaResponse = Microsoft.Graph.Users.Item.Todo.Lists.Item.Tasks.Delta.DeltaGetResponse;

namespace TmSync.Graph;

/// <summary>Syncs the user's default Microsoft To Do list via Microsoft Graph delta queries.</summary>
public sealed class GraphTaskStore : IGraphModuleStore<TaskItem>
{
    private readonly GraphServiceClient _client;
    private readonly ConcurrentDictionary<string, string> _defaultListIds = new(StringComparer.OrdinalIgnoreCase);

    public GraphTaskStore(GraphServiceClient client) => _client = client;

    private async Task<string> GetDefaultListIdAsync(string mailbox, CancellationToken ct)
    {
        if (_defaultListIds.TryGetValue(mailbox, out var cached)) return cached;

        var lists = await _client.Users[mailbox].Todo.Lists.GetAsync(rc => rc.QueryParameters.Top = 50, ct);
        var defaultList = lists?.Value?.FirstOrDefault(l => l.WellknownListName == WellknownListName.DefaultList)
                          ?? lists?.Value?.FirstOrDefault()
                          ?? throw new InvalidOperationException($"No To Do task list found for mailbox {mailbox}");

        _defaultListIds[mailbox] = defaultList.Id!;
        return defaultList.Id!;
    }

    public async Task<GraphDelta<TaskItem>> GetChangesAsync(string mailbox, string? deltaToken, CancellationToken ct)
    {
        var changes = new List<GraphRecord<TaskItem>>();
        string? nextToken = null;

        try
        {
            var listId = await GetDefaultListIdAsync(mailbox, ct);
            var builder = _client.Users[mailbox].Todo.Lists[listId].Tasks.Delta;

            TaskDeltaResponse? page = deltaToken is null
                ? await builder.GetAsDeltaGetResponseAsync(cancellationToken: ct)
                : await builder.WithUrl(deltaToken).GetAsDeltaGetResponseAsync(cancellationToken: ct);

            while (page != null)
            {
                foreach (var t in page.Value ?? [])
                {
                    if (t.Id is null) continue;
                    if (GraphHelpers.IsRemoved(t))
                    {
                        changes.Add(new GraphRecord<TaskItem>(t.Id, DateTime.UtcNow, true, null));
                        continue;
                    }
                    changes.Add(new GraphRecord<TaskItem>(
                        t.Id, GraphHelpers.ToUtc(t.LastModifiedDateTime), false, MapToItem(t)));
                }

                if (page.OdataNextLink != null)
                {
                    page = await builder.WithUrl(page.OdataNextLink).GetAsDeltaGetResponseAsync(cancellationToken: ct);
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

        return new GraphDelta<TaskItem>(changes, nextToken);
    }

    public async Task<string> CreateAsync(string mailbox, TaskItem item, CancellationToken ct)
    {
        try
        {
            var listId = await GetDefaultListIdAsync(mailbox, ct);
            var created = await _client.Users[mailbox].Todo.Lists[listId].Tasks.PostAsync(MapToTask(item), cancellationToken: ct);
            return created!.Id!;
        }
        catch (ODataError e) { throw GraphHelpers.Translate(e); }
    }

    public async Task UpdateAsync(string mailbox, string graphId, TaskItem item, CancellationToken ct)
    {
        try
        {
            var listId = await GetDefaultListIdAsync(mailbox, ct);
            await _client.Users[mailbox].Todo.Lists[listId].Tasks[graphId].PatchAsync(MapToTask(item), cancellationToken: ct);
        }
        catch (ODataError e) { throw GraphHelpers.Translate(e); }
    }

    public async Task DeleteAsync(string mailbox, string graphId, CancellationToken ct)
    {
        try
        {
            var listId = await GetDefaultListIdAsync(mailbox, ct);
            await _client.Users[mailbox].Todo.Lists[listId].Tasks[graphId].DeleteAsync(cancellationToken: ct);
        }
        catch (ODataError e) when (e.ResponseStatusCode == 404)
        {
        }
        catch (ODataError e) { throw GraphHelpers.Translate(e); }
    }

    private static TaskItem MapToItem(TodoTask t) => new()
    {
        Subject = t.Title,
        Notes = string.IsNullOrWhiteSpace(t.Body?.Content) ? null : t.Body.Content.Trim(),
        DueDateUtc = GraphHelpers.ParseUtcOrNull(t.DueDateTime),
        Completed = t.Status == GraphTaskStatus.Completed,
        CompletedUtc = GraphHelpers.ParseUtcOrNull(t.CompletedDateTime),
        Priority = t.Importance switch
        {
            Importance.Low => 0,
            Importance.High => 2,
            _ => 1
        }
    };

    private static TodoTask MapToTask(TaskItem item) => new()
    {
        Title = item.Subject,
        Body = new ItemBody { ContentType = BodyType.Text, Content = item.Notes ?? "" },
        DueDateTime = item.DueDateUtc is null ? null : GraphHelpers.ToGraphUtc(item.DueDateUtc.Value),
        Status = item.Completed ? GraphTaskStatus.Completed : GraphTaskStatus.NotStarted,
        CompletedDateTime = item.Completed && item.CompletedUtc != null ? GraphHelpers.ToGraphUtc(item.CompletedUtc.Value) : null,
        Importance = item.Priority switch
        {
            0 => Importance.Low,
            2 => Importance.High,
            _ => Importance.Normal
        }
    };
}
