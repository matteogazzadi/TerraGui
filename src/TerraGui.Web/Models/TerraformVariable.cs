namespace TerraGui.Web.Models;

/// <summary>
/// Represents a single Terraform variable parsed from variables.tf
/// </summary>
public class TerraformVariable
{
    /// <summary>Variable name</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Type expression (string, number, bool, list(string), map(string), object({...}), etc.)</summary>
    public string Type { get; set; } = "any";

    /// <summary>Description from the description attribute</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Default value as a raw string. Null means no default was specified (required variable).
    /// The string "null" means explicit null was set.
    /// </summary>
    public string? Default { get; set; }

    /// <summary>Whether the variable is sensitive</summary>
    public bool Sensitive { get; set; }

    /// <summary>Whether the variable is nullable (defaults to true in Terraform)</summary>
    public bool Nullable { get; set; } = true;

    /// <summary>Whether the variable is required (no default specified)</summary>
    public bool Required => Default is null;

    /// <summary>Validation blocks</summary>
    public List<TerraformValidation> Validations { get; set; } = new();

    /// <summary>The inferred UI input type based on the Type field</summary>
    public string InputType => ResolveInputType();

    private string ResolveInputType()
    {
        var t = Type.Trim().ToLowerInvariant();
        if (t == "string") return Sensitive ? "password" : "text";
        if (t == "number") return "number";
        if (t == "bool") return "bool";
        if (t.StartsWith("list(") || t.StartsWith("set(")) return "list";
        if (t.StartsWith("map(")) return "map";
        if (t.StartsWith("object(")) return "object";
        if (t.StartsWith("tuple(")) return "list";
        return "any"; // any or unknown → textarea
    }

    /// <summary>
    /// For object types, parsed attribute names and their types.
    /// Only populated when Type starts with "object("
    /// </summary>
    public Dictionary<string, string> ObjectAttributes { get; set; } = new();

    /// <summary>
    /// For list/set types, the inner element type expression
    /// </summary>
    public string? ElementType { get; set; }
}

/// <summary>
/// A validation block on a Terraform variable
/// </summary>
public class TerraformValidation
{
    public string Condition { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
}
