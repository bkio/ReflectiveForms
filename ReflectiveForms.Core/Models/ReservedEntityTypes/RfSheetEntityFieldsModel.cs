// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using Newtonsoft.Json;
using ReflectiveForms.Core.Attributes.Fields;
using ReflectiveForms.Core.Enums;

namespace ReflectiveForms.Core.Models.ReservedEntityTypes;

public sealed class RfSheetSharedUserModel : BaseModel
{
    [JsonProperty("user"),
     Relation(
         label: "User",
         instructions: "Select a user to share this sheet with",
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

public sealed class RfSheetSharedRoleModel : BaseModel
{
    [JsonProperty("role"),
     Relation(
         label: "Role",
         instructions: "Select a role to share this sheet with",
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

public class RfSheetEntityFieldsModel : EntityFieldsModel
{
    [JsonProperty("sources"),
     TextArea(
         label: "Sources",
         instructions: "JSON array of entity sources this sheet reads from",
         mandatory: false,
         placeholderText: "[]")]
    public string Sources { get; set; } = "[]";

    [JsonProperty("bound_regions"),
     TextArea(
         label: "Bound Regions",
         instructions: "JSON array of bound region definitions",
         mandatory: false,
         placeholderText: "[]")]
    public string BoundRegions { get; set; } = "[]";

    [JsonProperty("workbook_data"),
     TextArea(
         label: "Workbook Data",
         instructions: "Spreadsheet library native save state (JSON)",
         mandatory: false,
         placeholderText: "{}")]
    public string WorkbookData { get; set; } = "{}";

    [JsonProperty("refresh_interval_seconds"),
     Number(
         label: "Refresh Interval (seconds)",
         instructions: "How often the sheet polls for updated entity data",
         mandatory: false,
         placeholderText: "30",
         defaultValue: 30,
         minimumMaximumValues: new double[] { 5, 3600 })]
    public int RefreshIntervalSeconds { get; set; } = 30;

    [JsonProperty("is_public"),
     Checkbox(
         label: "Public",
         instructions: "When enabled, anyone with sheet access permissions can view this sheet",
         defaultValue: false)]
    public bool IsPublic { get; set; }

    [JsonProperty("shared_users", NullValueHandling = NullValueHandling.Ignore),
     Repeater(
         label: "Shared With Users",
         instructions: "Share this sheet with specific users",
         repeaterFor: typeof(RfSheetSharedUserModel),
         addButtonLabel: "Add User",
         minimumRows: 0,
         maximumRows: 100,
         groupRenderStyle: GroupRenderStyle.Grid2ElementsInRow,
         useAccordion: RepeatUseAccordion.No)]
    public List<RfSheetSharedUserModel> SharedUsers { get; set; } = [];

    [JsonProperty("shared_roles", NullValueHandling = NullValueHandling.Ignore),
     Repeater(
         label: "Shared With Roles",
         instructions: "Share this sheet with users who have a specific role",
         repeaterFor: typeof(RfSheetSharedRoleModel),
         addButtonLabel: "Add Role",
         minimumRows: 0,
         maximumRows: 50,
         groupRenderStyle: GroupRenderStyle.Grid2ElementsInRow,
         useAccordion: RepeatUseAccordion.No)]
    public List<RfSheetSharedRoleModel> SharedRoles { get; set; } = [];
}
