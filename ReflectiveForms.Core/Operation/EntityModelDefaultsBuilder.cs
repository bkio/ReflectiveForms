// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using System.Reflection;
using System.Text.RegularExpressions;
using CrossCloudKit.Utilities.Common;
using Newtonsoft.Json.Linq;
using ReflectiveForms.Core.Attributes;
using ReflectiveForms.Core.Attributes.Fields;
using ReflectiveForms.Core.Models;
using ReflectiveForms.Core.Utilities;

namespace ReflectiveForms.Core.Operation;

public static class EntityModelDefaultsBuilder
{
    public static string JsObjectPathSetAllArrayIndexesToZero(string jsObjectPath)
    {
        return Regex.Replace(jsObjectPath, SetAllArrayIndexesToZeroPattern, "[0]");
    }
    private const string SetAllArrayIndexesToZeroPattern = @"\.find\(.*?\)";

    public static async Task<JObject> CreateDefaultEntityFieldsObjectAsync(
        string entityName,
        EntityOperationState operationState,
        bool generateUniqueIds,
        CancellationToken cancellationToken)
    {
        var defaultElementInstance = (EntityFieldsModel)Activator.CreateInstance(RfConfiguration.EntityNameToConfiguration[entityName].EntityConfiguration.EntityFieldsModelType, nonPublic: true).NotNull();
        await FillEntityFieldsListsWithAtLeastAnElementAsync(defaultElementInstance, operationState, cancellationToken);

        var result = defaultElementInstance.FromObjectWithPolymorphism();
        if (generateUniqueIds)
        {
            IterativelyChangeUniqueFieldIdsWithRandomIds(result);
        }
        return result;
    }
    private static async Task FillEntityFieldsListsWithAtLeastAnElementAsync(EntityFieldsModel fieldsModel, EntityOperationState operationState, CancellationToken cancellationToken)
    {
        fieldsModel.MustSerializeUniqueFieldId = true;

        var objectType = fieldsModel.GetType();
        var fields = objectType.GetFields(BindingFlags.Instance | BindingFlags.Public);

        foreach (var field in fields)
        {
            if (!Attribute.IsDefined(field, typeof(Field), true))
                continue;

            var fieldAttribute = field.GetCustomAttribute<Field>(true);

            var shouldSerializeMethods = objectType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                          .Where(
                                m => m.Name == $"ShouldSerialize{field.Name}"
                                && m.ReturnType == typeof(bool)
                                && (m.GetParameters().Length == 0))
                          .ToList();
            if (shouldSerializeMethods is { Count: > 0 })
            {
                fieldsModel.OverrideShouldSerializeFor(field.Name);
            }

            if (field.FieldType.IsGenericType &&
                field.FieldType.GetGenericTypeDefinition() == typeof(List<>))
            {
                var elementType = field.FieldType.GetGenericArguments()[0];

                var defaultElementInstance = Activator.CreateInstance(elementType, nonPublic: true);

                var asList = field.GetValue(fieldsModel).NotNull();

                var addMethod = asList.GetType().GetMethod("Add").NotNull();
                var removeAtMethod = asList.GetType().GetMethod("RemoveAt").NotNull();

                var numberOfCurrentElements = (int)asList.GetType().GetProperty("Count").NotNull().GetValue(asList).NotNull();
                if (numberOfCurrentElements == 0)
                {
                    addMethod.Invoke(asList, [defaultElementInstance]);
                    numberOfCurrentElements++;
                }

                var fieldListAttribute = (fieldAttribute as Repeater).NotNull();

                if (fieldListAttribute.MinimumRows.HasValue && fieldListAttribute.MinimumRows > numberOfCurrentElements)
                {
                    for (var i = numberOfCurrentElements; i < fieldListAttribute.MinimumRows.Value; i++)
                    {
                        addMethod.Invoke(asList, [defaultElementInstance]);
                    }
                }
                if (fieldListAttribute.MaximumRows.HasValue && fieldListAttribute.MaximumRows < numberOfCurrentElements)
                {
                    for (var i = numberOfCurrentElements - 1; i >= fieldListAttribute.MaximumRows.Value; i--)
                    {
                        removeAtMethod.Invoke(asList, [i]);
                    }
                }

                var getProperty = asList.GetType().GetProperty("Item").NotNull();
                var elementInstance = getProperty.GetValue(asList, [0]).NotNull();

                if (elementType.IsClass && elementType != typeof(string))
                {
                    await FillEntityFieldsListsWithAtLeastAnElementAsync((EntityFieldsModel)elementInstance, operationState, cancellationToken);
                }
            }
            else if (field.FieldType.IsClass && field.FieldType != typeof(string))
            {
                var objectInstance = (EntityFieldsModel)field.GetValue(fieldsModel).NotNull();
                await FillEntityFieldsListsWithAtLeastAnElementAsync(objectInstance, operationState, cancellationToken);
            }
            else
            {
                if (fieldAttribute.NotNull().CalculatedDynamicDefaultValueNullable != null)
                {
                    field.SetValue(fieldsModel, fieldAttribute.NotNull().CalculatedDynamicDefaultValueNullable);
                }
                else
                {
                    var ddvFound = false;
                    var dynamicDefaultValueFunction = fieldsModel.GetType().GetMethod($"{field.Name}___DynamicDefaultValueAsync");
                    if (dynamicDefaultValueFunction != null)
                    {
                        var dynamicDefaultValueTask = (Task<object?>)dynamicDefaultValueFunction.Invoke(
                            fieldsModel,
                            [cancellationToken]
                        ).NotNull();

                        var newDefaultValue = await dynamicDefaultValueTask;
                        if (newDefaultValue != null)
                        {
                            fieldAttribute.NotNull().CalculatedDynamicDefaultValueNullable = newDefaultValue;
                            field.SetValue(fieldsModel, newDefaultValue);
                            ddvFound = true;
                        }
                    }
                    if (!ddvFound)
                    {
                        fieldAttribute.NotNull().SetDefaultValue(operationState, obj =>
                        {
                            field.SetValue(fieldsModel, obj);
                        });
                    }
                }
            }
        }
    }

    public static void IterativelyChangeUniqueFieldIdsWithRandomIds(JObject clonedDefaultObject)
    {
        if (clonedDefaultObject.ContainsKey(BaseModel.UniqueFieldIdPropertyName))
        {
            clonedDefaultObject[BaseModel.UniqueFieldIdPropertyName] = StringUtilities.GenerateRandomString(16, DigitOptions.OnlyCharacters, CaseOptions.FullUppercase);
        }

        foreach (var item in clonedDefaultObject)
        {
            switch (item.Value)
            {
                case { Type: JTokenType.Object }:
                    IterativelyChangeUniqueFieldIdsWithRandomIds((JObject)item.Value);
                    break;
                case { Type: JTokenType.Array }:
                {
                    var asArr = (JArray)item.Value;
                    foreach (var arrIt in asArr)
                    {
                        IterativelyChangeUniqueFieldIdsWithRandomIds((JObject)arrIt);
                    }

                    break;
                }
            }
        }
    }

}
