// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReflectiveForms.Core.Attributes.Fields;
using ReflectiveForms.Core.Enums;
using ReflectiveForms.Core.Operation;

namespace ReflectiveForms.Core.Models.ReservedEntityTypes;

public sealed class IamRoleCapabilitiesModel : BaseModel
{
    [JsonProperty("entity_type"),
     Select(
         label: "Entity Type",
         instructions: "",
         defaultValue: "",
         choices: null)]
    public string EntityType { get; init; } = "unspecified";
    public static Task<string[]> EntityType___DynamicChoicesCompileTimeAsync(CancellationToken cancellationToken)
    {
        var result = new List<string> { "unspecified : Please Select" };
        result.AddRange(RfConfiguration.EntityNameToConfiguration.Select(et => $"{et.Key} : {et.Value.EntityConfiguration.EntityReadableNamePlural}"));
        return Task.FromResult(result.ToArray());
    }

    [JsonProperty("allow_peek_all"),
     Checkbox(
         label: "Allow Peek-All",
         instructions: "",
         defaultValue: false)]
    public bool AllowPeekAll { get; init; }

    [JsonProperty("allow_read"),
     Checkbox(
         label: "Allow Read",
         instructions: "",
         defaultValue: false)]
    public bool AllowRead { get; init; }

    [JsonProperty("allow_update"),
     Checkbox(
         label: "Allow Update",
         instructions: "",
         defaultValue: false)]
    public bool AllowUpdate { get; init; }

    [JsonProperty("allow_create"),
     Checkbox(
         label: "Allow Create",
         instructions: "",
         defaultValue: false)]
    public bool AllowCreate { get; init; }

    [JsonProperty("allow_delete"),
     Checkbox(
         label: "Allow Delete",
         instructions: "",
         defaultValue: false)]
    public bool AllowDelete { get; init; }

    // ReSharper disable once MemberCanBePrivate.Global
    public bool Equals(IamRoleCapabilitiesModel? other)
    {
        if (other is null) return false;
        return EntityType == other.EntityType
               && AllowPeekAll == other.AllowPeekAll
               && AllowRead == other.AllowRead
               && AllowUpdate == other.AllowUpdate
               && AllowCreate == other.AllowCreate
               && AllowDelete == other.AllowDelete;
    }

    public override bool Equals(object? obj) => Equals(obj as IamRoleCapabilitiesModel);

    public override int GetHashCode() =>
        HashCode.Combine(EntityType, AllowPeekAll, AllowRead, AllowUpdate, AllowCreate, AllowDelete);
}

public sealed class IamRoleEntityFieldsModel : EntityFieldsModel
{
    [JsonProperty("capabilities", NullValueHandling = NullValueHandling.Ignore),
        Repeater(
            label: "Capabilities",
            instructions: "",
            repeaterFor: typeof(IamRoleCapabilitiesModel),
            addButtonLabel: "Add New",
            groupRenderStyle: GroupRenderStyle.Grid6ElementsInRow,
            useAccordion: RepeatUseAccordion.No)]
    public List<IamRoleCapabilitiesModel> Capabilities { get; set; } = [];
    public Task<string?> Capabilities___LogicSanityCheckAsync(int entityId, EntityOperationState operationState, JObject parentJObject, CancellationToken cancellationToken)
    {
        if (Capabilities.Count == 0)
            return Task.FromResult<string?>("There must be at least one capability for the role.");

        var seenEntityTypes = new HashSet<string>();
        foreach (var capability in Capabilities)
        {
            if (string.IsNullOrEmpty(capability.EntityType) || capability.EntityType == "unspecified")
                return Task.FromResult<string?>("Entity types of all capabilities must be selected correctly.");
            if (!seenEntityTypes.Add(capability.EntityType))
                return Task.FromResult<string?>($"Entity types of all capabilities must be selected uniquely. There cannot be repeating entity types. Entity type {capability.EntityType} is repeated.");
            if (capability is { AllowPeekAll: false, AllowRead: false, AllowUpdate: false, AllowCreate: false, AllowDelete: false })
                return Task.FromResult<string?>($"Capability for entity type {capability.EntityType} should at least have one allowed operation.");
        }
        return Task.FromResult<string?>(null);
    }

    public bool CanDo(string entityType, string operation)
    {
        switch (operation)
        {
            case "READ":
                foreach (var capability in Capabilities.Where(capability => capability.EntityType == entityType))
                {
                    if (capability.AllowRead) return true;
                    break;
                }
                break;
            case "PEEK_ALL":
                foreach (var capability in Capabilities.Where(capability => capability.EntityType == entityType))
                {
                    if (capability.AllowPeekAll) return true;
                    break;
                }
                break;
            case "UPDATE":
                foreach (var capability in Capabilities.Where(capability => capability.EntityType == entityType))
                {
                    if (capability.AllowUpdate) return true;
                    break;
                }
                break;
            case "CREATE":
                foreach (var capability in Capabilities.Where(capability => capability.EntityType == entityType))
                {
                    if (capability.AllowCreate) return true;
                    break;
                }
                break;
            case "DELETE":
                foreach (var capability in Capabilities.Where(capability => capability.EntityType == entityType))
                {
                    if (capability.AllowDelete) return true;
                    break;
                }
                break;
        }
        return false;
    }
}
