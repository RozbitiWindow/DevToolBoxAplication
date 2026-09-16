using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Text tab: case conversion, line operations, escaping/encoding and live
/// statistics. Buttons are wired by name from the Inspector list so adding an
/// operation is one entry + one case in Apply().
/// </summary>
public class TextToolsPanel : MonoBehaviour
{
    [Serializable]
    public class Operation
    {
        public string id;
        public Button button;
    }

    [SerializeField] private TMP_InputField input;
    [SerializeField] private TMP_Text output;
    [SerializeField] private ScrollRect outputScroll;
    [SerializeField] private TMP_Text stats;
    [SerializeField] private TMP_Text status;
    [SerializeField] private Button copyButton;
    [SerializeField] private Button useOutputButton;
    [SerializeField] private Button clearButton;
    [SerializeField] private Operation[] operations;

    private const string ColorErr = "#FF7A7A";
    private const string ColorDim = "#9A8FBF";
    private string rawOutput = "";

    private void Awake()
    {
        if (input != null) input.onValueChanged.AddListener(_ => UpdateStats());
        if (copyButton != null) copyButton.onClick.AddListener(() => { if (rawOutput.Length > 0) GUIUtility.systemCopyBuffer = rawOutput; });
        if (useOutputButton != null) useOutputButton.onClick.AddListener(() => { if (input != null && rawOutput.Length > 0) input.text = rawOutput; });
        if (clearButton != null) clearButton.onClick.AddListener(() => { if (input != null) input.text = ""; SetOutput(""); SetStatus(""); });

        if (operations != null)
            foreach (Operation op in operations)
            {
                if (op.button == null || string.IsNullOrEmpty(op.id)) continue;
                string id = op.id;
                op.button.onClick.AddListener(() => Apply(id));
            }
        UpdateStats();
    }

    public void Apply(string id)
    {
        string text = input != null ? input.text : "";
        try
        {
            string result;
            switch (id)
            {
                case "upper": result = text.ToUpperInvariant(); break;
                case "lower": result = text.ToLowerInvariant(); break;
                case "title": result = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(text.ToLowerInvariant()); break;
                case "camel": result = JoinWords(text, (w, i) => i == 0 ? w.ToLowerInvariant() : Cap(w), ""); break;
                case "pascal": result = JoinWords(text, (w, i) => Cap(w), ""); break;
                case "snake": result = JoinWords(text, (w, i) => w.ToLowerInvariant(), "_"); break;
                case "kebab": result = JoinWords(text, (w, i) => w.ToLowerInvariant(), "-"); break;
                case "constant": result = JoinWords(text, (w, i) => w.ToUpperInvariant(), "_"); break;
                case "trim": result = string.Join("\n", Lines(text).Select(l => l.Trim())); break;
                case "sort": result = string.Join("\n", Lines(text).OrderBy(l => l, StringComparer.OrdinalIgnoreCase)); break;
                case "dedupe": result = string.Join("\n", Lines(text).Distinct()); break;
                case "reverse": result = string.Join("\n", Lines(text).Reverse()); break;
                case "removeEmpty": result = string.Join("\n", Lines(text).Where(l => l.Trim().Length > 0)); break;
                case "jsonEscape": result = JsonEscape(text); break;
                case "jsonUnescape": result = JsonUnescape(text); break;
                case "urlEncode": result = Uri.EscapeDataString(text); break;
                case "urlDecode": result = Uri.UnescapeDataString(text.Replace("+", " ")); break;
                case "base64Encode": result = Convert.ToBase64String(Encoding.UTF8.GetBytes(text)); break;
                case "base64Decode": result = Encoding.UTF8.GetString(Convert.FromBase64String(text.Trim())); break;
                case "htmlEscape": result = System.Net.WebUtility.HtmlEncode(text); break;
                case "htmlUnescape": result = System.Net.WebUtility.HtmlDecode(text); break;
                case "slug": result = Regex.Replace(RemoveDiacritics(text).ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-'); break;
                default: SetStatus("Unknown operation '" + id + "'", ColorErr); return;
            }
            SetOutput(result);
            SetStatus(id + "  -  " + result.Length + " characters", ColorDim);
            if (AppSettings.AutoCopy && result.Length > 0) GUIUtility.systemCopyBuffer = result;
        }
        catch (Exception e)
        {
            SetStatus(e.Message, ColorErr);
        }
    }

    // ---------- helpers ----------

    private static IEnumerable<string> Lines(string text) => text.Replace("\r", "").Split('\n');

    private static string Cap(string w) => w.Length == 0 ? w : char.ToUpperInvariant(w[0]) + w.Substring(1).ToLowerInvariant();

    // Splits on whitespace, punctuation and camelCase boundaries, then rejoins.
    private static string JoinWords(string text, Func<string, int, string> transform, string separator)
    {
        string spaced = Regex.Replace(text, "([a-z0-9])([A-Z])", "$1 $2");
        string[] words = Regex.Split(spaced, "[^A-Za-z0-9]+").Where(w => w.Length > 0).ToArray();
        var sb = new StringBuilder();
        for (int i = 0; i < words.Length; i++)
        {
            if (i > 0) sb.Append(separator);
            sb.Append(transform(words[i], i));
        }
        return sb.ToString();
    }

    private static string RemoveDiacritics(string text)
    {
        string normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (char c in normalized)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private static string JsonEscape(string s)
    {
        var sb = new StringBuilder();
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default: if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4")); else sb.Append(c); break;
            }
        }
        return sb.ToString();
    }

    private static string JsonUnescape(string s)
    {
        string t = s.Trim();
        if (t.Length >= 2 && t[0] == '"' && t[t.Length - 1] == '"') t = t.Substring(1, t.Length - 2);
        var node = JsonTool.Parse("\"" + t + "\"") as JsonTool.StringNode;
        return node != null ? node.Value : s;
    }

    private void UpdateStats()
    {
        if (stats == null) return;
        string text = input != null ? input.text : "";
        int chars = text.Length;
        int noSpaces = text.Count(c => !char.IsWhiteSpace(c));
        int words = Regex.Matches(text, @"\S+").Count;
        int lines = text.Length == 0 ? 0 : Lines(text).Count();
        int bytes = Encoding.UTF8.GetByteCount(text);
        stats.text = "<color=" + ColorDim + ">" + chars + " chars  |  " + noSpaces + " without spaces  |  " + words + " words  |  " + lines + " lines  |  " + bytes + " bytes UTF-8</color>";
    }

    private void SetOutput(string text)
    {
        rawOutput = text ?? "";
        if (output == null) return;
        output.text = rawOutput.Replace("<", "<​"); // don't let user text act as rich-text tags
        if (outputScroll != null) outputScroll.verticalNormalizedPosition = 1f;
    }

    private void SetStatus(string message, string color = null)
    {
        if (status == null) return;
        status.text = string.IsNullOrEmpty(color) ? message : "<color=" + color + ">" + message + "</color>";
    }
}
