// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using Newtonsoft.Json;
using ReflectiveForms.Core.Attributes.Fields;

namespace ReflectiveForms.Core.Models.ReservedEntityTypes;

public class MediaEntityFieldsModel : EntityFieldsModel
{
    [JsonProperty("media_source"),
     MediaSourceBase64(
         label: "Media",
         instructions: "",
         mandatory: true)]
    public string MediaSource { get; set; } = "";

    [JsonProperty("media_link_150px")]
    public string MediaLink150Px { get; init; } = "";

    [JsonProperty("media_link_300px")]
    public string MediaLink300Px { get; init; } = "";

    [JsonProperty("media_link_512px")]
    public string MediaLink512Px { get; init; } = "";

    [JsonProperty("media_link_1024px")]
    public string MediaLink1024Px { get; init; } = "";
}
