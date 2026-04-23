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
     WysiwygEditor(label: "Content", instructions: "Write the note content.", mandatory: true)]{{AI_NOTE_ATTRIBUTES}}
    public string Content = "";

    [JsonProperty("priority"),
     Select(label: "Priority", instructions: "Set the importance level.",
         defaultValue: "medium", choices: new[] { "low : Low", "medium : Medium", "high : High" })]
    public string Priority = "medium";

    [JsonProperty("is_pinned"),
     Checkbox(label: "Pinned", instructions: "Pin this note to the top.", defaultValue: false)]
    public bool IsPinned;
}
