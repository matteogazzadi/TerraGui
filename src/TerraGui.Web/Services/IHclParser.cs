using TerraGui.Web.Models;

namespace TerraGui.Web.Services;

public interface IHclParser
{
    /// <summary>
    /// Parse a variables.tf file content and return the list of variable definitions.
    /// </summary>
    List<TerraformVariable> ParseVariables(string hclContent);

    /// <summary>
    /// Parse a .tfvars or .tfvars.example file and return name→raw-value pairs.
    /// </summary>
    Dictionary<string, string> ParseTfVars(string tfVarsContent);
}
