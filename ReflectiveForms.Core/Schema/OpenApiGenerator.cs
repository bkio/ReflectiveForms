// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReflectiveForms.Core.Ai;
using ReflectiveForms.Core.Attributes;
using ReflectiveForms.Core.Attributes.Fields;
using ReflectiveForms.Core.Enums;
using ReflectiveForms.Core.Models;
using Range = ReflectiveForms.Core.Attributes.Fields.Range;

namespace ReflectiveForms.Core.Schema;

/// <summary>
/// Generates an OpenAPI 3.1 specification from entity registrations and endpoint definitions.
/// </summary>
internal static class OpenApiGenerator
{
    private static JObject? _cachedSpec;

    /// <summary>
    /// Generates the complete OpenAPI 3.1 spec. Result is cached in-memory (schemas don't change at runtime).
    /// </summary>
    internal static JObject Generate()
    {
        if (_cachedSpec != null)
            return _cachedSpec;

        var openApiConfig = RfConfiguration.EndpointConfiguration.OpenApi!;
        var apiBaseUrl = RfConfiguration.EndpointConfiguration.PublicUrlRootForApi.TrimEnd('/');

        var spec = new JObject
        {
            ["openapi"] = "3.1.0",
            ["info"] = BuildInfo(openApiConfig),
            ["servers"] = new JArray { new JObject { ["url"] = apiBaseUrl } },
            ["paths"] = BuildPaths(openApiConfig),
            ["components"] = BuildComponents(openApiConfig)
        };

        _cachedSpec = spec;
        return spec;
    }

    private static JObject BuildInfo(OpenApiConfiguration config)
    {
        var info = new JObject
        {
            ["title"] = config.Title,
            ["version"] = config.Version
        };
        if (config.Description != null)
            info["description"] = config.Description;
        if (config.ContactEmail != null)
            info["contact"] = new JObject { ["email"] = config.ContactEmail };
        return info;
    }

    private static JObject BuildPaths(OpenApiConfiguration config)
    {
        var paths = new JObject();

        // Entity CRUD paths
        foreach (var (entityName, configBase) in RfConfiguration.EntityNameToConfiguration)
        {
            var ec = configBase.EntityConfiguration;
            var readableSingular = ec.EntityReadableNameSingular;

            AddCrudPaths(paths, entityName, readableSingular);

            // Sanity check
            paths[$"/sanity_check?type={entityName}"] = new JObject
            {
                ["post"] = BuildOperation(
                    $"Validate {readableSingular}",
                    $"Run sanity checks on {readableSingular} data",
                    $"validate_{entityName}",
                    RequestBodyRef($"{entityName}_fields"),
                    ResponseRef("sanity_check_response"),
                    requiresAuth: true)
            };

            // Schema (single entity)
            if (config.IncludeSchemaEndpoints)
            {
                paths[$"/schema?type={entityName}"] = new JObject
                {
                    ["get"] = BuildOperation(
                        $"Get {readableSingular} schema",
                        $"Returns the JSON schema for {readableSingular}",
                        $"get_schema_{entityName}",
                        requestBodyRef: null,
                        ResponseRef("entity_schema"),
                        requiresAuth: false)
                };
            }
        }

        // Static paths
        if (config.IncludeSchemaEndpoints)
        {
            paths["/schema"] = new JObject
            {
                ["get"] = BuildOperation("Get all schemas", "Returns JSON schemas for all entity types",
                    "get_all_schemas", null, ResponseObject(new JObject { ["type"] = "object" }), false)
            };
        }

        if (config.IncludeAuthEndpoints)
        {
            paths["/login"] = new JObject
            {
                ["post"] = BuildOperation("Login", "Authenticate with email and password", "login",
                    RequestBodyInline(new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject
                        {
                            ["email"] = new JObject { ["type"] = "string", ["format"] = "email" },
                            ["password"] = new JObject { ["type"] = "string" }
                        },
                        ["required"] = new JArray("email", "password")
                    }),
                    ResponseObject(new JObject { ["type"] = "object", ["properties"] = new JObject { ["token"] = new JObject { ["type"] = "string" } } }),
                    false)
            };

            paths["/logout"] = new JObject
            {
                ["post"] = BuildOperation("Logout", "Clear session", "logout", null,
                    ResponseObject(new JObject { ["type"] = "object" }), true)
            };

            paths["/auth_check"] = new JObject
            {
                ["post"] = BuildOperation("Check authentication", "Verify authentication status", "auth_check",
                    null, ResponseObject(new JObject { ["type"] = "object" }), true)
            };

            paths["/capabilities"] = new JObject
            {
                ["post"] = BuildOperation("Get capabilities", "Get user capabilities per entity type", "get_capabilities",
                    null, ResponseObject(new JObject { ["type"] = "object" }), true)
            };
        }

        if (config.IncludeMediaEndpoints)
        {
            paths["/media"] = new JObject
            {
                ["post"] = BuildOperation("Upload media", "Upload media file", "upload_media",
                    RequestBodyInline(new JObject { ["type"] = "string", ["format"] = "binary" }),
                    ResponseObject(new JObject { ["type"] = "object" }), true),
                ["get"] = BuildOperation("Download media", "Download media file by entity/id", "download_media",
                    null, ResponseObject(new JObject { ["type"] = "string", ["format"] = "binary" }), false)
            };
        }

        paths["/bulk_read"] = new JObject
        {
            ["post"] = BuildOperation("Bulk read", "Fetch multiple entities with optional field filtering", "bulk_read",
                RequestBodyInline(new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["requests"] = new JObject
                        {
                            ["type"] = "array",
                            ["items"] = new JObject
                            {
                                ["type"] = "object",
                                ["properties"] = new JObject
                                {
                                    ["type"] = new JObject { ["type"] = "string" },
                                    ["id"] = new JObject { ["type"] = "integer" },
                                    ["fields"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "string" } }
                                }
                            }
                        }
                    }
                }),
                ResponseObject(new JObject { ["type"] = "object" }), true)
        };

        paths["/entity_lock_control"] = new JObject
        {
            ["post"] = BuildOperation("Entity lock control", "Lock/unlock/heartbeat for entity editing", "entity_lock_control",
                null, ResponseObject(new JObject { ["type"] = "object" }), true),
            ["get"] = BuildOperation("Get locked entities", "List all locked entities", "get_locked_entities",
                null, ResponseObject(new JObject { ["type"] = "object" }), true)
        };

        // AI endpoints
        if (RfConfiguration.AiServiceConfiguration != null && config.IncludeAiEndpoints)
        {
            AddAiPaths(paths);
        }

        return paths;
    }

    private static void AddCrudPaths(JObject paths, string entityName, string readableName)
    {
        var operations = new[] { "CREATE", "READ", "UPDATE", "DELETE", "PEEK_ALL", "PEEK_ALL_PAGINATED", "HISTORY", "SHARING_CANDIDATES" };

        foreach (var op in operations)
        {
            var pathKey = op == "PEEK_ALL_PAGINATED"
                ? $"/crud?operation={op}&type={entityName}&page_size={{n}}"
                : $"/crud?operation={op}&type={entityName}";

            var (summary, description, requestBody, responseSchema) = op switch
            {
                "CREATE" => ($"Create {readableName}", $"Create a new {readableName}", RequestBodyRef($"{entityName}_fields"), ResponseRef($"{entityName}_entity")),
                "READ" => ($"Read {readableName}", $"Read a {readableName} by ID", RequestBodyInline(IdSchema()), ResponseRef($"{entityName}_entity")),
                "UPDATE" => ($"Update {readableName}", $"Update a {readableName}", RequestBodyRef($"{entityName}_fields"), ResponseRef($"{entityName}_entity")),
                "DELETE" => ($"Delete {readableName}", $"Delete a {readableName} by ID", RequestBodyInline(IdSchema()), ResponseRef($"{entityName}_entity")),
                "PEEK_ALL" => ($"List all {readableName}", $"List all {readableName} entities", RequestBodyInline(new JObject { ["type"] = "object" }), ResponseObject(new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "object" } })),
                "PEEK_ALL_PAGINATED" => ($"List {readableName} (paginated)", $"List {readableName} with pagination", RequestBodyInline(new JObject { ["type"] = "object" }), ResponseObject(new JObject { ["type"] = "object" })),
                "HISTORY" => ($"{readableName} history", $"Get revision history for {readableName}", RequestBodyInline(IdSchema()), ResponseObject(new JObject { ["type"] = "object" })),
                "SHARING_CANDIDATES" => ($"{readableName} sharing candidates", $"Get users/roles eligible for sharing", RequestBodyInline(new JObject { ["type"] = "object" }), ResponseObject(new JObject { ["type"] = "object" })),
                _ => ($"{op} {readableName}", $"{op} operation", (JObject?)null, ResponseObject(new JObject { ["type"] = "object" }))
            };

            paths[pathKey] = new JObject
            {
                ["post"] = BuildOperation(summary, description, $"{op.ToLowerInvariant()}_{entityName}", requestBody, responseSchema, true)
            };
        }
    }

    private static void AddAiPaths(JObject paths)
    {
        paths["/ai/semantic_search"] = new JObject
        {
            ["post"] = BuildOperation("Semantic search", "Search entities by natural language query", "ai_semantic_search",
                RequestBodyInline(new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["query"] = new JObject { ["type"] = "string" },
                        ["entity_name"] = new JObject { ["type"] = "string" },
                        ["top_k"] = new JObject { ["type"] = "integer", ["default"] = 10 }
                    },
                    ["required"] = new JArray("query")
                }),
                ResponseObject(new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["results"] = new JObject
                        {
                            ["type"] = "array",
                            ["items"] = new JObject
                            {
                                ["type"] = "object",
                                ["properties"] = new JObject
                                {
                                    ["entity_name"] = new JObject { ["type"] = "string" },
                                    ["entity_id"] = new JObject { ["type"] = "integer" },
                                    ["title"] = new JObject { ["type"] = "string" },
                                    ["score"] = new JObject { ["type"] = "number" }
                                }
                            }
                        }
                    }
                }), true)
        };

        paths["/ai/generate"] = new JObject
        {
            ["post"] = BuildOperation("Generate entity with AI", "Create entity from natural language", "ai_generate",
                RequestBodyInline(new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["entity_name"] = new JObject { ["type"] = "string" },
                        ["prompt"] = new JObject { ["type"] = "string" }
                    },
                    ["required"] = new JArray("entity_name", "prompt")
                }),
                ResponseObject(new JObject { ["type"] = "object", ["description"] = "Partial entity data (draft, not saved)" }), true)
        };

        paths["/ai/suggest"] = new JObject
        {
            ["post"] = BuildOperation("AI field suggestion", "Get AI suggestion for a field value", "ai_suggest",
                RequestBodyInline(new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["entity_name"] = new JObject { ["type"] = "string" },
                        ["target_field"] = new JObject { ["type"] = "string" },
                        ["current_fields"] = new JObject { ["type"] = "object" }
                    },
                    ["required"] = new JArray("entity_name", "target_field", "current_fields")
                }),
                ResponseObject(new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject { ["suggestion"] = new JObject { ["type"] = "string" } }
                }), true)
        };

        paths["/ai/sanity_check"] = new JObject
        {
            ["post"] = BuildOperation("AI sanity check", "Run AI-powered validation on a field", "ai_sanity_check",
                RequestBodyInline(new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["entity_name"] = new JObject { ["type"] = "string" },
                        ["field_name"] = new JObject { ["type"] = "string" },
                        ["field_value"] = new JObject { }
                    },
                    ["required"] = new JArray("entity_name", "field_name", "field_value")
                }),
                ResponseObject(new JObject { ["type"] = "object" }), true)
        };

        paths["/ai/diff_summary"] = new JObject
        {
            ["post"] = BuildOperation("AI diff summary", "Get AI summary of revision changes", "ai_diff_summary",
                RequestBodyInline(new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["entity_name"] = new JObject { ["type"] = "string" },
                        ["entity_id"] = new JObject { ["type"] = "integer" },
                        ["revision_index"] = new JObject { ["type"] = "integer" }
                    },
                    ["required"] = new JArray("entity_name", "entity_id", "revision_index")
                }),
                ResponseObject(new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject { ["summary"] = new JObject { ["type"] = "string" } }
                }), true)
        };

        paths["/ai/nl_filter"] = new JObject
        {
            ["post"] = BuildOperation("Natural language filter", "Filter entities using natural language", "ai_nl_filter",
                RequestBodyInline(new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["entity_name"] = new JObject { ["type"] = "string" },
                        ["query"] = new JObject { ["type"] = "string" }
                    },
                    ["required"] = new JArray("entity_name", "query")
                }),
                ResponseObject(new JObject { ["type"] = "object" }), true)
        };

        paths["/ai/relation_suggest"] = new JObject
        {
            ["post"] = BuildOperation("AI relation suggestions", "Get AI-suggested related entities", "ai_relation_suggest",
                RequestBodyInline(new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["entity_name"] = new JObject { ["type"] = "string" },
                        ["relation_field"] = new JObject { ["type"] = "string" },
                        ["current_text"] = new JObject { ["type"] = "string" }
                    },
                    ["required"] = new JArray("entity_name", "relation_field", "current_text")
                }),
                ResponseObject(new JObject { ["type"] = "object" }), true)
        };

        paths["/ai/reindex"] = new JObject
        {
            ["post"] = BuildOperation("Reindex vectors", "Re-embed and reindex entity vectors (root user only)", "ai_reindex",
                RequestBodyInline(new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["entity_name"] = new JObject { ["type"] = "string" },
                        ["mode"] = new JObject { ["type"] = "string", ["enum"] = new JArray("full", "incremental") }
                    },
                    ["required"] = new JArray("entity_name")
                }),
                ResponseObject(new JObject { ["type"] = "object" }), true)
        };
    }

    private static JObject BuildComponents(OpenApiConfiguration config)
    {
        var schemas = new JObject();

        // Entity field schemas
        foreach (var (entityName, configBase) in RfConfiguration.EntityNameToConfiguration)
        {
            var ec = configBase.EntityConfiguration;
            var fieldsModelType = ec.EntityFieldsModelType;

            // Fields schema
            var fieldsSchema = BuildFieldsSchemaFromType(fieldsModelType, config.IncludeRfExtensions);
            schemas[$"{entityName}_fields"] = fieldsSchema;

            // Entity wrapper schema
            var entitySchema = BuildEntityWrapperSchema(entityName, ec);
            schemas[$"{entityName}_entity"] = entitySchema;
        }

        // Common schemas
        schemas["sanity_check_response"] = new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["message"] = new JObject { ["type"] = "string" }
            }
        };

        schemas["entity_schema"] = new JObject
        {
            ["type"] = "object",
            ["description"] = "Entity schema definition"
        };

        var components = new JObject
        {
            ["schemas"] = schemas,
            ["securitySchemes"] = new JObject
            {
                ["bearerAuth"] = new JObject
                {
                    ["type"] = "http",
                    ["scheme"] = "bearer",
                    ["bearerFormat"] = "JWT"
                },
                ["cookieAuth"] = new JObject
                {
                    ["type"] = "apiKey",
                    ["in"] = "cookie",
                    ["name"] = RfConfiguration.EndpointConfiguration.AuthCookieName ?? "rf-auth-token"
                }
            }
        };

        return components;
    }

    private static JObject BuildFieldsSchemaFromType(Type modelType, bool includeExtensions)
    {
        var properties = new JObject();
        var requiredFields = new JArray();

        var fields = modelType.GetFields(BindingFlags.Instance | BindingFlags.Public);
        var props = modelType.GetProperties(BindingFlags.Instance | BindingFlags.Public);

        foreach (var member in fields.Cast<MemberInfo>().Concat(props))
        {
            var fieldAttr = member.GetCustomAttribute<Field>();
            if (fieldAttr == null) continue;

            var jsonProp = member.GetCustomAttribute<JsonPropertyAttribute>();
            var name = jsonProp?.PropertyName ?? member.Name;

            var fieldSchema = MapFieldToJsonSchema(member, fieldAttr, modelType, includeExtensions);
            properties[name] = fieldSchema;

            if (IsFieldRequired(fieldAttr))
                requiredFields.Add(name);
        }

        var schema = new JObject
        {
            ["type"] = "object",
            ["properties"] = properties
        };
        if (requiredFields.Count > 0)
            schema["required"] = requiredFields;

        return schema;
    }

    private static JObject MapFieldToJsonSchema(MemberInfo member, Field fieldAttr, Type parentType, bool includeExtensions)
    {
        var schema = fieldAttr.Type switch
        {
            FieldType.Text or FieldType.TextArea or FieldType.Email or FieldType.Url =>
                new JObject { ["type"] = "string" },

            FieldType.WysiwygEditor =>
                new JObject { ["type"] = "string", ["description"] = "HTML content" },

            FieldType.Number =>
                BuildNumberSchema(fieldAttr),

            FieldType.Range =>
                BuildRangeSchema(fieldAttr),

            FieldType.Checkbox =>
                new JObject { ["type"] = "boolean" },

            FieldType.DatePicker =>
                new JObject { ["type"] = "string", ["format"] = "date-time" },

            FieldType.Select =>
                BuildSelectSchema(fieldAttr),

            FieldType.Relation =>
                BuildRelationSchema(fieldAttr),

            FieldType.MediaSourceBase64 =>
                new JObject { ["type"] = "string", ["description"] = "Base64-encoded image" },

            FieldType.Group =>
                BuildGroupSchema(member, parentType, includeExtensions),

            FieldType.Repeater =>
                BuildRepeaterSchema(member, parentType, includeExtensions),

            _ => new JObject { ["type"] = "string" }
        };

        if (includeExtensions)
        {
            var displayCondition = member.GetCustomAttribute<DisplayCondition>();
            if (displayCondition != null)
                schema["x-rf-display-condition"] = displayCondition.Condition;

            schema["x-rf-instructions"] = fieldAttr.Instructions;

            // Check for dynamic methods
            var memberName = member.Name;
            if (parentType.GetMethod($"{memberName}___DynamicChoicesRuntimeAsync") != null)
                schema["x-rf-has-dynamic-choices-runtime"] = true;
            if (parentType.GetMethod($"{memberName}___DynamicChoicesCompileTimeAsync") != null)
                schema["x-rf-has-dynamic-choices-compile-time"] = true;
            if (parentType.GetMethod($"{memberName}___LogicSanityCheckAsync") != null)
                schema["x-rf-has-logic-sanity-check"] = true;
        }

        return schema;
    }

    private static JObject BuildNumberSchema(Field fieldAttr)
    {
        var schema = new JObject { ["type"] = "number" };
        if (fieldAttr is Attributes.Fields.Number)
        {
            var minVal = GetPrivateField<double?>(fieldAttr, "_minimumValue");
            var maxVal = GetPrivateField<double?>(fieldAttr, "_maximumValue");
            if (minVal.HasValue) schema["minimum"] = minVal.Value;
            if (maxVal.HasValue) schema["maximum"] = maxVal.Value;
            var step = GetPrivateField<double?>(fieldAttr, "_stepSize");
            if (step.HasValue) schema["multipleOf"] = step.Value;
        }
        return schema;
    }

    private static JObject BuildRangeSchema(Field fieldAttr)
    {
        var schema = new JObject { ["type"] = "number" };
        if (fieldAttr is Range)
        {
            var minVal = GetPrivateField<double>(fieldAttr, "_minimumValue");
            var maxVal = GetPrivateField<double>(fieldAttr, "_maximumValue");
            var step = GetPrivateField<double>(fieldAttr, "_stepSize");
            schema["minimum"] = minVal;
            schema["maximum"] = maxVal;
            schema["multipleOf"] = step;
        }
        return schema;
    }

    private static JObject BuildSelectSchema(Field fieldAttr)
    {
        if (fieldAttr is Select select)
        {
            var choices = select.Choices;
            if (choices != null)
            {
                var enumValues = new JArray();
                foreach (var choice in choices)
                {
                    var parts = choice.Split(" : ");
                    enumValues.Add(parts[0]);
                }
                return new JObject { ["type"] = "string", ["enum"] = enumValues };
            }
        }
        return new JObject { ["type"] = "string" };
    }

    private static JObject BuildRelationSchema(Field fieldAttr)
    {
        if (fieldAttr is Relation)
        {
            var entityName = GetPrivateField<string>(fieldAttr, "_relationEntityName");
            return new JObject
            {
                ["type"] = "integer",
                ["description"] = $"ID of related {entityName}"
            };
        }
        return new JObject { ["type"] = "integer" };
    }

    private static JObject BuildGroupSchema(MemberInfo member, Type parentType, bool includeExtensions)
    {
        if (member.GetCustomAttribute<Group>() is { } group)
        {
            var groupType = GetPrivateField<Type>(group, "_groupFor");
            if (groupType != null)
            {
                return BuildFieldsSchemaFromType(groupType, includeExtensions);
            }
        }
        return new JObject { ["type"] = "object" };
    }

    private static JObject BuildRepeaterSchema(MemberInfo member, Type parentType, bool includeExtensions)
    {
        if (member.GetCustomAttribute<Repeater>() is { } repeater)
        {
            var itemType = GetPrivateField<Type>(repeater, "_repeaterFor");
            if (itemType != null)
            {
                var itemSchema = BuildFieldsSchemaFromType(itemType, includeExtensions);
                var schema = new JObject
                {
                    ["type"] = "array",
                    ["items"] = itemSchema
                };
                if (repeater.MinimumRows.HasValue)
                    schema["minItems"] = repeater.MinimumRows.Value;
                if (repeater.MaximumRows.HasValue)
                    schema["maxItems"] = repeater.MaximumRows.Value;
                return schema;
            }
        }
        return new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "object" } };
    }

    private static JObject BuildEntityWrapperSchema(string entityName, EntityConfigurationBuilderBase ec)
    {
        var properties = new JObject
        {
            [EntityModelAttributes.Id] = new JObject { ["type"] = "integer" },
            [EntityModelAttributes.Slug] = new JObject { ["type"] = "string" },
            [EntityModelAttributes.Title] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["rendered"] = new JObject { ["type"] = "string" }
                }
            },
            [EntityModelAttributes.Date] = new JObject { ["type"] = "string", ["format"] = "date-time" },
            [EntityModelAttributes.DateGmt] = new JObject { ["type"] = "string", ["format"] = "date-time" },
            [EntityModelAttributes.Modified] = new JObject { ["type"] = "string", ["format"] = "date-time" },
            [EntityModelAttributes.ModifiedGmt] = new JObject { ["type"] = "string", ["format"] = "date-time" },
            [EntityModelAttributes.Fields] = new JObject { ["$ref"] = $"#/components/schemas/{entityName}_fields" }
        };

        if (ec.HasParentChildRelationship)
            properties[EntityModelAttributes.Parent] = new JObject { ["type"] = "integer" };
        if (ec.HasAuthor)
            properties[EntityModelAttributes.Author] = new JObject { ["type"] = "integer" };
        if (ec.HasTags)
            properties[EntityModelAttributes.Tags] = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "integer" } };
        if (ec.HasCategories)
            properties[EntityModelAttributes.Categories] = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "integer" } };

        return new JObject
        {
            ["type"] = "object",
            ["properties"] = properties
        };
    }

    #region Helper Methods

    private static JObject BuildOperation(string summary, string description, string operationId,
        JObject? requestBodyRef, JObject responseSchema, bool requiresAuth)
    {
        var op = new JObject
        {
            ["summary"] = summary,
            ["description"] = description,
            ["operationId"] = operationId,
            ["responses"] = new JObject
            {
                ["200"] = new JObject
                {
                    ["description"] = "Successful response",
                    ["content"] = new JObject
                    {
                        ["application/json"] = new JObject
                        {
                            ["schema"] = responseSchema
                        }
                    }
                }
            }
        };

        if (requestBodyRef != null)
        {
            op["requestBody"] = new JObject
            {
                ["required"] = true,
                ["content"] = new JObject
                {
                    ["application/json"] = new JObject
                    {
                        ["schema"] = requestBodyRef
                    }
                }
            };
        }

        if (requiresAuth)
        {
            op["security"] = new JArray
            {
                new JObject { ["bearerAuth"] = new JArray() },
                new JObject { ["cookieAuth"] = new JArray() }
            };
        }

        return op;
    }

    private static JObject RequestBodyRef(string schemaName) =>
        new() { ["$ref"] = $"#/components/schemas/{schemaName}" };

    private static JObject RequestBodyInline(JObject schema) => schema;

    private static JObject ResponseRef(string schemaName) =>
        new() { ["$ref"] = $"#/components/schemas/{schemaName}" };

    private static JObject ResponseObject(JObject schema) => schema;

    private static JObject IdSchema() =>
        new()
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["id"] = new JObject { ["type"] = "integer" }
            },
            ["required"] = new JArray("id")
        };

    private static bool IsFieldRequired(Field fieldAttr)
    {
        // Check via reflection for the _mandatory field pattern
        var mandatoryField = fieldAttr.GetType().GetField("_mandatory", BindingFlags.Instance | BindingFlags.NonPublic);
        if (mandatoryField != null)
            return (bool)mandatoryField.GetValue(fieldAttr)!;

        // Some fields like Checkbox don't have mandatory
        return false;
    }

    private static T? GetPrivateField<T>(object obj, string fieldName)
    {
        var field = obj.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        if (field == null) return default;
        return (T?)field.GetValue(obj);
    }

    #endregion
}
