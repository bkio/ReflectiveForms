using CrossCloudKit.Interfaces.Classes;
using ReflectiveForms.Core;
using ReflectiveForms.Core.Endpoints;

public static class RfBuilder
{
    public static RfConfigurationBuilder Build(ILogger logger)
    {
        var pubSubService = new PubSubServiceBasic();
        var memoryService = new MemoryServiceBasic(pubSubService);
        var fileService = new FileServiceBasic(Path.GetTempPath());
        var databaseService = new DatabaseServiceBasic("{{PROJECT_NAME}}-db", memoryService);

        return new RfConfigurationBuilder
        {
            Logger = logger,
            RootUserCredentials = new RootUserCredentials(
                "admin@example.com",
                Environment.GetEnvironmentVariable("ADMIN_PASSWORD") ?? "changeme123"),
            RepositoryServiceConfiguration = new EntityRepositoryServiceConfiguration(
                databaseService,
                memoryService,
                pubSubService,
                new FileServiceConfiguration(fileService, "{{PROJECT_NAME}}-media")),
            EndpointConfiguration = new EndpointConfiguration
            {
                JwtSecret = Environment.GetEnvironmentVariable("JWT_SECRET") ?? "dev-secret-key-change-in-production-12345",
                RootPath = "/rf",
                PublicUrlRootForApi = Environment.GetEnvironmentVariable("API_PUBLIC_URL") ?? "http://localhost:{{BACKEND_PORT}}/rf/api/",
                PublicFrontendBaseUrl = Environment.GetEnvironmentVariable("FRONTEND_URL") ?? "http://localhost:{{FRONTEND_PORT}}"
            },
            EntityTypes =
            [
                new EntityConfigurationBuilder<NoteModel>
                {
                    EntityName = "note",
                    ReadableEntityName = "Note",
                    PluralReadableEntityName = "Notes",
                    SupportsFrontendEdit = true,
                    RequireTitleUniqueness = true,
                }
            ]
        };
    }
}
