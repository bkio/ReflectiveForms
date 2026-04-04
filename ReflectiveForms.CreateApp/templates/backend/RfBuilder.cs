using CrossCloudKit.Database.Basic;
using CrossCloudKit.File.Basic;
using CrossCloudKit.Memory.Basic;
using CrossCloudKit.PubSub.Basic;
using ReflectiveForms.Core;
using ReflectiveForms.Core.Endpoints;

public static class RfBuilder
{
    public static RfConfigurationBuilder Build(ILogger logger)
    {
        var pubSubService = new PubSubServiceBasic();
        var memoryService = new MemoryServiceBasic(pubSubService);
        var fileService = new FileServiceBasic(memoryService, pubSubService);
        var databaseService = new DatabaseServiceBasic("{{PROJECT_NAME}}-db", memoryService, Path.GetTempPath());

        return new RfConfigurationBuilder
        {
            Logger = logger,
            RootUserCredentials = new RootUserCredentials(
                "admin@karasoftware.com",
                Environment.GetEnvironmentVariable("ADMIN_PASSWORD") ?? "123456"),
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
                    EntityReadableNameSingular = "Note",
                    EntityReadableNamePlural = "Notes",
                    SupportsFrontendEdit = true,
                    HasAuthor = false,
                    HasTags = false,
                    HasCategories = false,
                    HasParentChildRelationship = false,
                    RequireGlobalTitleUniqueness = true,
                    OptionalTitleSanityCheck = null,
                }
            ]
        };
    }
}
