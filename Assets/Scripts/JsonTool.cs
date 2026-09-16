using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

/// <summary>
/// Small strict JSON parser + serializer. Parses into a lightweight tree so it
/// can report the exact line/column of a syntax error and re-emit the document
/// pretty-printed or minified, preserving key order and number formatting.
/// </summary>
public static class JsonTool
{
    public class JsonError : Exception
    {
        public int Line { get; }
        public int Column { get; }
        public JsonError(string message, int line, int column) : base(message)
        {
            Line = line;
            Column = column;
        }
    }

    // ---------- tree ----------

    public abstract class Node { }
    public sealed class ObjectNode : Node { public readonly List<KeyValuePair<string, Node>> Members = new List<KeyValuePair<string, Node>>(); }
    public sealed class ArrayNode : Node { public readonly List<Node> Items = new List<Node>(); }
    public sealed class StringNode : Node { public string Value; }
    public sealed class NumberNode : Node { public string Raw; }   // kept as written (no float round-trip loss)
    public sealed class BoolNode : Node { public bool Value; }
    public sealed class NullNode : Node { }

    // ---------- public API ----------

    public static Node Parse(string text)
    {
        var p = new Parser(text ?? "");
        p.SkipWhitespace();
        if (p.AtEnd) throw p.Error("Empty input");
        Node root = p.ParseValue();
        p.SkipWhitespace();
        if (!p.AtEnd) throw p.Error("Unexpected content after the end of the JSON value");
        return root;
    }

    public static string Format(string text, int indent = 4) => Serialize(Parse(text), indent);
    public static string Minify(string text) => Serialize(Parse(text), 0);

    /// <summary>Counts objects/arrays/keys for the status line.</summary>
    public static string Describe(Node node)
    {
        int objects = 0, arrays = 0, keys = 0, values = 0;
        Walk(node, ref objects, ref arrays, ref keys, ref values);
        return objects + " objects, " + arrays + " arrays, " + keys + " keys, " + values + " values";
    }

    private static void Walk(Node node, ref int objects, ref int arrays, ref int keys, ref int values)
    {
        switch (node)
        {
            case ObjectNode o:
                objects++;
                foreach (var m in o.Members) { keys++; Walk(m.Value, ref objects, ref arrays, ref keys, ref values); }
                break;
            case ArrayNode a:
                arrays++;
                foreach (var i in a.Items) Walk(i, ref objects, ref arrays, ref keys, ref values);
                break;
            default:
                values++;
                break;
        }
    }

    // ---------- serializer ----------

    public static string Serialize(Node node, int indent)
    {
        var sb = new StringBuilder();
        Write(sb, node, indent, 0);
        return sb.ToString();
    }

    private static void Write(StringBuilder sb, Node node, int indent, int depth)
    {
        bool pretty = indent > 0;
        switch (node)
        {
            case ObjectNode o:
                if (o.Members.Count == 0) { sb.Append("{}"); return; }
                sb.Append('{');
                for (int i = 0; i < o.Members.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    if (pretty) NewLine(sb, indent, depth + 1);
                    WriteString(sb, o.Members[i].Key);
                    sb.Append(pretty ? ": " : ":");
                    Write(sb, o.Members[i].Value, indent, depth + 1);
                }
                if (pretty) NewLine(sb, indent, depth);
                sb.Append('}');
                break;

            case ArrayNode a:
                if (a.Items.Count == 0) { sb.Append("[]"); return; }
                sb.Append('[');
                for (int i = 0; i < a.Items.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    if (pretty) NewLine(sb, indent, depth + 1);
                    Write(sb, a.Items[i], indent, depth + 1);
                }
                if (pretty) NewLine(sb, indent, depth);
                sb.Append(']');
                break;

            case StringNode s: WriteString(sb, s.Value); break;
            case NumberNode n: sb.Append(n.Raw); break;
            case BoolNode b: sb.Append(b.Value ? "true" : "false"); break;
            case NullNode _: sb.Append("null"); break;
        }
    }

    private static void NewLine(StringBuilder sb, int indent, int depth)
    {
        sb.Append('\n').Append(' ', indent * depth);
    }

    private static void WriteString(StringBuilder sb, string s)
    {
        sb.Append('"');
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
    }

    // ---------- parser ----------

    private class Parser
    {
        private readonly string s;
        private int pos;

        public Parser(string text) { s = text; }
        public bool AtEnd => pos >= s.Length;

        public JsonError Error(string message)
        {
            int line = 1, col = 1;
            for (int i = 0; i < pos && i < s.Length; i++)
            {
                if (s[i] == '\n') { line++; col = 1; }
                else col++;
            }
            return new JsonError(message, line, col);
        }

        public void SkipWhitespace()
        {
            while (pos < s.Length && (s[pos] == ' ' || s[pos] == '\t' || s[pos] == '\n' || s[pos] == '\r')) pos++;
        }

        public Node ParseValue()
        {
            SkipWhitespace();
            if (AtEnd) throw Error("Unexpected end of input, expected a value");
            char c = s[pos];
            switch (c)
            {
                case '{': return ParseObject();
                case '[': return ParseArray();
                case '"': return new StringNode { Value = ParseString() };
                case 't': ExpectWord("true"); return new BoolNode { Value = true };
                case 'f': ExpectWord("false"); return new BoolNode { Value = false };
                case 'n': ExpectWord("null"); return new NullNode();
                case '\'': throw Error("Strings must use double quotes");
                default:
                    if (c == '-' || (c >= '0' && c <= '9')) return ParseNumber();
                    throw Error("Unexpected character '" + c + "'");
            }
        }

        private ObjectNode ParseObject()
        {
            var obj = new ObjectNode();
            pos++; // {
            SkipWhitespace();
            if (Peek() == '}') { pos++; return obj; }

            while (true)
            {
                SkipWhitespace();
                if (AtEnd) throw Error("Unexpected end of input inside object");
                if (Peek() == '}') throw Error("Trailing comma before '}'");
                if (Peek() != '"') throw Error("Expected a string key in double quotes");
                string key = ParseString();
                SkipWhitespace();
                if (Peek() != ':') throw Error("Expected ':' after key \"" + key + "\"");
                pos++;
                Node value = ParseValue();
                obj.Members.Add(new KeyValuePair<string, Node>(key, value));
                SkipWhitespace();
                if (AtEnd) throw Error("Unexpected end of input, expected ',' or '}'");
                char c = s[pos++];
                if (c == '}') return obj;
                if (c != ',') { pos--; throw Error("Expected ',' or '}' after value"); }
            }
        }

        private ArrayNode ParseArray()
        {
            var arr = new ArrayNode();
            pos++; // [
            SkipWhitespace();
            if (Peek() == ']') { pos++; return arr; }

            while (true)
            {
                SkipWhitespace();
                if (AtEnd) throw Error("Unexpected end of input inside array");
                if (Peek() == ']') throw Error("Trailing comma before ']'");
                arr.Items.Add(ParseValue());
                SkipWhitespace();
                if (AtEnd) throw Error("Unexpected end of input, expected ',' or ']'");
                char c = s[pos++];
                if (c == ']') return arr;
                if (c != ',') { pos--; throw Error("Expected ',' or ']' after array item"); }
            }
        }

        private string ParseString()
        {
            pos++; // opening quote
            var sb = new StringBuilder();
            while (true)
            {
                if (AtEnd) throw Error("Unterminated string");
                char c = s[pos++];
                if (c == '"') return sb.ToString();
                if (c == '\n') { pos--; throw Error("Line break inside string (use \\n)"); }
                if (c != '\\') { sb.Append(c); continue; }

                if (AtEnd) throw Error("Unterminated escape sequence");
                char e = s[pos++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (pos + 4 > s.Length) throw Error("Incomplete \\u escape");
                        string hex = s.Substring(pos, 4);
                        if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
                            throw Error("Invalid \\u escape '" + hex + "'");
                        sb.Append((char)code);
                        pos += 4;
                        break;
                    default:
                        pos--;
                        throw Error("Invalid escape '\\" + e + "'");
                }
            }
        }

        private NumberNode ParseNumber()
        {
            int start = pos;
            if (Peek() == '-') pos++;
            if (AtEnd || !char.IsDigit(s[pos])) throw Error("Expected digits after '-'");
            if (s[pos] == '0') pos++;
            else while (!AtEnd && char.IsDigit(s[pos])) pos++;

            if (!AtEnd && s[pos] == '.')
            {
                pos++;
                if (AtEnd || !char.IsDigit(s[pos])) throw Error("Expected digits after decimal point");
                while (!AtEnd && char.IsDigit(s[pos])) pos++;
            }
            if (!AtEnd && (s[pos] == 'e' || s[pos] == 'E'))
            {
                pos++;
                if (!AtEnd && (s[pos] == '+' || s[pos] == '-')) pos++;
                if (AtEnd || !char.IsDigit(s[pos])) throw Error("Expected digits in exponent");
                while (!AtEnd && char.IsDigit(s[pos])) pos++;
            }
            if (!AtEnd && char.IsLetterOrDigit(s[pos])) throw Error("Invalid number");
            return new NumberNode { Raw = s.Substring(start, pos - start) };
        }

        private void ExpectWord(string word)
        {
            if (string.CompareOrdinal(s, pos, word, 0, word.Length) != 0)
                throw Error("Unexpected token, did you mean '" + word + "'?");
            pos += word.Length;
            if (!AtEnd && char.IsLetterOrDigit(s[pos])) throw Error("Unexpected characters after '" + word + "'");
        }

        private char Peek() => AtEnd ? '\0' : s[pos];
    }
}
