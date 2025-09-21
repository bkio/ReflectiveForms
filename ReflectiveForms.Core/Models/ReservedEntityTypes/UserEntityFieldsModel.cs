// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReflectiveForms.Core.Attributes.Fields;
using ReflectiveForms.Core.Enums;
using ReflectiveForms.Core.Operation;

namespace ReflectiveForms.Core.Models.ReservedEntityTypes;

public sealed class UserRoleAssignmentModel : BaseModel
{
    [JsonProperty("role"),
     Relation(
         label: "Role",
         instructions: "",
         mandatory: true,
         isRelationEntityNotExistsOk: false,
         relationEntityName: RfReservedEntities.IamRoleEntityName)]
    public int RoleId { get; init; } = -1;
}

public sealed class UserEntityFieldsModel : EntityFieldsModel
{
    [JsonProperty("email_address"),
        Text(
            label: "E-Mail Address",
            instructions: "",
            mandatory: true,
            placeholderText: "")]
    public string EmailAddress { get; set; } = "";
    public Task<string?> EmailAddress___LogicSanityCheckAsync(int entityId, EntityOperationState operationState, JObject parentJObject, CancellationToken cancellationToken)
    {
        var users = RfConfiguration.UserEntitiesCache.FindEntitiesAndGetCopies();
        return Task.FromResult(users
            .Where(user => user.Id != entityId)
            .Any(user => user.Fields.EmailAddress == EmailAddress)
            ? $"E-mail Address must be unique globally. There is another user with e-mail address {EmailAddress}"
            : null);
    }

    [JsonProperty("optional_custom_password"),
        Text(
            label: "(Optional) Custom Password",
            instructions: "",
            mandatory: false,
            placeholderText: "")]
    public string OptionalCustomPassword { get; set; } = "";

    [JsonProperty("generate_password"),
        Checkbox(
            label: "Generate New Password",
            instructions: "",
            defaultValue: true)]
    public bool GeneratePassword { get; set; }
    public Task<string?> GeneratePassword___LogicSanityCheckAsync(int entityId, EntityOperationState operationState, JObject parentJObject, CancellationToken cancellationToken)
    {
        return GeneratePassword switch
        {
            true when !string.IsNullOrEmpty(OptionalCustomPassword) =>
                Task.FromResult<string?>("Generate password checkbox cannot be checked when a custom password is provided."),
            false when string.IsNullOrEmpty(OptionalCustomPassword) && string.IsNullOrEmpty(PasswordSha256) =>
                Task.FromResult<string?>("User has not been assigned with a password before. Therefore either Generate Password must be checked or a custom password must be provided."),
            _ => Task.FromResult<string?>(null)
        };
    }

    [JsonProperty("password_sha256")]
    public string PasswordSha256 { get; set; } = "";

    [JsonProperty("roles", NullValueHandling = NullValueHandling.Ignore),
        Repeater(
            label: "Roles",
            instructions: "",
            repeaterFor: typeof(UserRoleAssignmentModel),
            addButtonLabel: "Add Role",
            groupRenderStyle: GroupRenderStyle.Full,
            useAccordion: RepeatUseAccordion.No)]
    public List<UserRoleAssignmentModel> Roles { get; set; } = [];
    public Task<string?> Roles___LogicSanityCheckAsync(int entityId, EntityOperationState operationState, JObject parentJObject, CancellationToken cancellationToken)
    {
        if (Roles.Count == 0)
            return Task.FromResult<string?>("There must be at least one role assigned for the user.");

        var seenRoles = new HashSet<int>();
        foreach (var role in Roles)
        {
            if (role.RoleId <= 0)
                return Task.FromResult<string?>("All assigned roles must be selected correctly.");
            if (!seenRoles.Add(role.RoleId))
                return Task.FromResult<string?>("Roles must be selected uniquely. There cannot be repeating roles.");
        }
        return Task.FromResult<string?>(null);
    }
}
