// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using CrossCloudKit.Database.Basic;
using CrossCloudKit.File.Basic;
using CrossCloudKit.Memory.Basic;
using CrossCloudKit.PubSub.Basic;
using ReflectiveForms.Core;
using ReflectiveForms.Core.Endpoints;

namespace ReflectiveForms.Sample1;

public static class RfBuilder
{
    public static RfConfigurationBuilder Build(ILogger logger)
    {
        var pubSubService = new PubSubServiceBasic();
        var memoryService = new MemoryServiceBasic(pubSubService);
        var fileService = new FileServiceBasic(memoryService, pubSubService);
        var dbService = new DatabaseServiceBasic("reflective-forms-tests-1", memoryService, Path.GetTempPath());

        return new RfConfigurationBuilder
        {
            Logger = logger,
            RootUserCredentials = new RootUserCredentials("admin@karasoftware.com", "123456"),
            RepositoryServiceConfiguration = new EntityRepositoryServiceConfiguration(
                dbService,
                memoryService,
                pubSubService,
                new FileServiceConfiguration(fileService, "reflective-forms-media")),
            EndpointConfiguration = new EndpointConfiguration
            {
                JwtSecret = "my-awesome-secret-key-1234567890",
                PublicBaseUrl = "http://localhost:9000",
                ReflectiveFormsEndpointPathPrefix = "/rf",
                PublicPathForApiProxy = "/rf-api/",
                PublicPathForFrontendProxy = "/"
            },
            EntityTypes =
            [
                new EntityConfigurationBuilder<RfObjectiveExampleModel>
                {
                    EntityName = "objective",
                    EntityReadableNamePlural = "Objectives",
                    EntityReadableNameSingular = "Objective",
                    ShallSupportFrontendEdit = SupportsFrontendEdit.ForAllAuthorized,
                    HasAuthor = true,
                    HasTags = true,
                    HasCategories = true,
                    HasParentChildRelationship = true,
                    RequireGlobalTitleUniqueness = true,
                    OptionalTitleSanityCheck = async title => await Task.FromResult(title.Text != "Forbidden title example"),
                    HooksSetup = new EntityOnChangedHooksSetup<RfObjectiveExampleModel>
                    {
                        PostCreateHook = (p, _) =>
                        {
                            logger.LogInformation($"{p.EntityName}({p.NewId}) - created");
                            return Task.CompletedTask;
                        },
                        PostUpdateHook = (p, _) =>
                        {
                            logger.LogInformation($"{p.EntityName}({p.Id}) - updated");
                            return Task.CompletedTask;
                        },
                        PostDeleteHook = (p, _) =>
                        {
                            logger.LogInformation($"{p.EntityName}({p.Id}) - deleted");
                            return Task.CompletedTask;
                        }
                    }
                }
            ]
        };
    }
}
