using Microsoft.AspNetCore.Mvc;
using TerraGui.Web.Models;
using TerraGui.Web.Services;

namespace TerraGui.Web.Controllers;

[ApiController]
[Route("api/terraform")]
public class TerraformController : ControllerBase
{
    private readonly IHclParser _parser;
    private readonly ITfVarsGenerator _generator;
    private readonly ILogger<TerraformController> _logger;

    public TerraformController(
        IHclParser parser,
        ITfVarsGenerator generator,
        ILogger<TerraformController> logger)
    {
        _parser = parser;
        _generator = generator;
        _logger = logger;
    }

    /// <summary>
    /// Parse uploaded .tf and .tfvars files and return variable definitions + example values.
    /// </summary>
    [HttpPost("parse")]
    [RequestSizeLimit(10 * 1024 * 1024)] // 10 MB
    public async Task<IActionResult> Parse(IFormFileCollection files)
    {
        if (files == null || files.Count == 0)
        {
            // Also try the request form files
            files = Request.Form.Files;
        }

        if (files == null || files.Count == 0)
            return BadRequest(new { error = "No files uploaded." });

        var project = new ParsedProject();

        foreach (var file in files)
        {
            if (file.Length == 0) continue;

            string content;
            try
            {
                using var reader = new StreamReader(file.OpenReadStream());
                content = await reader.ReadToEndAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read file {FileName}", file.FileName);
                project.Warnings.Add($"Could not read file '{file.FileName}': {ex.Message}");
                continue;
            }

            string lowerName = file.FileName.ToLowerInvariant();

            if (lowerName.EndsWith("variables.tf") || lowerName == "variables.tf")
            {
                try
                {
                    var vars = _parser.ParseVariables(content);
                    project.Variables.AddRange(vars);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse variables.tf");
                    project.Warnings.Add($"Error parsing '{file.FileName}': {ex.Message}");
                }
            }
            else if (lowerName.EndsWith(".tf") && !lowerName.EndsWith("variables.tf"))
            {
                // Could be main.tf or others — try to parse any variable blocks
                try
                {
                    var vars = _parser.ParseVariables(content);
                    if (vars.Count > 0)
                        project.Variables.AddRange(vars);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse {FileName}", file.FileName);
                }
            }
            else if (lowerName.EndsWith(".tfvars") || lowerName.EndsWith(".tfvars.example"))
            {
                try
                {
                    var examples = _parser.ParseTfVars(content);
                    foreach (var kv in examples)
                        project.ExampleValues[kv.Key] = kv.Value;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse tfvars file {FileName}", file.FileName);
                    project.Warnings.Add($"Error parsing '{file.FileName}': {ex.Message}");
                }
            }
        }

        if (project.Variables.Count == 0 && project.ExampleValues.Count == 0)
        {
            project.Warnings.Add("No variable definitions found. Make sure you upload a variables.tf file.");
        }

        // Build the response: variables with their example values merged in
        var response = new
        {
            variables = project.Variables.Select(v => new
            {
                name = v.Name,
                type = v.Type,
                description = v.Description,
                defaultValue = v.Default,
                sensitive = v.Sensitive,
                nullable = v.Nullable,
                required = v.Required,
                inputType = v.InputType,
                elementType = v.ElementType,
                objectAttributes = v.ObjectAttributes,
                validations = v.Validations.Select(val => new
                {
                    condition = val.Condition,
                    errorMessage = val.ErrorMessage
                }),
                exampleValue = project.ExampleValues.TryGetValue(v.Name, out var ex) ? ex : null
            }),
            warnings = project.Warnings
        };

        return Ok(response);
    }

    /// <summary>
    /// Generate a terraform.tfvars file from variable values.
    /// </summary>
    [HttpPost("generate")]
    public IActionResult Generate([FromBody] GenerateRequest request)
    {
        if (request == null)
            return BadRequest(new { error = "Request body is required." });

        if (request.Variables == null || request.Variables.Count == 0)
            return BadRequest(new { error = "No variable definitions provided." });

        if (request.Values == null)
            request.Values = new Dictionary<string, string>();

        try
        {
            string content = _generator.Generate(request.Variables, request.Values);

            if (request.Download)
            {
                byte[] bytes = System.Text.Encoding.UTF8.GetBytes(content);
                return File(bytes, "text/plain", "terraform.tfvars");
            }

            return Ok(new { content });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating tfvars");
            return StatusCode(500, new { error = "An error occurred while generating the file." });
        }
    }
}

public class GenerateRequest
{
    public List<TerraformVariable> Variables { get; set; } = new();
    public Dictionary<string, string> Values { get; set; } = new();
    public bool Download { get; set; } = false;
}
