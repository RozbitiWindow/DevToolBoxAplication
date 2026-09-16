using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Settings tab: binds UI controls to AppSettings and offers data management.
/// </summary>
public class SettingsPanel : MonoBehaviour
{
    [Header("Behavior")]
    [SerializeField] private Toggle rememberLastTab;
    [SerializeField] private Toggle autoCopy;
    [SerializeField] private Toggle groupBinary;

    [Header("Appearance")]
    [SerializeField] private Slider uiScale;
    [SerializeField] private TMP_Text uiScaleLabel;

    [Header("Data")]
    [SerializeField] private Button clearSavedColors;
    [SerializeField] private Button clearRecent;
    [SerializeField] private Button openDataFolder;
    [SerializeField] private Button resetSettings;
    [SerializeField] private SavedColors savedColors;
    [SerializeField] private TMP_Text statusLabel;

    [Header("About")]
    [SerializeField] private TMP_Text aboutText;

    private bool syncing;

    private void Awake()
    {
        if (rememberLastTab != null) rememberLastTab.onValueChanged.AddListener(v => { if (!syncing) AppSettings.RememberLastTab = v; });
        if (autoCopy != null) autoCopy.onValueChanged.AddListener(v => { if (!syncing) AppSettings.AutoCopy = v; });
        if (groupBinary != null) groupBinary.onValueChanged.AddListener(v => { if (!syncing) AppSettings.GroupBinaryNibbles = v; });

        if (uiScale != null)
        {
            uiScale.minValue = AppSettings.MinUiScale;
            uiScale.maxValue = AppSettings.MaxUiScale;
            uiScale.onValueChanged.AddListener(v =>
            {
                float snapped = Mathf.Round(v * 20f) / 20f; // 5 % steps
                if (uiScaleLabel != null) uiScaleLabel.text = Mathf.RoundToInt(snapped * 100f) + " %";
                if (!syncing) AppSettings.UiScale = snapped;
            });
        }

        if (clearSavedColors != null) clearSavedColors.onClick.AddListener(() =>
        {
            if (savedColors != null) savedColors.Clear();
            Status("Saved colors cleared");
        });
        if (clearRecent != null) clearRecent.onClick.AddListener(() =>
        {
            RecentTools.Clear();
            Status("Recent list cleared");
        });
        if (openDataFolder != null) openDataFolder.onClick.AddListener(() =>
        {
            Application.OpenURL("file:///" + Application.persistentDataPath.Replace("\\", "/"));
            Status("Opened " + Application.persistentDataPath);
        });
        if (resetSettings != null) resetSettings.onClick.AddListener(() =>
        {
            AppSettings.ResetToDefaults();
            Status("Settings reset to defaults");
        });

        if (aboutText != null)
        {
            aboutText.text =
                Application.productName + "  v" + Application.version + "\n" +
                "Unity " + Application.unityVersion + "\n" +
                Application.platform + "  |  " + SystemInfo.operatingSystem + "\n" +
                "Data: " + Application.persistentDataPath;
        }
    }

    private void OnEnable()
    {
        AppSettings.Changed += Sync;
        Sync();
        Status("");
    }

    private void OnDisable()
    {
        AppSettings.Changed -= Sync;
    }

    // Push AppSettings values into the controls without re-triggering the setters.
    private void Sync()
    {
        syncing = true;
        try
        {
            if (rememberLastTab != null) rememberLastTab.isOn = AppSettings.RememberLastTab;
            if (autoCopy != null) autoCopy.isOn = AppSettings.AutoCopy;
            if (groupBinary != null) groupBinary.isOn = AppSettings.GroupBinaryNibbles;
            if (uiScale != null) uiScale.value = AppSettings.UiScale;
            if (uiScaleLabel != null) uiScaleLabel.text = Mathf.RoundToInt(AppSettings.UiScale * 100f) + " %";
        }
        finally
        {
            syncing = false;
        }
    }

    private void Status(string message)
    {
        if (statusLabel == null) return;
        statusLabel.text = message;
        statusLabel.gameObject.SetActive(!string.IsNullOrEmpty(message));
    }
}
