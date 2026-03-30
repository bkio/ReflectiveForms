using Newtonsoft.Json;
using ReflectiveForms.Core.Attributes;
using ReflectiveForms.Core.Attributes.Fields;
using ReflectiveForms.Core.Models;

/// <summary>
/// Sample entity model — a simple Note with content, priority, and a pinned flag.
/// Add your own entity models in this folder following this pattern.
/// </summary>
public class NoteModel : EntityFieldsModel
{
    [JsonProperty("content"),
     WysiwygEditor(label: "Content", mandatory: true)]
    public string Content = "";

    [JsonProperty("priority"),
     Select(label: "Priority", mandatory: true, choices: ["low", "medium", "high"], defaultValue: "medium")]
    public string Priority = "medium";

    [JsonProperty("is_pinned"),
     Checkbox(label: "Pinned", defaultValue: false)]
    public bool IsPinned;
}
