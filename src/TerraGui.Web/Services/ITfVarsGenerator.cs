using TerraGui.Web.Models;

namespace TerraGui.Web.Services;

public interface ITfVarsGenerator
{
    /// <summary>
    /// Generate a terraform.tfvars file content from the variable definitions and user-supplied values.
    /// </summary>
    /// <param name="variables">Variable definitions (used for type info, sensitivity flags)</param>
    /// <param name="values">Map of variable name to user-supplied value (as string)</param>
    /// <returns>Complete terraform.tfvars content as a string</returns>
    /// <exception cref="InvalidOperationException">Thrown when a required variable has no value</exception>
    string Generate(IEnumerable<TerraformVariable> variables, Dictionary<string, string> values);
}
