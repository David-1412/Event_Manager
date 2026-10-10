using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace SportMeet.Application;

public static class DependencyInjection
{
    /// <summary>Composition root for the application layer. The Api never
    /// registers an application service by hand.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);
        services.AddScoped<Events.IEventService, Events.EventService>();
        services.AddScoped<Events.IAdminEventService, Events.AdminEventService>();

        // Ingestion services. Options *binding* happens in AddInfrastructure, which
        // is where IConfiguration is available - this layer only declares the
        // interfaces and their implementations.
        services.AddScoped<Ingestion.IEventDraftService, Ingestion.EventDraftService>();
        services.AddScoped<Ingestion.IEmailIngestionService, Ingestion.EmailIngestionService>();

        // The poll cycle's policy layer. It depends only on abstractions declared in this
        // layer (IEmailReader, IEmailIngestionService), so it composes here even though
        // the reader that satisfies it is an Infrastructure singleton registered there —
        // which is the direction the dependency rule requires.
        services.AddScoped<Ingestion.IEmailProcessor, Ingestion.EmailProcessor>();

        // Paste-to-event import: extract, geocode, flag, record.
        services.AddScoped<Imports.IImportService, Imports.ImportService>();

        // Administrator user management. The last-admin guard is the reason this is
        // a service rather than the controller calling its repository directly.
        services.AddScoped<Admin.IUserAdminService, Admin.UserAdminService>();
        services.AddScoped<Notifications.INotificationService, Notifications.NotificationService>();
        return services;
    }
}
