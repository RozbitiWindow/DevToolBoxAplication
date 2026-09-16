using System;
using UnityEngine;

/// <summary>
/// Global app settings, persisted in PlayerPrefs. Raise Changed after every
/// write so UI (Settings panel, CanvasScaler, converters) can react.
/// </summary>
public static class AppSettings
{
    private const string RememberLastTabKey = "DevToolBox.Settings.RememberLastTab";
    private const string AutoCopyKey = "DevToolBox.Settings.AutoCopy";
    private const string GroupBinaryKey = "DevToolBox.Settings.GroupBinary";
    private const string UiScaleKey = "DevToolBox.Settings.UiScale";

    public const float MinUiScale = 0.75f;
    public const float MaxUiScale = 1.5f;

    public static event Action Changed;

    /// <summary>Reopen the last used tab on startup instead of Home.</summary>
    public static bool RememberLastTab
    {
        get => PlayerPrefs.GetInt(RememberLastTabKey, 0) == 1;
        set => SetInt(RememberLastTabKey, value ? 1 : 0);
    }

    /// <summary>Copy generated values (UUID, password...) to the clipboard automatically.</summary>
    public static bool AutoCopy
    {
        get => PlayerPrefs.GetInt(AutoCopyKey, 0) == 1;
        set => SetInt(AutoCopyKey, value ? 1 : 0);
    }

    /// <summary>Show binary numbers grouped in fours (1111 1111).</summary>
    public static bool GroupBinaryNibbles
    {
        get => PlayerPrefs.GetInt(GroupBinaryKey, 1) == 1;
        set => SetInt(GroupBinaryKey, value ? 1 : 0);
    }

    /// <summary>UI scale multiplier applied to the CanvasScaler (1 = default).</summary>
    public static float UiScale
    {
        get => Mathf.Clamp(PlayerPrefs.GetFloat(UiScaleKey, 1f), MinUiScale, MaxUiScale);
        set
        {
            float clamped = Mathf.Clamp(value, MinUiScale, MaxUiScale);
            if (Mathf.Approximately(clamped, PlayerPrefs.GetFloat(UiScaleKey, 1f))) return;
            PlayerPrefs.SetFloat(UiScaleKey, clamped);
            Save();
        }
    }

    public static void ResetToDefaults()
    {
        PlayerPrefs.DeleteKey(RememberLastTabKey);
        PlayerPrefs.DeleteKey(AutoCopyKey);
        PlayerPrefs.DeleteKey(GroupBinaryKey);
        PlayerPrefs.DeleteKey(UiScaleKey);
        Save();
    }

    private static void SetInt(string key, int value)
    {
        if (PlayerPrefs.GetInt(key, int.MinValue) == value) return;
        PlayerPrefs.SetInt(key, value);
        Save();
    }

    private static void Save()
    {
        PlayerPrefs.Save();
        Changed?.Invoke();
    }
}
