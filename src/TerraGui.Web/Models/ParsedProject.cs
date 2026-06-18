namespace TerraGui.Web.Models;

/// <summary>
/// Result of parsing a collection of uploaded Terraform files
/// </summary>
public class ParsedProject
{
    /// <summary>All variables found in variables.tf</summary>
    public List<TerraformVariable> Variables { get; set; } = new();

    /// <summary>Pre-populated values from terraform.tfvars.example or .tfvars files</summary>
    public Dictionary<string, string> ExampleValues { get; set; } = new();

    /// <summary>Any warnings or non-fatal parse issues</summary>
    public List<string> Warnings { get; set; } = new();
}
