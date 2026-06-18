using System.Text;
using System.Text.RegularExpressions;
using TerraGui.Web.Models;

namespace TerraGui.Web.Services;

/// <summary>
/// Hand-written HCL parser for Terraform variables.tf files.
/// Supports: comments, multi-line values, nested types, validation blocks,
/// heredoc strings, quoted strings with escapes.
/// </summary>
public class HclParser : IHclParser
{
    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    public List<TerraformVariable> ParseVariables(string hclContent)
    {
        var tokens = Tokenize(hclContent);
        return ParseVariableBlocks(tokens);
    }

    public Dictionary<string, string> ParseTfVars(string tfVarsContent)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var tokens = Tokenize(tfVarsContent);
        int i = 0;
        while (i < tokens.Count)
        {
            // Skip any stray newlines
            if (tokens[i].Kind == TokenKind.Newline) { i++; continue; }

            if (tokens[i].Kind == TokenKind.Identifier || tokens[i].Kind == TokenKind.QuotedString)
            {
                string name = tokens[i].Value;
                i++;
                // expect '='
                if (i < tokens.Count && tokens[i].Kind == TokenKind.Equals)
                {
                    i++;
                    var (value, next) = ReadValue(tokens, i);
                    result[name] = value;
                    i = next;
                }
            }
            else
            {
                i++;
            }
        }
        return result;
    }

    // -------------------------------------------------------------------------
    // Tokenizer
    // -------------------------------------------------------------------------

    private enum TokenKind
    {
        Identifier,
        QuotedString,
        HeredocString,
        Number,
        OpenBrace,
        CloseBrace,
        OpenBracket,
        CloseBracket,
        OpenParen,
        CloseParen,
        Equals,
        Comma,
        Newline,
        EOF
    }

    private record Token(TokenKind Kind, string Value, int Line);

    private static List<Token> Tokenize(string src)
    {
        var tokens = new List<Token>();
        int pos = 0;
        int line = 1;
        int len = src.Length;

        while (pos < len)
        {
            char c = src[pos];

            // Skip single-line comments
            if (c == '#' || (c == '/' && pos + 1 < len && src[pos + 1] == '/'))
            {
                while (pos < len && src[pos] != '\n') pos++;
                continue;
            }

            // Skip block comments
            if (c == '/' && pos + 1 < len && src[pos + 1] == '*')
            {
                pos += 2;
                while (pos < len - 1 && !(src[pos] == '*' && src[pos + 1] == '/'))
                {
                    if (src[pos] == '\n') line++;
                    pos++;
                }
                pos += 2; // skip */
                continue;
            }

            // Newlines
            if (c == '\n')
            {
                tokens.Add(new Token(TokenKind.Newline, "\n", line));
                line++;
                pos++;
                continue;
            }

            // Carriage return (skip)
            if (c == '\r') { pos++; continue; }

            // Whitespace
            if (char.IsWhiteSpace(c)) { pos++; continue; }

            // Heredoc  <<EOF or <<-EOF
            if (c == '<' && pos + 1 < len && src[pos + 1] == '<')
            {
                pos += 2;
                bool stripLeading = pos < len && src[pos] == '-';
                if (stripLeading) pos++;
                // Read the marker
                var markerSb = new StringBuilder();
                while (pos < len && src[pos] != '\n' && src[pos] != '\r')
                {
                    markerSb.Append(src[pos]);
                    pos++;
                }
                string marker = markerSb.ToString().Trim();
                // Skip the newline after the marker
                if (pos < len && src[pos] == '\r') pos++;
                if (pos < len && src[pos] == '\n') { line++; pos++; }
                // Collect heredoc body
                var bodySb = new StringBuilder();
                while (pos < len)
                {
                    // Find end of line
                    int lineStart = pos;
                    var lineSb = new StringBuilder();
                    while (pos < len && src[pos] != '\n')
                    {
                        lineSb.Append(src[pos]);
                        pos++;
                    }
                    string rawLine = lineSb.ToString();
                    if (pos < len) { pos++; line++; } // consume newline

                    string trimmedLine = stripLeading ? rawLine.TrimStart() : rawLine;
                    if (trimmedLine == marker) break;
                    bodySb.Append(rawLine).Append('\n');
                }
                tokens.Add(new Token(TokenKind.HeredocString, bodySb.ToString(), line));
                continue;
            }

            // Quoted strings
            if (c == '"')
            {
                pos++; // skip opening quote
                var sb = new StringBuilder();
                while (pos < len && src[pos] != '"')
                {
                    if (src[pos] == '\\' && pos + 1 < len)
                    {
                        pos++;
                        switch (src[pos])
                        {
                            case 'n': sb.Append('\n'); break;
                            case 'r': sb.Append('\r'); break;
                            case 't': sb.Append('\t'); break;
                            case '"': sb.Append('"'); break;
                            case '\\': sb.Append('\\'); break;
                            case '$': sb.Append('$'); break;
                            default: sb.Append('\\'); sb.Append(src[pos]); break;
                        }
                        pos++;
                    }
                    else
                    {
                        if (src[pos] == '\n') line++;
                        sb.Append(src[pos]);
                        pos++;
                    }
                }
                pos++; // skip closing quote
                tokens.Add(new Token(TokenKind.QuotedString, sb.ToString(), line));
                continue;
            }

            // Numbers (including negative)
            if (char.IsDigit(c) || (c == '-' && pos + 1 < len && char.IsDigit(src[pos + 1])))
            {
                var sb = new StringBuilder();
                if (c == '-') { sb.Append(c); pos++; }
                while (pos < len && (char.IsDigit(src[pos]) || src[pos] == '.' || src[pos] == 'e' || src[pos] == 'E' || src[pos] == '+' || src[pos] == '-'))
                {
                    sb.Append(src[pos]);
                    pos++;
                }
                tokens.Add(new Token(TokenKind.Number, sb.ToString(), line));
                continue;
            }

            // Identifiers and keywords
            if (char.IsLetter(c) || c == '_')
            {
                var sb = new StringBuilder();
                while (pos < len && (char.IsLetterOrDigit(src[pos]) || src[pos] == '_' || src[pos] == '-'))
                {
                    sb.Append(src[pos]);
                    pos++;
                }
                tokens.Add(new Token(TokenKind.Identifier, sb.ToString(), line));
                continue;
            }

            // Punctuation
            switch (c)
            {
                case '{': tokens.Add(new Token(TokenKind.OpenBrace, "{", line)); pos++; break;
                case '}': tokens.Add(new Token(TokenKind.CloseBrace, "}", line)); pos++; break;
                case '[': tokens.Add(new Token(TokenKind.OpenBracket, "[", line)); pos++; break;
                case ']': tokens.Add(new Token(TokenKind.CloseBracket, "]", line)); pos++; break;
                case '(': tokens.Add(new Token(TokenKind.OpenParen, "(", line)); pos++; break;
                case ')': tokens.Add(new Token(TokenKind.CloseParen, ")", line)); pos++; break;
                case '=': tokens.Add(new Token(TokenKind.Equals, "=", line)); pos++; break;
                case ',': tokens.Add(new Token(TokenKind.Comma, ",", line)); pos++; break;
                default: pos++; break; // skip unknown chars
            }
        }

        tokens.Add(new Token(TokenKind.EOF, "", line));
        return tokens;
    }

    // -------------------------------------------------------------------------
    // Variable block parser
    // -------------------------------------------------------------------------

    private static List<TerraformVariable> ParseVariableBlocks(List<Token> tokens)
    {
        var variables = new List<TerraformVariable>();
        int i = 0;

        while (i < tokens.Count)
        {
            if (tokens[i].Kind == TokenKind.Identifier && tokens[i].Value == "variable")
            {
                i++; // skip "variable"
                // skip newlines
                while (i < tokens.Count && tokens[i].Kind == TokenKind.Newline) i++;

                if (i < tokens.Count && (tokens[i].Kind == TokenKind.QuotedString || tokens[i].Kind == TokenKind.Identifier))
                {
                    string varName = tokens[i].Value;
                    i++;
                    // skip newlines
                    while (i < tokens.Count && tokens[i].Kind == TokenKind.Newline) i++;

                    if (i < tokens.Count && tokens[i].Kind == TokenKind.OpenBrace)
                    {
                        i++; // skip '{'
                        var variable = new TerraformVariable { Name = varName };
                        i = ParseVariableBody(tokens, i, variable);
                        variables.Add(variable);
                    }
                }
            }
            else
            {
                i++;
            }
        }

        return variables;
    }

    private static int ParseVariableBody(List<Token> tokens, int i, TerraformVariable variable)
    {
        int depth = 1;

        while (i < tokens.Count && depth > 0)
        {
            if (tokens[i].Kind == TokenKind.Newline) { i++; continue; }

            if (tokens[i].Kind == TokenKind.CloseBrace)
            {
                depth--;
                i++;
                if (depth == 0) break;
                continue;
            }

            if (tokens[i].Kind == TokenKind.OpenBrace)
            {
                depth++;
                i++;
                continue;
            }

            // Attribute: identifier = value
            if (tokens[i].Kind == TokenKind.Identifier)
            {
                string attrName = tokens[i].Value;
                i++;

                // skip newlines
                while (i < tokens.Count && tokens[i].Kind == TokenKind.Newline) i++;

                if (i < tokens.Count && tokens[i].Kind == TokenKind.Equals)
                {
                    i++; // skip '='

                    switch (attrName)
                    {
                        case "type":
                            (variable.Type, i) = ReadTypeExpression(tokens, i);
                            break;
                        case "description":
                            var (desc, ni) = ReadValue(tokens, i);
                            variable.Description = desc.Trim('"');
                            i = ni;
                            break;
                        case "default":
                            var (def, di) = ReadValue(tokens, i);
                            variable.Default = def;
                            i = di;
                            break;
                        case "sensitive":
                            var (sens, si) = ReadValue(tokens, i);
                            variable.Sensitive = sens.Trim() == "true";
                            i = si;
                            break;
                        case "nullable":
                            var (nul, nuli) = ReadValue(tokens, i);
                            variable.Nullable = nul.Trim() != "false";
                            i = nuli;
                            break;
                        default:
                            // skip unknown attributes
                            var (_, ski) = ReadValue(tokens, i);
                            i = ski;
                            break;
                    }
                }
                else if (attrName == "validation" && i < tokens.Count && tokens[i].Kind == TokenKind.OpenBrace)
                {
                    i++; // skip '{'
                    var validation = new TerraformValidation();
                    i = ParseValidationBlock(tokens, i, validation);
                    variable.Validations.Add(validation);
                }
                else
                {
                    // not an assignment, skip
                }
            }
            else
            {
                i++;
            }
        }

        // Post-process object type attributes
        if (variable.Type.StartsWith("object(", StringComparison.OrdinalIgnoreCase))
        {
            variable.ObjectAttributes = ParseObjectAttributes(variable.Type);
        }
        else if (variable.Type.StartsWith("list(", StringComparison.OrdinalIgnoreCase) ||
                 variable.Type.StartsWith("set(", StringComparison.OrdinalIgnoreCase))
        {
            variable.ElementType = ExtractInnerType(variable.Type);
        }

        return i;
    }

    private static int ParseValidationBlock(List<Token> tokens, int i, TerraformValidation validation)
    {
        while (i < tokens.Count)
        {
            if (tokens[i].Kind == TokenKind.Newline) { i++; continue; }

            if (tokens[i].Kind == TokenKind.CloseBrace)
            {
                i++;
                break;
            }

            if (tokens[i].Kind == TokenKind.Identifier)
            {
                string attrName = tokens[i].Value;
                i++;
                while (i < tokens.Count && tokens[i].Kind == TokenKind.Newline) i++;

                if (i < tokens.Count && tokens[i].Kind == TokenKind.Equals)
                {
                    i++;
                    var (val, ni) = ReadValue(tokens, i);
                    if (attrName == "condition") validation.Condition = val;
                    else if (attrName == "error_message") validation.ErrorMessage = val.Trim('"');
                    i = ni;
                }
                else
                {
                    // skip
                }
            }
            else
            {
                i++;
            }
        }
        return i;
    }

    // -------------------------------------------------------------------------
    // Type expression reader
    // -------------------------------------------------------------------------

    private static (string type, int nextIndex) ReadTypeExpression(List<Token> tokens, int i)
    {
        // Skip newlines
        while (i < tokens.Count && tokens[i].Kind == TokenKind.Newline) i++;
        if (i >= tokens.Count) return ("any", i);

        var sb = new StringBuilder();

        // Could be a simple identifier (string, number, bool, any) or a complex one with parens
        if (tokens[i].Kind == TokenKind.Identifier)
        {
            sb.Append(tokens[i].Value);
            i++;

            // Check for '(' (list(...), map(...), object(...), etc.)
            if (i < tokens.Count && tokens[i].Kind == TokenKind.OpenParen)
            {
                int depth = 1;
                sb.Append('(');
                i++;
                while (i < tokens.Count && depth > 0)
                {
                    var t = tokens[i];
                    if (t.Kind == TokenKind.Newline)
                    {
                        // In object/tuple type bodies, newlines separate attributes just like commas.
                        // Emit a comma so ParseObjectAttributes can split on it correctly.
                        if (sb.Length > 0)
                        {
                            char last = sb[sb.Length - 1];
                            if (last != '{' && last != '(' && last != '[' && last != ',')
                                sb.Append(',');
                        }
                        i++;
                        continue;
                    }
                    if (t.Kind == TokenKind.OpenParen) { depth++; sb.Append('('); }
                    else if (t.Kind == TokenKind.CloseParen) { depth--; if (depth >= 0) sb.Append(')'); }
                    else if (t.Kind == TokenKind.OpenBrace) sb.Append('{');
                    else if (t.Kind == TokenKind.CloseBrace) sb.Append('}');
                    else if (t.Kind == TokenKind.OpenBracket) sb.Append('[');
                    else if (t.Kind == TokenKind.CloseBracket) sb.Append(']');
                    else if (t.Kind == TokenKind.Comma) sb.Append(", ");
                    else if (t.Kind == TokenKind.Equals) sb.Append(" = ");
                    else sb.Append(t.Value);
                    i++;
                }
            }
        }
        else if (tokens[i].Kind == TokenKind.QuotedString)
        {
            // Sometimes people write type = "string"
            sb.Append(tokens[i].Value);
            i++;
        }

        return (sb.ToString(), i);
    }

    // -------------------------------------------------------------------------
    // Generic value reader (reads one complete value from the token stream)
    // -------------------------------------------------------------------------

    private static (string value, int nextIndex) ReadValue(List<Token> tokens, int i)
    {
        // Skip newlines
        while (i < tokens.Count && tokens[i].Kind == TokenKind.Newline) i++;
        if (i >= tokens.Count) return ("", i);

        var t = tokens[i];

        switch (t.Kind)
        {
            case TokenKind.QuotedString:
                return ($"\"{EscapeString(t.Value)}\"", i + 1);

            case TokenKind.HeredocString:
                return (t.Value, i + 1);

            case TokenKind.Number:
                return (t.Value, i + 1);

            case TokenKind.Identifier:
                // true, false, null, or a reference
                return (t.Value, i + 1);

            case TokenKind.OpenBrace:
                return ReadBraceValue(tokens, i);

            case TokenKind.OpenBracket:
                return ReadBracketValue(tokens, i);

            default:
                return ("", i + 1);
        }
    }

    private static (string value, int nextIndex) ReadBraceValue(List<Token> tokens, int start)
    {
        // start points to '{'
        var sb = new StringBuilder();
        sb.Append('{');
        int i = start + 1;
        int depth = 1;

        while (i < tokens.Count && depth > 0)
        {
            var t = tokens[i];
            if (t.Kind == TokenKind.Newline) { sb.Append(' '); i++; continue; }
            if (t.Kind == TokenKind.OpenBrace) { depth++; sb.Append('{'); }
            else if (t.Kind == TokenKind.CloseBrace) { depth--; if (depth >= 0) sb.Append('}'); }
            else if (t.Kind == TokenKind.Equals) sb.Append(" = ");
            else if (t.Kind == TokenKind.Comma) sb.Append(", ");
            else if (t.Kind == TokenKind.QuotedString) sb.Append($"\"{EscapeString(t.Value)}\"");
            else sb.Append(t.Value);
            i++;
        }

        return (sb.ToString(), i);
    }

    private static (string value, int nextIndex) ReadBracketValue(List<Token> tokens, int start)
    {
        // start points to '['
        var sb = new StringBuilder();
        sb.Append('[');
        int i = start + 1;
        int depth = 1;

        while (i < tokens.Count && depth > 0)
        {
            var t = tokens[i];
            if (t.Kind == TokenKind.Newline) { sb.Append(' '); i++; continue; }
            if (t.Kind == TokenKind.OpenBracket) { depth++; sb.Append('['); }
            else if (t.Kind == TokenKind.CloseBracket) { depth--; if (depth >= 0) sb.Append(']'); }
            else if (t.Kind == TokenKind.OpenBrace) sb.Append('{');
            else if (t.Kind == TokenKind.CloseBrace) sb.Append('}');
            else if (t.Kind == TokenKind.Equals) sb.Append(" = ");
            else if (t.Kind == TokenKind.Comma) sb.Append(", ");
            else if (t.Kind == TokenKind.QuotedString) sb.Append($"\"{EscapeString(t.Value)}\"");
            else sb.Append(t.Value);
            i++;
        }

        return (sb.ToString(), i);
    }

    // -------------------------------------------------------------------------
    // Helpers for type parsing
    // -------------------------------------------------------------------------

    private static Dictionary<string, string> ParseObjectAttributes(string typeExpr)
    {
        // typeExpr: object({key1 = type1, key2 = type2, ...})
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Find the inner content between the outer braces
        int openBrace = typeExpr.IndexOf('{');
        if (openBrace < 0) return result;

        int closeBrace = FindMatchingBrace(typeExpr, openBrace);
        if (closeBrace < 0) return result;

        string inner = typeExpr[(openBrace + 1)..closeBrace].Trim();

        // Split by commas at depth 0
        var attrs = SplitAtDepthZero(inner, ',');
        foreach (var attr in attrs)
        {
            string trimmed = attr.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            // Find '=' at depth 0
            int eqIdx = FindEqualsAtDepthZero(trimmed);
            if (eqIdx < 0) continue;

            string attrName = trimmed[..eqIdx].Trim();
            string attrType = trimmed[(eqIdx + 1)..].Trim();

            // Remove optional() wrapper
            if (attrType.StartsWith("optional(", StringComparison.OrdinalIgnoreCase))
            {
                int innerStart = attrType.IndexOf('(');
                if (innerStart >= 0)
                {
                    int innerEnd = FindMatchingParen(attrType, innerStart);
                    if (innerEnd > innerStart)
                    {
                        string optContent = attrType[(innerStart + 1)..innerEnd];
                        // optional(type) or optional(type, default)
                        int commaIdx = FindCommaAtDepthZero(optContent);
                        attrType = commaIdx >= 0 ? optContent[..commaIdx].Trim() : optContent.Trim();
                    }
                }
            }

            result[attrName] = attrType;
        }

        return result;
    }

    private static string ExtractInnerType(string typeExpr)
    {
        int openParen = typeExpr.IndexOf('(');
        if (openParen < 0) return "any";
        int closeParen = FindMatchingParen(typeExpr, openParen);
        if (closeParen < 0) return "any";
        return typeExpr[(openParen + 1)..closeParen].Trim();
    }

    private static int FindMatchingBrace(string s, int openIdx)
    {
        int depth = 0;
        for (int i = openIdx; i < s.Length; i++)
        {
            if (s[i] == '{') depth++;
            else if (s[i] == '}') { depth--; if (depth == 0) return i; }
        }
        return -1;
    }

    private static int FindMatchingParen(string s, int openIdx)
    {
        int depth = 0;
        for (int i = openIdx; i < s.Length; i++)
        {
            if (s[i] == '(') depth++;
            else if (s[i] == ')') { depth--; if (depth == 0) return i; }
        }
        return -1;
    }

    private static int FindEqualsAtDepthZero(string s)
    {
        int depth = 0;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '(' || c == '{' || c == '[') depth++;
            else if (c == ')' || c == '}' || c == ']') depth--;
            else if (c == '=' && depth == 0) return i;
        }
        return -1;
    }

    private static int FindCommaAtDepthZero(string s)
    {
        int depth = 0;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '(' || c == '{' || c == '[') depth++;
            else if (c == ')' || c == '}' || c == ']') depth--;
            else if (c == ',' && depth == 0) return i;
        }
        return -1;
    }

    private static List<string> SplitAtDepthZero(string s, char separator)
    {
        var parts = new List<string>();
        int depth = 0;
        int start = 0;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '(' || c == '{' || c == '[') depth++;
            else if (c == ')' || c == '}' || c == ']') depth--;
            else if (c == separator && depth == 0)
            {
                parts.Add(s[start..i]);
                start = i + 1;
            }
        }
        if (start < s.Length) parts.Add(s[start..]);
        return parts;
    }

    private static string EscapeString(string s)
        => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
