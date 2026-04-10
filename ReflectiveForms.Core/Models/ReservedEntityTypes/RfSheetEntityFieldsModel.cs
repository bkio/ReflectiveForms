// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using Newtonsoft.Json;
using ReflectiveForms.Core.Attributes.Fields;

namespace ReflectiveForms.Core.Models.ReservedEntityTypes;

public class RfSheetEntityFieldsModel : SharableEntityFieldsModel
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
}
