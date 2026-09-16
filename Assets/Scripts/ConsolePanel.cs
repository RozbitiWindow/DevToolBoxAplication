using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// In-app command line: runs shell commands through ShellRunner and shows
/// their output, plus (optionally) the app's own Debug.Log messages. Other
/// tools can write here via ConsolePanel.Write / RunCommand.
/// </summary>
public class ConsolePanel : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private TMP_InputField commandInput;
    [SerializeField] private TMP_Text output;
    [SerializeField] private ScrollRect scroll;
    [SerializeField] private TMP_Text promptLabel;
    [SerializeField] private Button runButton;
    [SerializeField] private Button clearButton;
    [SerializeField] private Button copyButton;
    [SerializeField] private Button stopButton;
    [SerializeField] private Toggle showAppLog;

    [Header("Behavior")]
    [SerializeField] private int maxLines = 600;
    [SerializeField] private int maxHistory = 50;

    public static ConsolePanel Instance { get; private set; }

    private readonly List<string> lines = new List<string>();
    private readonly List<string> history = new List<string>();
    private int historyIndex = -1;
    private string cwd;
    private ShellRunner.Job current;
    private bool dirty;
    private bool captureAppLog = true;

    private const string ColorCmd = "#C9B8FF";
    private const string ColorErr = "#FF7A7A";
    private const string ColorLog = "#8FD3FF";
    private const string ColorWarn = "#FFD27A";
    private const string ColorDim = "#9A8FBF";

    private void Awake()
    {
        Instance = this;
        cwd = Directory.GetCurrentDirectory();

        if (runButton != null) runButton.onClick.AddListener(SubmitInput);
        if (clearButton != null) clearButton.onClick.AddListener(Clear);
        if (copyButton != null) copyButton.onClick.AddListener(() => GUIUtility.systemCopyBuffer = string.Join("\n", lines));
        if (stopButton != null) stopButton.onClick.AddListener(Stop);
        if (showAppLog != null)
        {
            captureAppLog = showAppLog.isOn;
            showAppLog.onValueChanged.AddListener(v => captureAppLog = v);
        }
        if (commandInput != null)
        {
            commandInput.onSubmit.AddListener(_ => SubmitInput());
            commandInput.lineType = TMP_InputField.LineType.SingleLine;
        }

        Application.logMessageReceived += OnAppLog;
        UpdatePrompt();
        Write("DevToolBox console. Type a command and press Enter. 'cd <dir>' changes the working directory, 'cls' clears.", ColorDim);
    }

    private void OnDestroy()
    {
        Application.logMessageReceived -= OnAppLog;
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        // history navigation while the input has focus
        var keyboard = UnityEngine.InputSystem.Keyboard.current;
        if (keyboard != null && commandInput != null && commandInput.isFocused && history.Count > 0)
        {
            if (keyboard.upArrowKey.wasPressedThisFrame) Recall(-1);
            else if (keyboard.downArrowKey.wasPressedThisFrame) Recall(+1);
        }

        if (dirty)
        {
            dirty = false;
            if (output != null) output.text = string.Join("\n", lines);
            if (scroll != null) StartCoroutine(ScrollToBottom());
        }
    }

    // ---------- public API for other tools ----------

    /// <summary>Appends a line to the console (rich-text color optional).</summary>
    public void Write(string text, string colorHex = null)
    {
        if (text == null) return;
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            lines.Add(string.IsNullOrEmpty(colorHex) ? line : "<color=" + colorHex + ">" + line + "</color>");
        }
        if (lines.Count > maxLines) lines.RemoveRange(0, lines.Count - maxLines);
        dirty = true;
    }

    /// <summary>Runs a shell command, echoing it and its output to the console.</summary>
    public ShellRunner.Job RunCommand(string command, Action<int> onExit = null)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        Write("<b>" + ShortCwd() + "></b> " + command, ColorCmd);

        if (current != null && current.IsRunning)
        {
            Write("A command is still running. Press Stop first.", ColorWarn);
            return null;
        }

        current = ShellRunner.Instance.Run(command, cwd,
            (line, isError) => Write(line, isError ? ColorErr : null),
            code =>
            {
                Write(code == 0 ? "exit 0" : "exit " + code, code == 0 ? ColorDim : ColorErr);
                current = null;
                onExit?.Invoke(code);
            });
        return current;
    }

    /// <summary>Current working directory used for commands.</summary>
    public string WorkingDirectory => cwd;

    /// <summary>Changes the working directory (used by the Git panel's "Open in console").</summary>
    public bool SetWorkingDirectory(string path)
    {
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return false;
        cwd = Path.GetFullPath(path);
        UpdatePrompt();
        Write("cd " + cwd, ColorDim);
        return true;
    }

    // ---------- input ----------

    private void SubmitInput()
    {
        if (commandInput == null) return;
        string cmd = commandInput.text.Trim();
        commandInput.text = "";
        commandInput.ActivateInputField();
        if (cmd.Length == 0) return;

        history.Remove(cmd);
        history.Add(cmd);
        if (history.Count > maxHistory) history.RemoveAt(0);
        historyIndex = history.Count;

        if (HandleBuiltin(cmd)) return;
        RunCommand(cmd);
    }

    // cd / cls / pwd are handled here because cmd /c runs each command in a fresh process.
    private bool HandleBuiltin(string cmd)
    {
        string lower = cmd.ToLowerInvariant();
        if (lower == "cls" || lower == "clear") { Clear(); return true; }
        if (lower == "pwd" || lower == "cd")
        {
            Write("<b>" + ShortCwd() + "></b> " + cmd, ColorCmd);
            Write(cwd);
            return true;
        }
        if (lower.StartsWith("cd ") || lower.StartsWith("cd\t"))
        {
            Write("<b>" + ShortCwd() + "></b> " + cmd, ColorCmd);
            string target = cmd.Substring(3).Trim().Trim('"');
            string candidate = Path.IsPathRooted(target) ? target : Path.Combine(cwd, target);
            try
            {
                candidate = Path.GetFullPath(candidate);
                if (Directory.Exists(candidate)) { cwd = candidate; UpdatePrompt(); }
                else Write("Directory not found: " + candidate, ColorErr);
            }
            catch (Exception e) { Write(e.Message, ColorErr); }
            return true;
        }
        return false;
    }

    private void Recall(int direction)
    {
        historyIndex = Mathf.Clamp(historyIndex + direction, 0, history.Count);
        commandInput.text = historyIndex < history.Count ? history[historyIndex] : "";
        commandInput.caretPosition = commandInput.text.Length;
    }

    private void Stop()
    {
        if (current == null || !current.IsRunning) { Write("Nothing is running.", ColorDim); return; }
        ShellRunner.Instance.Kill(current);
        Write("Stopped.", ColorWarn);
    }

    public void Clear()
    {
        lines.Clear();
        dirty = true;
    }

    // ---------- helpers ----------

    private void OnAppLog(string condition, string stackTrace, LogType type)
    {
        if (!captureAppLog) return;
        string color = type == LogType.Error || type == LogType.Exception || type == LogType.Assert ? ColorErr
                     : type == LogType.Warning ? ColorWarn : ColorLog;
        Write("[" + type + "] " + condition, color);
    }

    private void UpdatePrompt()
    {
        if (promptLabel != null) promptLabel.text = ShortCwd() + ">";
    }

    private string ShortCwd()
    {
        const int max = 28;
        if (cwd.Length <= max) return cwd;
        return "..." + cwd.Substring(cwd.Length - max + 3);
    }

    private System.Collections.IEnumerator ScrollToBottom()
    {
        yield return null; // wait for the layout to pick up the new text size
        Canvas.ForceUpdateCanvases();
        scroll.verticalNormalizedPosition = 0f;
    }
}
