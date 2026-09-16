using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Sidebar category switcher. Tabs are data (id + button + panel), so adding a
/// category means adding an Inspector entry, not code.
/// </summary>
public class TabsSwitcher : MonoBehaviour
{
    [Serializable]
    public class Tab
    {
        [Tooltip("Stable id used by ToolLink / Quick tools, e.g. \"home\", \"colors\", \"numbers\".")]
        public string id;
        public Button button;
        public GameObject panel;
    }

    private const string LastTabKey = "DevToolBox.LastTab";

    [SerializeField] private Tab[] tabs;
    [SerializeField] private int defaultTab = 0;

    // "Remember last tab" lives in Settings (AppSettings.RememberLastTab).

    [Header("Button colors")]
    [SerializeField] private Color baseColor = new Color32(0x91, 0x76, 0xFF, 0xFF);
    [SerializeField] private Color selectedColor = new Color32(0xCA, 0x76, 0xFF, 0xFF);

    public static TabsSwitcher Instance { get; private set; }

    /// <summary>Fired with the tab id after a tab becomes visible.</summary>
    public event Action<string> TabOpened;

    public string CurrentId => currentIndex >= 0 && currentIndex < tabs.Length ? tabs[currentIndex].id : null;

    private int currentIndex = -1;

    private void Awake()
    {
        Instance = this;

        for (int i = 0; i < tabs.Length; i++)
        {
            Tab tab = tabs[i];
            if (tab.button == null || tab.panel == null)
            {
                Debug.LogError($"[TabsSwitcher] Tab {i} ('{tab.id}') is missing a button or panel.", this);
                continue;
            }

            int index = i; // closure capture
            tab.button.onClick.AddListener(() => Open(index));
        }
    }

    private void Start()
    {
        int start = defaultTab;
        if (AppSettings.RememberLastTab)
        {
            int remembered = IndexOf(PlayerPrefs.GetString(LastTabKey, null));
            if (remembered >= 0) start = remembered;
        }
        Open(start);
    }

    public void Open(int index)
    {
        if (index < 0 || index >= tabs.Length) return;

        for (int i = 0; i < tabs.Length; i++)
        {
            bool active = i == index;
            if (tabs[i].panel != null) tabs[i].panel.SetActive(active);
            if (tabs[i].button != null && tabs[i].button.targetGraphic != null)
                tabs[i].button.targetGraphic.color = active ? selectedColor : baseColor;
        }

        currentIndex = index;
        PlayerPrefs.SetString(LastTabKey, tabs[index].id); // always tracked; only read back when the setting is on
        TabOpened?.Invoke(tabs[index].id);
    }

    public bool Open(string id)
    {
        int index = IndexOf(id);
        if (index < 0)
        {
            Debug.LogWarning($"[TabsSwitcher] No tab with id '{id}'.", this);
            return false;
        }
        Open(index);
        return true;
    }

    private int IndexOf(string id)
    {
        if (string.IsNullOrEmpty(id)) return -1;
        for (int i = 0; i < tabs.Length; i++)
            if (string.Equals(tabs[i].id, id, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }
}
