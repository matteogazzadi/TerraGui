using TerraGui.Web.Services;
using Xunit;

namespace TerraGui.Tests;

public class HclParserTests
{
    private readonly HclParser _parser = new();

    // ---- ParseVariables ----------------------------------------

    [Fact]
    public void ParseVariables_SimpleString_ParsesName()
    {
        const string hcl = """
            variable "region" {
              type = string
            }
            """;
        var vars = _parser.ParseVariables(hcl);
        Assert.Single(vars);
        Assert.Equal("region", vars[0].Name);
    }

    [Fact]
    public void ParseVariables_SimpleString_HasCorrectType()
    {
        const string hcl = """
            variable "region" {
              type        = string
              description = "AWS region"
              default     = "us-east-1"
            }
            """;
        var vars = _parser.ParseVariables(hcl);
        Assert.Equal("string", vars[0].Type);
        Assert.Equal("AWS region", vars[0].Description);
        Assert.Equal("\"us-east-1\"", vars[0].Default);
    }

    [Fact]
    public void ParseVariables_RequiredVariable_NoDefault()
    {
        const string hcl = """
            variable "db_password" {
              type      = string
              sensitive = true
            }
            """;
        var vars = _parser.ParseVariables(hcl);
        Assert.True(vars[0].Required);
        Assert.Null(vars[0].Default);
        Assert.True(vars[0].Sensitive);
    }

    [Fact]
    public void ParseVariables_NumberType()
    {
        const string hcl = """
            variable "instance_count" {
              type    = number
              default = 3
            }
            """;
        var vars = _parser.ParseVariables(hcl);
        Assert.Equal("number", vars[0].Type);
        Assert.Equal("3", vars[0].Default);
    }

    [Fact]
    public void ParseVariables_BoolType()
    {
        const string hcl = """
            variable "enable_monitoring" {
              type    = bool
              default = true
            }
            """;
        var vars = _parser.ParseVariables(hcl);
        Assert.Equal("bool", vars[0].Type);
        Assert.Equal("true", vars[0].Default);
    }

    [Fact]
    public void ParseVariables_ListOfStrings()
    {
        const string hcl = """
            variable "availability_zones" {
              type    = list(string)
              default = ["us-east-1a", "us-east-1b"]
            }
            """;
        var vars = _parser.ParseVariables(hcl);
        Assert.Equal("list(string)", vars[0].Type);
        Assert.Equal("list", vars[0].InputType);
        Assert.Equal("string", vars[0].ElementType);
    }

    [Fact]
    public void ParseVariables_MapOfStrings()
    {
        const string hcl = """
            variable "tags" {
              type    = map(string)
              default = {}
            }
            """;
        var vars = _parser.ParseVariables(hcl);
        Assert.Equal("map(string)", vars[0].Type);
        Assert.Equal("map", vars[0].InputType);
    }

    [Fact]
    public void ParseVariables_ObjectType_ParsesAttributes()
    {
        const string hcl = """
            variable "db_config" {
              type = object({
                host = string
                port = number
                name = string
              })
            }
            """;
        var vars = _parser.ParseVariables(hcl);
        Assert.Equal("object", vars[0].InputType);
        Assert.True(vars[0].ObjectAttributes.ContainsKey("host"));
        Assert.True(vars[0].ObjectAttributes.ContainsKey("port"));
        Assert.True(vars[0].ObjectAttributes.ContainsKey("name"));
        Assert.Equal("string", vars[0].ObjectAttributes["host"]);
        Assert.Equal("number", vars[0].ObjectAttributes["port"]);
    }

    [Fact]
    public void ParseVariables_SensitiveFlag()
    {
        const string hcl = """
            variable "api_key" {
              type      = string
              sensitive = true
            }
            """;
        var vars = _parser.ParseVariables(hcl);
        Assert.True(vars[0].Sensitive);
        Assert.Equal("password", vars[0].InputType);
    }

    [Fact]
    public void ParseVariables_ValidationBlock()
    {
        const string hcl = """
            variable "environment" {
              type = string
              validation {
                condition     = contains(["dev", "prod"], var.environment)
                error_message = "Must be dev or prod."
              }
            }
            """;
        var vars = _parser.ParseVariables(hcl);
        Assert.Single(vars[0].Validations);
        Assert.Equal("Must be dev or prod.", vars[0].Validations[0].ErrorMessage);
    }

    [Fact]
    public void ParseVariables_MultipleVariables()
    {
        const string hcl = """
            variable "region" {
              type = string
            }
            variable "count" {
              type = number
            }
            variable "enabled" {
              type = bool
            }
            """;
        var vars = _parser.ParseVariables(hcl);
        Assert.Equal(3, vars.Count);
        Assert.Equal("region", vars[0].Name);
        Assert.Equal("count", vars[1].Name);
        Assert.Equal("enabled", vars[2].Name);
    }

    [Fact]
    public void ParseVariables_InlineComments_Ignored()
    {
        const string hcl = """
            # This is a comment
            variable "region" {
              type        = string  # inline comment
              // another comment
              description = "The region"
            }
            """;
        var vars = _parser.ParseVariables(hcl);
        Assert.Single(vars);
        Assert.Equal("The region", vars[0].Description);
    }

    [Fact]
    public void ParseVariables_BlockComments_Ignored()
    {
        const string hcl = """
            /* block comment */
            variable "region" {
              type = string
              /* another block comment */
              default = "eu-west-1"
            }
            """;
        var vars = _parser.ParseVariables(hcl);
        Assert.Single(vars);
        Assert.Equal("\"eu-west-1\"", vars[0].Default);
    }

    [Fact]
    public void ParseVariables_NullableFlag()
    {
        const string hcl = """
            variable "optional_name" {
              type     = string
              nullable = false
              default  = null
            }
            """;
        var vars = _parser.ParseVariables(hcl);
        Assert.False(vars[0].Nullable);
        Assert.Equal("null", vars[0].Default);
    }

    [Fact]
    public void ParseVariables_SetType()
    {
        const string hcl = """
            variable "allowed_ips" {
              type = set(string)
            }
            """;
        var vars = _parser.ParseVariables(hcl);
        Assert.Equal("set(string)", vars[0].Type);
        Assert.Equal("list", vars[0].InputType);
    }

    // ---- ParseTfVars -------------------------------------------

    [Fact]
    public void ParseTfVars_StringValue()
    {
        const string tfvars = """
            region = "us-east-1"
            """;
        var vals = _parser.ParseTfVars(tfvars);
        Assert.True(vals.ContainsKey("region"));
        Assert.Equal("\"us-east-1\"", vals["region"]);
    }

    [Fact]
    public void ParseTfVars_NumberValue()
    {
        const string tfvars = "instance_count = 5";
        var vals = _parser.ParseTfVars(tfvars);
        Assert.Equal("5", vals["instance_count"]);
    }

    [Fact]
    public void ParseTfVars_BoolValue()
    {
        const string tfvars = "enabled = true";
        var vals = _parser.ParseTfVars(tfvars);
        Assert.Equal("true", vals["enabled"]);
    }

    [Fact]
    public void ParseTfVars_ListValue()
    {
        const string tfvars = """
            zones = ["us-east-1a", "us-east-1b"]
            """;
        var vals = _parser.ParseTfVars(tfvars);
        Assert.True(vals.ContainsKey("zones"));
        Assert.Contains("us-east-1a", vals["zones"]);
    }

    [Fact]
    public void ParseTfVars_MultipleValues()
    {
        const string tfvars = """
            region         = "us-east-1"
            instance_count = 3
            enabled        = false
            """;
        var vals = _parser.ParseTfVars(tfvars);
        Assert.Equal(3, vals.Count);
        Assert.True(vals.ContainsKey("region"));
        Assert.True(vals.ContainsKey("instance_count"));
        Assert.True(vals.ContainsKey("enabled"));
    }

    [Fact]
    public void ParseTfVars_Comments_Ignored()
    {
        const string tfvars = """
            # This is a comment
            region = "eu-west-1"
            // another comment
            """;
        var vals = _parser.ParseTfVars(tfvars);
        Assert.Single(vals);
        Assert.Equal("\"eu-west-1\"", vals["region"]);
    }
}
