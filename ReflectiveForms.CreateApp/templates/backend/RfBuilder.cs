{{INFRA_USING_STATEMENTS}}
{{AI_USING_STATEMENTS}}
using ReflectiveForms.Core;
using ReflectiveForms.Core.Endpoints;

public static class RfBuilder
{
    public static RfConfigurationBuilder Build(ILogger logger)
    {
{{INFRA_SERVICE_INIT}}
{{AI_SERVICE_INIT}}

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
            // EditInactivityTimeoutMs = 600_000, // Lock timeout in ms (default: 10 min)
            EndpointConfiguration = new EndpointConfiguration
            {
                JwtSecret = Environment.GetEnvironmentVariable("JWT_SECRET") ?? "dev-secret-key-change-in-production-12345",
                RootPath = "/rf",
                PublicUrlRootForApi = Environment.GetEnvironmentVariable("API_PUBLIC_URL") ?? "http://localhost:{{BACKEND_PORT}}/rf/api/",
                PublicFrontendBaseUrl = Environment.GetEnvironmentVariable("FRONTEND_URL") ?? "http://localhost:{{FRONTEND_PORT}}",
                // OpenApi = new OpenApiConfiguration
                // {
                //     Title = "{{APP_NAME}} API",
                //     Version = "1.0.0",
                //     Description = "Auto-generated OpenAPI spec for {{APP_NAME}}.",
                //     ContactEmail = "admin@example.com"
                // },
                // SsoConfiguration = new SsoConfiguration
                // {
                //     Provider = SsoProvider.AzureAd,
                //     Authority = "https://login.microsoftonline.com/{tenant}/v2.0",
                //     ClientId = Environment.GetEnvironmentVariable("SSO_CLIENT_ID") ?? "",
                //     ClientSecret = Environment.GetEnvironmentVariable("SSO_CLIENT_SECRET") ?? "",
                //     AllowedDomains = new[] { "example.com" }
                // },
            },
{{AI_BUILDER_CONFIG}}
{{SHEETS_CONFIG}}
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
                    // ShowInNavigation = true, // Set false to hide from sidebar & dashboard
                    // HasIndividualSharing = false, // Per-entity access control (requires HasAuthor = true)
                    // CustomFrontendListRoute = null, // Custom sidebar link for sharing entities
                    // HooksSetup = new EntityOnChangedHooksSetup<NoteModel>
                    // {
                    //     PostCreateHook = (p, _) => { Console.WriteLine($"Created {p.NewId}"); return Task.CompletedTask; },
                    //     PostUpdateHook = (p, _) => { Console.WriteLine($"Updated {p.Id}"); return Task.CompletedTask; },
                    //     PostDeleteHook = (p, _) => { Console.WriteLine($"Deleted {p.Id}"); return Task.CompletedTask; },
                    // },
{{AI_ENTITY_FLAGS}}
                }
            ]
        };
    }
}
