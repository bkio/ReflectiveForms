// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using System.Net;
using System.Reflection;
using CrossCloudKit.Interfaces.Classes;
using Newtonsoft.Json;
using ReflectiveForms.Core.Attributes;
using ReflectiveForms.Core.Attributes.Fields;
using ReflectiveForms.Core.Enums;
using ReflectiveForms.Core.Schema.Models;
using Range = ReflectiveForms.Core.Attributes.Fields.Range;

namespace ReflectiveForms.Core.Schema;

/// <summary>
/// Generates JSON schemas from C# entity configurations.
/// These schemas are consumed by frontend applications to dynamically render forms.
/// </summary>
public static class EntitySchemaGenerator
{
    /// <summary>
    /// Generate a complete schema for the specified entity type.
    /// </summary>
    public static OperationResult<EntitySchema> GenerateSchema(string entityName)
    {
        if (!RfConfiguration.EntityNameToConfiguration.TryGetValue(entityName, out var configBase))
        {
            return OperationResult<EntitySchema>.Failure(
                $"Entity type '{entityName}' not found.",
                HttpStatusCode.NotFound);
        }

        var config = configBase.EntityConfiguration;
        var fieldsModelType = config.EntityFieldsModelType;

        var fields = GenerateFieldSchemas(fieldsModelType);

        var schema = new EntitySchema
        {
            EntityName = entityName,
            ReadableName = new ReadableName
            {
                Singular = config.EntityReadableNameSingular,
                Plural = config.EntityReadableNamePlural
            },
            Features = new EntityFeatures
            {
                HasAuthor = config.HasAuthor,
                HasTags = config.HasTags,
                HasCategories = config.HasCategories,
                HasParentChild = config.HasParentChildRelationship,
                RequireTitleUniqueness = config.RequireGlobalTitleUniqueness,
                SupportsFrontendEdit = config.SupportsFrontendEdit
            },
            Fields = fields,
            ApiEndpoints = new ApiEndpoints
            {
                Crud = RfEndpointMapper.PublicCrudEndpoint,
                SanityCheck = RfEndpointMapper.PublicSanityCheckEndpoint,
                EntityLock = RfEndpointMapper.PublicEntityLockControlEndpoint,
                Media = RfConfiguration.EndpointConfiguration.PublicUrlRootForApi + "media"
            }
        };

        return OperationResult<EntitySchema>.Success(schema);
    }

    /// <summary>
    /// Generate schemas for all registered entity types.
    /// </summary>
    public static OperationResult<Dictionary<string, EntitySchema>> GenerateAllSchemas()
    {
        var schemas = new Dictionary<string, EntitySchema>();

        foreach (var entityName in RfConfiguration.EntityNameToConfiguration.Keys)
        {
            var result = GenerateSchema(entityName);
            if (!result.IsSuccessful)
            {
                return OperationResult<Dictionary<string, EntitySchema>>.Failure(
                    result.ErrorMessage,
                    result.StatusCode);
            }
            schemas[entityName] = result.Data;
        }

        return OperationResult<Dictionary<string, EntitySchema>>.Success(schemas);
    }

    private static List<FieldSchema> GenerateFieldSchemas(Type modelType)
    {
        var fieldSchemas = new List<FieldSchema>();
        var fields = modelType.GetFields(BindingFlags.Instance | BindingFlags.Public);
        var properties = modelType.GetProperties(BindingFlags.Instance | BindingFlags.Public);

        // Process fields
        foreach (var field in fields)
        {
            var schema = ProcessMember(field, field.FieldType, modelType);
            if (schema != null)
            {
                fieldSchemas.Add(schema);
            }
        }

        // Process properties
        foreach (var prop in properties)
        {
            var schema = ProcessMember(prop, prop.PropertyType, modelType);
            if (schema != null)
            {
                fieldSchemas.Add(schema);
            }
        }

        return fieldSchemas;
    }

    private static FieldSchema? ProcessMember(MemberInfo member, Type memberType, Type parentType)
    {
        if (!Attribute.IsDefined(member, typeof(Field), true))
            return null;

        var fieldAttribute = member.GetCustomAttribute<Field>(true);
        if (fieldAttribute == null)
            return null;

        // Get JSON property name
        var jsonPropAttr = member.GetCustomAttribute<JsonPropertyAttribute>(true);
        var fieldName = jsonPropAttr?.PropertyName ?? member.Name;

        // Get display condition
        var displayCondAttr = member.GetCustomAttribute<DisplayCondition>(true);
        var displayCondition = displayCondAttr?.Condition;

        // Check for dynamic methods
        var hasDynamicChoicesRuntime = parentType.GetMethod($"{member.Name}___DynamicChoicesRuntimeAsync") != null;
        var hasDynamicChoicesCompileTime = parentType.GetMethod($"{member.Name}___DynamicChoicesCompileTimeAsync") != null;
        var hasLogicSanityCheck = parentType.GetMethod($"{member.Name}___LogicSanityCheckAsync") != null;
        var hasDynamicDefaultValue = parentType.GetMethod($"{member.Name}___DynamicDefaultValueAsync") != null;

        // If the field has DynamicDefaultValueAsync, invoke it to populate the default value.
        object? dynamicDefaultValue = null;
        if (hasDynamicDefaultValue)
        {
            var dynamicDefaultMethod = parentType.GetMethod($"{member.Name}___DynamicDefaultValueAsync");
            if (dynamicDefaultMethod != null)
            {
                try
                {
                    var instance = Activator.CreateInstance(parentType, nonPublic: true);
                    var task = (Task<object?>)dynamicDefaultMethod.Invoke(instance, [CancellationToken.None])!;
                    dynamicDefaultValue = task.GetAwaiter().GetResult();
                }
                catch
                {
                    // Fallback: leave dynamicDefaultValue null
                }
            }
        }

        // If the field has DynamicChoicesCompileTimeAsync, invoke the static method to populate choices.
        if (hasDynamicChoicesCompileTime && fieldAttribute is Select compileTimeSelect)
        {
            var compileTimeMethod = parentType.GetMethod($"{member.Name}___DynamicChoicesCompileTimeAsync");
            if (compileTimeMethod != null)
            {
                try
                {
                    var task = (Task<string[]>)compileTimeMethod.Invoke(null, [CancellationToken.None])!;
                    compileTimeSelect.Choices = task.GetAwaiter().GetResult();
                }
                catch
                {
                    // Fallback: leave Choices null
                }
            }
        }

        // If the field has DynamicChoicesRuntimeAsync, invoke the method to get the JS function
        // and populate it on the Select attribute so GetSelectOptions can read it.
        if (hasDynamicChoicesRuntime && fieldAttribute is Select selectAttr)
        {
            var runtimeMethod = parentType.GetMethod($"{member.Name}___DynamicChoicesRuntimeAsync");
            if (runtimeMethod != null)
            {
                try
                {
                    var instance = Activator.CreateInstance(parentType, nonPublic: true);
                    var task = (Task<string>)runtimeMethod.Invoke(instance, [CancellationToken.None])!;
                    selectAttr.RuntimeChoiceJsFunction = task.GetAwaiter().GetResult();
                }
                catch
                {
                    // Fallback: leave DynamicChoicesJsFunction null
                }
            }
        }

        var schema = new FieldSchema
        {
            Name = fieldName,
            Type = MapFieldType(fieldAttribute.Type),
            Label = fieldAttribute.Label ?? fieldName,
            Instructions = fieldAttribute.Instructions,
            Required = IsRequired(fieldAttribute),
            DefaultValue = dynamicDefaultValue ?? GetDefaultValue(fieldAttribute),
            DisplayCondition = displayCondition,
            HasDynamicChoicesRuntime = hasDynamicChoicesRuntime,
            HasDynamicChoicesCompileTime = hasDynamicChoicesCompileTime,
            HasLogicSanityCheck = hasLogicSanityCheck,
            TextOptions = GetTextOptions(fieldAttribute),
            SelectOptions = GetSelectOptions(fieldAttribute),
            NumberOptions = GetNumberOptions(fieldAttribute),
            DateOptions = GetDateOptions(fieldAttribute),
            RelationOptions = GetRelationOptions(fieldAttribute),
            RepeaterOptions = GetRepeaterOptions(fieldAttribute),
            GroupOptions = GetGroupOptions(fieldAttribute),
            MediaOptions = GetMediaOptions(fieldAttribute)
        };

        return schema;
    }

    private static FieldSchemaType MapFieldType(FieldType type) => type switch
    {
        FieldType.Text => FieldSchemaType.Text,
        FieldType.TextArea => FieldSchemaType.TextArea,
        FieldType.WysiwygEditor => FieldSchemaType.WysiwygEditor,
        FieldType.Number => FieldSchemaType.Number,
        FieldType.Range => FieldSchemaType.Range,
        FieldType.Email => FieldSchemaType.Email,
        FieldType.Url => FieldSchemaType.Url,
        FieldType.Select => FieldSchemaType.Select,
        FieldType.Checkbox => FieldSchemaType.Checkbox,
        FieldType.Relation => FieldSchemaType.Relation,
        FieldType.DatePicker => FieldSchemaType.DatePicker,
        FieldType.Group => FieldSchemaType.Group,
        FieldType.Repeater => FieldSchemaType.Repeater,
        FieldType.MediaSourceBase64 => FieldSchemaType.MediaSourceBase64,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };

    private static bool IsRequired(Field field) => field switch
    {
        TextArea ta => GetPrivateField<bool>(ta, "_mandatory"),
        Text t => GetPrivateField<bool>(t, "_mandatory"),
        Email e => GetPrivateField<bool>(e, "_mandatory"),
        Url u => GetPrivateField<bool>(u, "_mandatory"),
        DatePicker dp => GetPrivateField<bool>(dp, "_mandatory"),
        Relation r => GetPrivateField<bool>(r, "_mandatory"),
        _ => false
    };

    private static object? GetDefaultValue(Field field) => field switch
    {
        TextArea ta => GetPrivateField<string>(ta, "_defaultValueNullable"),
        Text t => GetPrivateField<string>(t, "_defaultValueNullable"),
        Select s => GetPrivateField<string>(s, "_defaultValue"),
        Checkbox c => GetPrivateField<bool>(c, "_defaultValue"),
        Number n => GetPrivateField<double?>(n, "_defaultValue"),
        Range r => GetPrivateField<double?>(r, "_defaultValue"),
        _ => null
    };

    private static TextFieldOptions? GetTextOptions(Field field)
    {
        return field switch
        {
            TextArea ta => new TextFieldOptions
            {
                Placeholder = GetPrivateField<string>(ta, "_placeholderText"),
                IsMultiline = true
            },
            Text t => new TextFieldOptions
            {
                Placeholder = GetPrivateField<string>(t, "_placeholderText"),
                IsMultiline = false
            },
            Email e => new TextFieldOptions
            {
                Placeholder = GetPrivateField<string>(e, "_placeholderText"),
                IsMultiline = false
            },
            Url u => new TextFieldOptions
            {
                Placeholder = GetPrivateField<string>(u, "_placeholderText"),
                IsMultiline = false
            },
            _ => null
        };
    }

    private static SelectFieldOptions? GetSelectOptions(Field field)
    {
        if (field is not Select select)
            return null;

        var choices = select.Choices?.Select(choice =>
        {
            var parts = choice.Split(" : ", 2);
            return new SelectChoice
            {
                Value = parts[0],
                Label = parts.Length > 1 ? parts[1] : parts[0]
            };
        }).ToList();

        return new SelectFieldOptions
        {
            Choices = choices,
            DynamicChoicesJsFunction = GetPrivateField<string>(select, "_internalRuntimeChoiceJsFunction")
        };
    }

    private static NumberFieldOptions? GetNumberOptions(Field field)
    {
        return field switch
        {
            Number n => new NumberFieldOptions
            {
                Min = GetPrivateField<double?>(n, "_minimumValue"),
                Max = GetPrivateField<double?>(n, "_maximumValue"),
                Step = GetPrivateField<double?>(n, "_stepSize"),
                IsRange = false
            },
            Range r => new NumberFieldOptions
            {
                Min = GetPrivateField<double>(r, "_minimumValue"),
                Max = GetPrivateField<double>(r, "_maximumValue"),
                Step = GetPrivateField<double>(r, "_stepSize"),
                IsRange = true
            },
            _ => null
        };
    }

    private static DateFieldOptions? GetDateOptions(Field field)
    {
        if (field is not DatePicker dp)
            return null;

        return new DateFieldOptions
        {
            Format = GetPrivateField<string>(dp, "_dateFormat") ?? "yyyyMMdd"
        };
    }

    private static RelationFieldOptions? GetRelationOptions(Field field)
    {
        if (field is not Relation rel)
            return null;

        return new RelationFieldOptions
        {
            RelationEntityName = GetPrivateField<string>(rel, "_relationEntityName") ?? "",
            IsRelationEntityNotExistsOk = GetPrivateField<bool>(rel, "_isRelationEntityNotExistsOk")
        };
    }

    private static RepeaterFieldOptions? GetRepeaterOptions(Field field)
    {
        if (field is not Repeater rep)
            return null;

        var repeaterForType = GetPrivateField<Type>(rep, "_repeaterFor");
        var childSchemas = repeaterForType != null
            ? GenerateFieldSchemas(repeaterForType)
            : [];

        var renderStyle = GetPrivateField<GroupRenderStyle>(rep, "_groupRenderStyle");
        var useAccordion = GetPrivateField<RepeatUseAccordion>(rep, "_useAccordion");

        return new RepeaterFieldOptions
        {
            ItemSchema = childSchemas,
            MinItems = rep.MinimumRows,
            MaxItems = rep.MaximumRows,
            AddButtonLabel = GetPrivateField<string>(rep, "_addButtonLabel") ?? "Add",
            UseAccordion = useAccordion == RepeatUseAccordion.Yes,
            RenderStyle = MapRenderStyle(renderStyle)
        };
    }

    private static GroupFieldOptions? GetGroupOptions(Field field)
    {
        if (field is not Group grp)
            return null;

        var groupForType = GetPrivateField<Type>(grp, "_groupFor");
        var childSchemas = groupForType != null
            ? GenerateFieldSchemas(groupForType)
            : [];

        var renderStyle = GetPrivateField<GroupRenderStyle>(grp, "_renderStyle");

        return new GroupFieldOptions
        {
            ChildSchema = childSchemas,
            RenderStyle = MapRenderStyle(renderStyle)
        };
    }

    private static MediaFieldOptions? GetMediaOptions(Field field)
    {
        if (field is not MediaSourceBase64)
            return null;

        return new MediaFieldOptions
        {
            MaxFileSizeMb = 8,
            PreviewEnabled = true,
            AcceptedTypes = ["image/*"]
        };
    }

    private static GroupRenderStyleSchema MapRenderStyle(GroupRenderStyle style) => style switch
    {
        GroupRenderStyle.Full => GroupRenderStyleSchema.Full,
        GroupRenderStyle.Grid2ElementsInRow => GroupRenderStyleSchema.Grid2,
        GroupRenderStyle.Grid3ElementsInRow => GroupRenderStyleSchema.Grid3,
        GroupRenderStyle.Grid4ElementsInRow => GroupRenderStyleSchema.Grid4,
        GroupRenderStyle.Grid6ElementsInRow => GroupRenderStyleSchema.Grid6,
        _ => GroupRenderStyleSchema.Full
    };

    private static T? GetPrivateField<T>(object obj, string fieldName)
    {
        var field = obj.GetType().GetField(fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (field == null) return default;

        var value = field.GetValue(obj);
        return value is T typed ? typed : default;
    }
}
