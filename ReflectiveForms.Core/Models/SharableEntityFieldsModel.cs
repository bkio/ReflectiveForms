// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using Newtonsoft.Json;
using ReflectiveForms.Core.Attributes.Fields;
using ReflectiveForms.Core.Enums;

namespace ReflectiveForms.Core.Models;

/// <summary>
/// Represents a user-level sharing entry: a user id and their permission level.
/// </summary>
public sealed class SharedUserModel : BaseModel
{
    [JsonProperty("user"),
     Relation(
         label: "User",
         instructions: "Select a user to share with",
         mandatory: true,
         isRelationEntityNotExistsOk: false,
         relationEntityName: RfReservedEntities.UsersEntityName)]
    public int UserId { get; set; } = -1;

    [JsonProperty("permission"),
     Select(
         label: "Permission",
         instructions: "",
         defaultValue: "view",
         choices: new[] { "view : View", "edit : Edit" })]
    public string Permission { get; set; } = "view";
}

/// <summary>
/// Represents a role-level sharing entry: a role id and its permission level.
/// </summary>
public sealed class SharedRoleModel : BaseModel
{
    [JsonProperty("role"),
     Relation(
         label: "Role",
         instructions: "Select a role to share with",
         mandatory: true,
         isRelationEntityNotExistsOk: false,
         relationEntityName: RfReservedEntities.IamRoleEntityName)]
    public int RoleId { get; set; } = -1;

    [JsonProperty("permission"),
     Select(
         label: "Permission",
         instructions: "",
         defaultValue: "view",
         choices: new[] { "view : View", "edit : Edit" })]
    public string Permission { get; set; } = "view";
}

/// <summary>
/// Base class for entity field models that support individual sharing.
/// Provides is_public, shared_users, and shared_roles fields.
/// Entity types configured with HasIndividualSharing = true must have their
/// fields model inherit from this class.
/// </summary>
public class SharableEntityFieldsModel : EntityFieldsModel
{
    [JsonProperty("is_public"),
     Checkbox(
         label: "Public",
         instructions: "When enabled, anyone with access permissions for this entity type can view this entry",
         defaultValue: false)]
    public bool IsPublic { get; set; }

    [JsonProperty("shared_users", NullValueHandling = NullValueHandling.Ignore),
     Repeater(
         label: "Shared With Users",
         instructions: "Share with specific users",
         repeaterFor: typeof(SharedUserModel),
         addButtonLabel: "Add User",
         minimumRows: 0,
         maximumRows: 100,
         groupRenderStyle: GroupRenderStyle.Grid2ElementsInRow,
         useAccordion: RepeatUseAccordion.No)]
    public List<SharedUserModel> SharedUsers { get; set; } = [];

    [JsonProperty("shared_roles", NullValueHandling = NullValueHandling.Ignore),
     Repeater(
         label: "Shared With Roles",
         instructions: "Share with users who have a specific role",
         repeaterFor: typeof(SharedRoleModel),
         addButtonLabel: "Add Role",
         minimumRows: 0,
         maximumRows: 50,
         groupRenderStyle: GroupRenderStyle.Grid2ElementsInRow,
         useAccordion: RepeatUseAccordion.No)]
    public List<SharedRoleModel> SharedRoles { get; set; } = [];
}
