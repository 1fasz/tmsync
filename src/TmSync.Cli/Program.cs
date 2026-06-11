using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TmSync.Core.Abstractions;
using TmSync.Core.Models;
using TmSync.Service;

var configPath = Environment.GetEnvironmentVariable("TMSYNC_CONFIG")
                 ?? Path.Combine(AppContext.BaseDirectory, "appsettings.json");

var configuration = new ConfigurationBuilder()
    .AddJsonFile(configPath, optional: true)
    .AddEnvironmentVariables(prefix: "TMSYNC_")
    .Build();

var services = new ServiceCollection();
services.AddLogging(b => b.AddSimpleConsole(o => o.SingleLine = true).SetMinimumLevel(LogLevel.Information));
services.AddTmSync(configuration);
await using var provider = services.BuildServiceProvider();

try
{
    return await RunAsync(args, provider);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    return 1;
}

static async Task<int> RunAsync(string[] args, ServiceProvider provider)
{
    if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
    {
        PrintUsage();
        return 0;
    }

    var state = provider.GetRequiredService<IStateStore>();

    switch (args[0].ToLowerInvariant())
    {
        case "users":
            return RunUsers(args.Skip(1).ToArray(), state);

        case "sync":
        {
            string? user = GetOption(args, "--user");
            SyncModule? module = GetOption(args, "--module") switch
            {
                null => null,
                "calendar" => SyncModule.Calendar,
                "contacts" => SyncModule.Contacts,
                "tasks" => SyncModule.Tasks,
                "email" => SyncModule.EmailJournal,
                var m => throw new ArgumentException($"Unknown module '{m}'. Use calendar, contacts, tasks or email.")
            };

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

            var runner = provider.GetRequiredService<SyncRunner>();
            await runner.RunOnceAsync(user, module, cts.Token);
            Console.WriteLine("Sync pass complete.");
            return 0;
        }

        case "status":
        {
            var users = state.ListUsers();
            if (users.Count == 0)
            {
                Console.WriteLine("No sync users configured.");
                return 0;
            }
            foreach (var u in users)
            {
                Console.WriteLine($"{u.StaffCode} -> {u.Mailbox} [{(u.Enabled ? "enabled" : "DISABLED")}]");
                foreach (var moduleKey in new[] { "Calendar", "Contacts", "Tasks", "EmailJournal:inbox", "EmailJournal:sentitems" })
                {
                    var s = state.GetModuleState(moduleKey, u.StaffCode);
                    if (s.LastRunUtc != null)
                        Console.WriteLine($"    {moduleKey,-24} last run {s.LastRunUtc:u}");
                }
            }
            return 0;
        }

        default:
            Console.Error.WriteLine($"Unknown command '{args[0]}'.");
            PrintUsage();
            return 1;
    }
}

static int RunUsers(string[] args, IStateStore state)
{
    if (args.Length == 0)
    {
        PrintUsage();
        return 1;
    }

    switch (args[0].ToLowerInvariant())
    {
        case "list":
        {
            var users = state.ListUsers();
            if (users.Count == 0)
            {
                Console.WriteLine("No sync users configured.");
                return 0;
            }
            Console.WriteLine($"{"STAFF",-12} {"MAILBOX",-40} {"STATE",-9} CAL CON TASK MAIL");
            foreach (var u in users)
            {
                Console.WriteLine($"{u.StaffCode,-12} {u.Mailbox,-40} {(u.Enabled ? "enabled" : "disabled"),-9} " +
                                  $"{Flag(u.Calendar)}   {Flag(u.Contacts)}   {Flag(u.Tasks)}    {Flag(u.EmailJournal)}");
            }
            return 0;
        }

        case "add":
        {
            if (args.Length < 3)
            {
                Console.Error.WriteLine("Usage: tmsync users add <staffCode> <mailbox> [--no-calendar] [--no-contacts] [--no-tasks] [--email-journal]");
                return 1;
            }
            var user = new SyncUser(
                args[1], args[2],
                Enabled: true,
                Calendar: !args.Contains("--no-calendar"),
                Contacts: !args.Contains("--no-contacts"),
                Tasks: !args.Contains("--no-tasks"),
                EmailJournal: args.Contains("--email-journal"));
            state.AddOrUpdateUser(user);
            Console.WriteLine($"User '{user.StaffCode}' mapped to mailbox '{user.Mailbox}'.");
            return 0;
        }

        case "remove":
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("Usage: tmsync users remove <staffCode> [--purge]");
                return 1;
            }
            var purge = args.Contains("--purge");
            if (state.RemoveUser(args[1], purge))
            {
                Console.WriteLine($"User '{args[1]}' removed{(purge ? " (sync state purged)" : "")}. Existing records on both sides are left in place.");
                return 0;
            }
            Console.Error.WriteLine($"User '{args[1]}' not found.");
            return 1;
        }

        case "enable":
        case "disable":
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine($"Usage: tmsync users {args[0]} <staffCode>");
                return 1;
            }
            var existing = state.GetUser(args[1]);
            if (existing is null)
            {
                Console.Error.WriteLine($"User '{args[1]}' not found.");
                return 1;
            }
            state.AddOrUpdateUser(existing with { Enabled = args[0].Equals("enable", StringComparison.OrdinalIgnoreCase) });
            Console.WriteLine($"User '{existing.StaffCode}' {args[0].ToLowerInvariant()}d.");
            return 0;
        }

        default:
            Console.Error.WriteLine($"Unknown users subcommand '{args[0]}'.");
            PrintUsage();
            return 1;
    }
}

static string Flag(bool value) => value ? "Y" : "-";

static string? GetOption(string[] args, string name)
{
    for (var i = 0; i < args.Length - 1; i++)
        if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
            return args[i + 1];
    return null;
}

static void PrintUsage()
{
    Console.WriteLine("""
        TmSync - Time Matters <-> Office 365 sync (Microsoft Graph)

        Usage:
          tmsync users list
          tmsync users add <staffCode> <mailbox> [--no-calendar] [--no-contacts] [--no-tasks] [--email-journal]
          tmsync users remove <staffCode> [--purge]
          tmsync users enable <staffCode>
          tmsync users disable <staffCode>
          tmsync sync [--user <staffCode>] [--module calendar|contacts|tasks|email]
          tmsync status

        Configuration is read from appsettings.json next to the executable,
        or the file pointed to by the TMSYNC_CONFIG environment variable.
        """);
}
