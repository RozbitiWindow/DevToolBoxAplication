using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Most-recently-used tool list, persisted in PlayerPrefs. Each entry pairs a
/// display name with the tab id that opens it.
/// </summary>
public static class RecentTools
{
    [Serializable]
    public class Entry
    {
        public string name;
        public string tabId;
    }

    [Serializable]
    private class Store
    {
        public List<Entry> entries = new List<Entry>();
    }

    private const string Key = "DevToolBox.RecentTools";
    private const int MaxEntries = 5;

    public static event Action Changed;

    public static IReadOnlyList<Entry> Get() => Load().entries;

    public static void Record(string name, string tabId)
    {
        if (string.IsNullOrEmpty(name)) return;

        Store store = Load();
        store.entries.RemoveAll(e => e.name == name);
        store.entries.Insert(0, new Entry { name = name, tabId = tabId });
        if (store.entries.Count > MaxEntries)
            store.entries.RemoveRange(MaxEntries, store.entries.Count - MaxEntries);

        PlayerPrefs.SetString(Key, JsonUtility.ToJson(store));
        PlayerPrefs.Save();
        Changed?.Invoke();
    }

    public static void Clear()
    {
        PlayerPrefs.DeleteKey(Key);
        PlayerPrefs.Save();
        Changed?.Invoke();
    }

    private static Store Load()
    {
        string json = PlayerPrefs.GetString(Key, "");
        if (string.IsNullOrEmpty(json)) return new Store();
        try { return JsonUtility.FromJson<Store>(json) ?? new Store(); }
        catch { return new Store(); }
    }
}
