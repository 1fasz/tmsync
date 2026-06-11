using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TmSync.Core.Abstractions;
using TmSync.Core.Config;
using TmSync.Core.Models;
using TmSync.Core.State;
using TmSync.Graph;
using TmSync.TimeMatters;

namespace TmSync.Service;

/// <summary>Composition root shared by the Windows service and the management CLI.</summary>
public static class TmSyncServices
{
    public static IServiceCollection AddTmSync(this IServiceCollection services, IConfiguration configuration)
    {
        var syncOptions = configuration.GetSection(TmSyncOptions.SectionName).Get<TmSyncOptions>() ?? new TmSyncOptions();
        var m365Options = configuration.GetSection(Microsoft365Options.SectionName).Get<Microsoft365Options>() ?? new Microsoft365Options();
        var tmOptions = configuration.GetSection(TimeMattersOptions.SectionName).Get<TimeMattersOptions>() ?? new TimeMattersOptions();

        services.AddSingleton(syncOptions);
        services.AddSingleton(m365Options);
        services.AddSingleton(tmOptions);

        services.AddSingleton<IStateStore>(_ => new StateStore(syncOptions.StateDatabasePath));
        services.AddSingleton<Microsoft.Extensions.Logging.ILoggerProvider, StateStoreLoggerProvider>();
        services.AddSingleton(_ => new TmDb(tmOptions.ConnectionString));
        services.AddSingleton(_ => TmGraphClientFactory.Create(m365Options));

        services.AddSingleton<ITmModuleStore<CalendarItem>, TmCalendarStore>();
        services.AddSingleton<ITmModuleStore<ContactItem>, TmContactStore>();
        services.AddSingleton<ITmModuleStore<TaskItem>, TmTaskStore>();
        services.AddSingleton<ITmEmailSink, TmEmailSink>();

        services.AddSingleton<IGraphModuleStore<CalendarItem>, GraphCalendarStore>();
        services.AddSingleton<IGraphModuleStore<ContactItem>, GraphContactStore>();
        services.AddSingleton<IGraphModuleStore<TaskItem>, GraphTaskStore>();
        services.AddSingleton<IGraphMailReader, GraphMailReader>();

        services.AddSingleton<SyncRunner>();
        return services;
    }
}
