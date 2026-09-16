using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Git tab: status (branch, ahead/behind, changed files) and recent log for a
/// repository path, plus fetch / pull / push / stage / commit. Every git
/// command is echoed in the Console tab.
/// </summary>
public class GitPanel : MonoBehaviour
{
    [Header("Repository")]
    [SerializeField] private TMP_InputField repoInput;
    [SerializeField] private Button useConsoleCwdButton;
    [SerializeField] private Button openInConsoleButton;
    [SerializeField] private Button refreshButton;
    [SerializeField] private TMP_Text statusLabel;

    [Header("Content")]
    [SerializeField] private TMP_Text changesText;
    [SerializeField] private TMP_Text logText;

    [Header("Actions")]
    [SerializeField] private Button fetchButton;
    [SerializeField] private Button pullButton;
    [SerializeField] private Button pushButton;
    [SerializeField] private Button stageAllButton;
    [SerializeField] private Button unstageAllButton;
    [SerializeField] private TMP_InputField commitMessageInput;
    [SerializeField] private Button commitButton;

    private const string RepoKey = "DevToolBox.Git.Repo";
    private const string ColorOk = "#7AFFB0";
    private const string ColorWarn = "#FFD27A";
    private const string ColorErr = "#FF7A7A";
    private const string ColorDim = "#9A8FBF";
    private const string ColorAdd = "#7AFFB0";
    private const string ColorMod = "#FFD27A";
    private const string ColorDel = "#FF7A7A";
    private const string ColorUntracked = "#8FD3FF";

    private readonly List<string> statusLines = new List<string>();
    private readonly List<string> logLines = new List<string>();
    private bool busy;

    private string Repo => repoInput != null ? repoInput.text.Trim().Trim('"') : "";

    private void Awake()
    {
        if (repoInput != null)
        {
            if (string.IsNullOrEmpty(repoInput.text))
                repoInput.text = PlayerPrefs.GetString(RepoKey, Directory.GetCurrentDirectory());
            repoInput.onSubmit.AddListener(_ => Refresh());
        }
        if (useConsoleCwdButton != null) useConsoleCwdButton.onClick.AddListener(() =>
        {
            if (ConsolePanel.Instance != null && repoInput != null) { repoInput.text = ConsolePanel.Instance.WorkingDirectory; Refresh(); }
        });
        if (openInConsoleButton != null) openInConsoleButton.onClick.AddListener(() =>
        {
            if (ConsolePanel.Instance != null && ConsolePanel.Instance.SetWorkingDirectory(Repo)) TabsSwitcher.Instance?.Open("console");
        });
        if (refreshButton != null) refreshButton.onClick.AddListener(Refresh);

        Bind(fetchButton, "fetch --all --prune");
        Bind(pullButton, "pull");
        Bind(pushButton, "push");
        Bind(stageAllButton, "add -A");
        Bind(unstageAllButton, "reset");
        if (commitButton != null) commitButton.onClick.AddListener(Commit);
    }

    private void OnEnable()
    {
        Refresh();
    }

    private void Bind(Button button, string gitArgs)
    {
        if (button != null) button.onClick.AddListener(() => RunGit(gitArgs));
    }

    private void Commit()
    {
        string message = commitMessageInput != null ? commitMessageInput.text.Trim() : "";
        if (message.Length == 0) { SetStatus("Commit message is empty.", ColorErr); return; }
        RunGit("commit -m \"" + message.Replace("\"", "\\\"") + "\"");
        if (commitMessageInput != null) commitMessageInput.text = "";
    }

    // Runs through the Console tab so the user sees exactly what happened, then refreshes.
    private void RunGit(string args)
    {
        string repo = Repo;
        if (!Directory.Exists(repo)) { SetStatus("Folder does not exist: " + repo, ColorErr); return; }
        string command = "git -C \"" + repo + "\" " + args;
        if (ConsolePanel.Instance != null) ConsolePanel.Instance.RunCommand(command, code => Refresh());
        else ShellRunner.Instance.Run(command, null, null, code => Refresh());
    }

    public void Refresh()
    {
        if (busy) return;
        string repo = Repo;
        if (repo.Length == 0 || !Directory.Exists(repo))
        {
            SetStatus("Enter a folder that contains a git repository.", ColorWarn);
            if (changesText != null) changesText.text = "";
            if (logText != null) logText.text = "";
            return;
        }
        PlayerPrefs.SetString(RepoKey, repo);

        busy = true;
        statusLines.Clear();
        SetStatus("Reading status...", ColorDim);
        ShellRunner.Instance.Run("git -C \"" + repo + "\" status --porcelain=v1 -b", null,
            (line, isErr) => statusLines.Add((isErr ? "!" : "") + line),
            code =>
            {
                if (code != 0)
                {
                    busy = false;
                    string err = statusLines.Find(l => l.StartsWith("!"));
                    SetStatus(code == 9009 || code == 127 ? "git is not installed or not on PATH."
                            : err != null && err.Contains("not a git repository") ? "Not a git repository: " + repo
                            : "git failed (" + code + "): " + (err != null ? err.Substring(1) : ""), ColorErr);
                    if (changesText != null) changesText.text = "";
                    if (logText != null) logText.text = "";
                    return;
                }
                RenderStatus();
                LoadLog(repo);
            });
    }

    private void LoadLog(string repo)
    {
        logLines.Clear();
        ShellRunner.Instance.Run("git -C \"" + repo + "\" log --oneline --decorate=short -n 25 --date=relative --format=\"%h|%ar|%an|%s|%D\"", null,
            (line, isErr) => { if (!isErr) logLines.Add(line); },
            code =>
            {
                busy = false;
                if (logText == null) return;
                var sb = new StringBuilder();
                foreach (string l in logLines)
                {
                    string[] f = l.Split(new[] { '|' }, 5);
                    if (f.Length < 4) continue;
                    sb.Append("<color=" + ColorWarn + ">").Append(f[0]).Append("</color> ");
                    if (f.Length == 5 && f[4].Length > 0) sb.Append("<color=" + ColorUntracked + ">(").Append(f[4]).Append(")</color> ");
                    sb.Append(f[3]).Append("  <color=" + ColorDim + ">").Append(f[2]).Append(", ").Append(f[1]).Append("</color>\n");
                }
                logText.text = sb.Length > 0 ? sb.ToString().TrimEnd() : "<color=" + ColorDim + ">No commits yet.</color>";
            });
    }

    private void RenderStatus()
    {
        string branchLine = statusLines.Find(l => l.StartsWith("## "));
        string branch = "?", tracking = "";
        if (branchLine != null)
        {
            string b = branchLine.Substring(3);
            int bracket = b.IndexOf(" [");
            if (bracket >= 0) { tracking = b.Substring(bracket + 2).TrimEnd(']'); b = b.Substring(0, bracket); }
            int dots = b.IndexOf("...");
            branch = dots >= 0 ? b.Substring(0, dots) : b;
        }

        int staged = 0, unstaged = 0, untracked = 0;
        var sb = new StringBuilder();
        foreach (string l in statusLines)
        {
            if (l.StartsWith("## ") || l.StartsWith("!") || l.Length < 3) continue;
            char x = l[0], y = l[1];
            string path = l.Substring(3);
            string color; string tag;
            if (x == '?' ) { untracked++; color = ColorUntracked; tag = "??"; }
            else
            {
                if (x != ' ') staged++;
                if (y != ' ') unstaged++;
                char c = x != ' ' ? x : y;
                color = c == 'A' ? ColorAdd : c == 'D' ? ColorDel : ColorMod;
                tag = (x == ' ' ? "." : x.ToString()) + (y == ' ' ? "." : y.ToString());
            }
            sb.Append("<color=" + color + ">").Append(tag).Append("</color>  ").Append(path).Append('\n');
        }

        if (changesText != null)
            changesText.text = sb.Length > 0 ? sb.ToString().TrimEnd() : "<color=" + ColorDim + ">Working tree clean.</color>";

        string summary = "<b>" + branch + "</b>";
        if (tracking.Length > 0) summary += "  <color=" + ColorWarn + ">[" + tracking + "]</color>";
        summary += "  -  " + staged + " staged, " + unstaged + " modified, " + untracked + " untracked";
        SetStatus(summary, staged + unstaged + untracked == 0 ? ColorOk : ColorWarn);
    }

    private void SetStatus(string text, string color)
    {
        if (statusLabel != null) statusLabel.text = "<color=" + color + ">" + text + "</color>";
    }
}
