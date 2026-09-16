using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Renders the RECENT list on Home by cloning a template button per entry.
/// </summary>
public class RecentToolsView : MonoBehaviour
{
    [SerializeField] private GameObject entryTemplate;
    [SerializeField] private Transform container;
    [SerializeField] private TMP_Text emptyLabel;

    private readonly List<GameObject> spawned = new List<GameObject>();

    private void OnEnable()
    {
        RecentTools.Changed += Rebuild;
        Rebuild();
    }

    private void OnDisable()
    {
        RecentTools.Changed -= Rebuild;
    }

    private void Rebuild()
    {
        foreach (GameObject go in spawned) Destroy(go);
        spawned.Clear();

        IReadOnlyList<RecentTools.Entry> entries = RecentTools.Get();
        if (emptyLabel != null) emptyLabel.gameObject.SetActive(entries.Count == 0);
        if (entryTemplate == null || container == null) return;

        foreach (RecentTools.Entry entry in entries)
        {
            GameObject go = Instantiate(entryTemplate, container);
            go.name = "Recent_" + entry.name;
            go.SetActive(true);

            TMP_Text label = go.GetComponentInChildren<TMP_Text>(true);
            if (label != null) label.text = entry.name;

            ToolLink link = go.GetComponent<ToolLink>();
            if (link == null) link = go.AddComponent<ToolLink>();
            link.Configure(entry.name, entry.tabId);

            spawned.Add(go);
        }
    }
}
