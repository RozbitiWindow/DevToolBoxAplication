using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Put on any Button: clicking it opens the given sidebar tab and records the
/// tool in the RECENT list. Used by Quick tools on Home and by recent entries.
/// </summary>
[RequireComponent(typeof(Button))]
public class ToolLink : MonoBehaviour
{
    [SerializeField] private string toolName = "Number Converter";
    [SerializeField] private string tabId = "numbers";

    public string ToolName => toolName;
    public string TabId => tabId;

    private void Awake()
    {
        GetComponent<Button>().onClick.AddListener(Open);
    }

    public void Configure(string name, string tab)
    {
        toolName = name;
        tabId = tab;
    }

    public void Open()
    {
        if (TabsSwitcher.Instance == null)
        {
            Debug.LogWarning("[ToolLink] No TabsSwitcher in scene.", this);
            return;
        }
        if (TabsSwitcher.Instance.Open(tabId))
            RecentTools.Record(toolName, tabId);
    }
}
