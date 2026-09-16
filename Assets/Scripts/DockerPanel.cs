using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Docker tab: lists containers and images through the docker CLI and offers
/// start / stop / restart / logs / remove per container. Command output goes
/// to the Console tab so nothing is hidden.
/// </summary>
public class DockerPanel : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private TMP_Text statusLabel;
    [SerializeField] private Button refreshButton;
    [SerializeField] private Button pruneButton;
    [SerializeField] private Transform containerList;
    [SerializeField] private GameObject containerRowTemplate;
    [SerializeField] private Transform imageList;
    [SerializeField] private GameObject imageRowTemplate;
    [SerializeField] private TMP_Text emptyContainers;
    [SerializeField] private TMP_Text emptyImages;

    private const string Sep = "|";
    private const string ColorRunning = "#7AFFB0";
    private const string ColorStopped = "#FF9A7A";
    private const string ColorDim = "#9A8FBF";

    private readonly List<GameObject> spawned = new List<GameObject>();
    private readonly List<string> psLines = new List<string>();
    private readonly List<string> imageLines = new List<string>();
    private bool busy;

    private void Awake()
    {
        if (refreshButton != null) refreshButton.onClick.AddListener(Refresh);
        if (pruneButton != null) pruneButton.onClick.AddListener(() => RunAndRefresh("docker system prune -f"));
    }

    private void OnEnable()
    {
        Refresh();
    }

    public void Refresh()
    {
        if (busy) return;
        busy = true;
        SetStatus("Querying docker...", ColorDim);

        psLines.Clear();
        ShellRunner.Instance.Run(
            "docker ps -a --format \"{{.ID}}" + Sep + "{{.Names}}" + Sep + "{{.Image}}" + Sep + "{{.State}}" + Sep + "{{.Status}}" + Sep + "{{.Ports}}\"",
            null,
            (line, isError) => { if (!isError && line.Contains(Sep)) psLines.Add(line); else if (isError) psLines.Add("!" + line); },
            code =>
            {
                if (code != 0)
                {
                    busy = false;
                    string firstError = psLines.Find(l => l.StartsWith("!"));
                    string reason = firstError != null ? firstError.Substring(1) : "exit code " + code;
                    SetStatus(code == 9009 || code == 127 || reason.Contains("not recognized") || reason.Contains("not found")
                        ? "Docker CLI not found. Install Docker Desktop and make sure 'docker' is on PATH."
                        : "Docker is not responding: " + reason, ColorStopped);
                    Rebuild();
                    return;
                }
                LoadImages();
            });
    }

    private void LoadImages()
    {
        imageLines.Clear();
        ShellRunner.Instance.Run(
            "docker images --format \"{{.Repository}}:{{.Tag}}" + Sep + "{{.ID}}" + Sep + "{{.Size}}" + Sep + "{{.CreatedSince}}\"",
            null,
            (line, isError) => { if (!isError && line.Contains(Sep)) imageLines.Add(line); },
            code =>
            {
                busy = false;
                int running = psLines.FindAll(l => !l.StartsWith("!") && l.Split('|')[3] == "running").Count;
                int total = psLines.FindAll(l => !l.StartsWith("!")).Count;
                SetStatus(total + " containers (" + running + " running), " + imageLines.Count + " images", ColorRunning);
                Rebuild();
            });
    }

    private void Rebuild()
    {
        foreach (GameObject go in spawned) Destroy(go);
        spawned.Clear();

        var containers = psLines.FindAll(l => !l.StartsWith("!"));
        if (emptyContainers != null) emptyContainers.gameObject.SetActive(containers.Count == 0);
        if (containerRowTemplate != null && containerList != null)
        {
            foreach (string line in containers)
            {
                string[] f = line.Split('|');
                if (f.Length < 5) continue;
                string id = f[0], name = f[1], image = f[2], state = f[3], status = f[4];
                bool isRunning = state == "running";

                GameObject row = Instantiate(containerRowTemplate, containerList);
                row.SetActive(true);
                row.name = "Container_" + name;
                SetText(row, "Name", name);
                SetText(row, "Info", "<color=" + (isRunning ? ColorRunning : ColorStopped) + ">" + status + "</color>  <color=" + ColorDim + ">" + image + "</color>");

                Bind(row, "Start", !isRunning, () => RunAndRefresh("docker start " + id));
                Bind(row, "Stop", isRunning, () => RunAndRefresh("docker stop " + id));
                Bind(row, "Restart", isRunning, () => RunAndRefresh("docker restart " + id));
                Bind(row, "Logs", true, () => ShowInConsole("docker logs --tail 100 " + id));
                Bind(row, "Remove", true, () => RunAndRefresh("docker rm -f " + id));
                spawned.Add(row);
            }
        }

        if (emptyImages != null) emptyImages.gameObject.SetActive(imageLines.Count == 0);
        if (imageRowTemplate != null && imageList != null)
        {
            foreach (string line in imageLines)
            {
                string[] f = line.Split('|');
                if (f.Length < 4) continue;
                string repo = f[0], id = f[1], size = f[2], created = f[3];

                GameObject row = Instantiate(imageRowTemplate, imageList);
                row.SetActive(true);
                row.name = "Image_" + repo;
                SetText(row, "Name", repo);
                SetText(row, "Info", "<color=" + ColorDim + ">" + size + "  |  " + created + "  |  " + id + "</color>");
                Bind(row, "Run", true, () => ShowInConsole("docker run --rm -d " + repo, true));
                Bind(row, "Remove", true, () => RunAndRefresh("docker rmi " + id));
                spawned.Add(row);
            }
        }
    }

    private void RunAndRefresh(string command)
    {
        ShowInConsole(command, true);
    }

    // Every docker action is echoed in the Console tab; refresh the lists when it finishes.
    private void ShowInConsole(string command, bool refreshAfter = false)
    {
        if (ConsolePanel.Instance != null)
        {
            ConsolePanel.Instance.RunCommand(command, code => { if (refreshAfter) Refresh(); });
        }
        else
        {
            ShellRunner.Instance.Run(command, null, null, code => { if (refreshAfter) Refresh(); });
        }
    }

    private static void SetText(GameObject row, string child, string text)
    {
        Transform t = row.transform.Find(child);
        if (t == null) return;
        TMP_Text label = t.GetComponent<TMP_Text>();
        if (label != null) label.text = text;
    }

    private static void Bind(GameObject row, string child, bool interactable, Action action)
    {
        Transform t = row.transform.Find(child);
        if (t == null) return;
        Button b = t.GetComponent<Button>();
        if (b == null) return;
        b.interactable = interactable;
        b.onClick.RemoveAllListeners();
        b.onClick.AddListener(() => action());
    }

    private void SetStatus(string text, string color)
    {
        if (statusLabel != null) statusLabel.text = "<color=" + color + ">" + text + "</color>";
    }
}
