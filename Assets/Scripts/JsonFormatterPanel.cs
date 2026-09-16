using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Formatters tab: validate, pretty-print and minify JSON. Errors show the
/// line and column so they are easy to find in the input.
/// </summary>
public class JsonFormatterPanel : MonoBehaviour
{
    [SerializeField] private TMP_InputField input;
    [SerializeField] private TMP_Text output;
    [SerializeField] private ScrollRect outputScroll;
    [SerializeField] private TMP_Text status;
    [SerializeField] private Button validateButton;
    [SerializeField] private Button formatButton;
    [SerializeField] private Button minifyButton;
    [SerializeField] private Button copyButton;
    [SerializeField] private Button clearButton;
    [SerializeField] private Button useOutputButton;   // move output back to input
    [SerializeField] private int indent = 4;

    private const string ColorOk = "#7AFFB0";
    private const string ColorErr = "#FF7A7A";

    private void Awake()
    {
        Instance = this;
        if (validateButton != null) validateButton.onClick.AddListener(Validate);
        if (formatButton != null) formatButton.onClick.AddListener(Format);
        if (minifyButton != null) minifyButton.onClick.AddListener(Minify);
        if (copyButton != null) copyButton.onClick.AddListener(() =>
        {
            if (output != null && !string.IsNullOrEmpty(output.text)) GUIUtility.systemCopyBuffer = output.text;
        });
        if (clearButton != null) clearButton.onClick.AddListener(() =>
        {
            if (input != null) input.text = "";
            SetOutput("");
            SetStatus("", null);
        });
        if (useOutputButton != null) useOutputButton.onClick.AddListener(() =>
        {
            if (input != null && output != null && !string.IsNullOrEmpty(output.text)) input.text = output.text;
        });
        if (input != null) input.onValueChanged.AddListener(_ => SetStatus("", null));
    }

    public static JsonFormatterPanel Instance { get; private set; }

    /// <summary>Loads text into the input and formats it (used by the HTTP client's "Open in formatter").</summary>
    public void SetInput(string text, bool formatNow)
    {
        if (input != null) input.text = text ?? "";
        if (formatNow) Format();
    }

    public void Validate()
    {
        if (!TryParse(out JsonTool.Node root)) return;
        SetStatus("Valid JSON  -  " + JsonTool.Describe(root), ColorOk);
    }

    public void Format()
    {
        if (!TryParse(out JsonTool.Node root)) return;
        SetOutput(JsonTool.Serialize(root, indent));
        SetStatus("Formatted  -  " + JsonTool.Describe(root), ColorOk);
        if (AppSettings.AutoCopy) GUIUtility.systemCopyBuffer = output.text;
    }

    public void Minify()
    {
        if (!TryParse(out JsonTool.Node root)) return;
        SetOutput(JsonTool.Serialize(root, 0));
        SetStatus("Minified  -  " + output.text.Length + " characters", ColorOk);
        if (AppSettings.AutoCopy) GUIUtility.systemCopyBuffer = output.text;
    }

    private bool TryParse(out JsonTool.Node root)
    {
        root = null;
        string text = input != null ? input.text : "";
        try
        {
            root = JsonTool.Parse(text);
            return true;
        }
        catch (JsonTool.JsonError e)
        {
            SetStatus("Line " + e.Line + ", column " + e.Column + ": " + e.Message, ColorErr);
            MoveCaret(text, e.Line, e.Column);
            return false;
        }
    }

    // Put the caret on the error so the user lands right where the problem is.
    private void MoveCaret(string text, int line, int column)
    {
        if (input == null) return;
        int index = 0, currentLine = 1;
        while (currentLine < line && index < text.Length)
        {
            if (text[index] == '\n') currentLine++;
            index++;
        }
        index = Mathf.Clamp(index + column - 1, 0, text.Length);
        input.ActivateInputField();
        input.caretPosition = index;
        input.selectionAnchorPosition = index;
        input.selectionFocusPosition = index;
    }

    private void SetOutput(string text)
    {
        if (output == null) return;
        output.text = text;
        if (outputScroll != null) outputScroll.verticalNormalizedPosition = 1f;
    }

    private void SetStatus(string message, string color)
    {
        if (status == null) return;
        status.text = string.IsNullOrEmpty(color) ? message : "<color=" + color + ">" + message + "</color>";
    }
}
