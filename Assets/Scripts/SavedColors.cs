using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// "Save color" from the picker into a swatch list that survives restarts
/// (JSON file in Application.persistentDataPath). Click a swatch to load it
/// back into the picker, click its X to remove it.
/// </summary>
public class SavedColors : MonoBehaviour
{
    [Serializable]
    private class Store
    {
        public List<string> hex = new List<string>();
    }

    [SerializeField] private FlexibleColorPicker picker;
    [SerializeField] private Button saveButton;
    [SerializeField] private Transform container;
    [SerializeField] private GameObject swatchTemplate;
    [SerializeField] private TMP_Text emptyLabel;
    [SerializeField] private int maxColors = 24;

    private readonly Store store = new Store();
    private readonly List<GameObject> spawned = new List<GameObject>();

    private static string FilePath => Path.Combine(Application.persistentDataPath, "saved_colors.json");

    private void Awake()
    {
        if (saveButton != null) saveButton.onClick.AddListener(SaveCurrent);
        Load();
        Rebuild();
    }

    public void SaveCurrent()
    {
        if (picker == null) return;

        string hex = "#" + ColorUtility.ToHtmlStringRGBA(picker.color);
        store.hex.Remove(hex);          // move to top instead of duplicating
        store.hex.Insert(0, hex);
        if (store.hex.Count > maxColors)
            store.hex.RemoveRange(maxColors, store.hex.Count - maxColors);

        Persist();
        Rebuild();
    }

    /// <summary>Removes every saved color (used by Settings → Data).</summary>
    public void Clear()
    {
        store.hex.Clear();
        Persist();
        Rebuild();
    }

    private void Remove(string hex)
    {
        store.hex.Remove(hex);
        Persist();
        Rebuild();
    }

    private void Rebuild()
    {
        foreach (GameObject go in spawned) Destroy(go);
        spawned.Clear();

        if (emptyLabel != null) emptyLabel.gameObject.SetActive(store.hex.Count == 0);
        if (swatchTemplate == null || container == null) return;

        foreach (string hex in store.hex)
        {
            if (!ColorUtility.TryParseHtmlString(hex, out Color color)) continue;

            GameObject go = Instantiate(swatchTemplate, container);
            go.name = "Swatch_" + hex;
            go.SetActive(true);

            // Template layout: root has Image+Button (the swatch), child "Label" TMP, child "Delete" Button.
            Image swatchImage = go.GetComponent<Image>();
            if (swatchImage != null) swatchImage.color = color;

            Transform labelTransform = go.transform.Find("Label");
            TMP_Text label = labelTransform != null ? labelTransform.GetComponent<TMP_Text>() : null;
            if (label != null)
            {
                label.text = hex.Length == 9 && hex.EndsWith("FF") ? hex.Substring(0, 7) : hex;
                label.color = Readable(color);
            }

            Button pick = go.GetComponent<Button>();
            if (pick != null) pick.onClick.AddListener(() => { if (picker != null) picker.color = color; });

            Transform delete = go.transform.Find("Delete");
            Button deleteButton = delete != null ? delete.GetComponent<Button>() : null;
            if (deleteButton != null)
            {
                string captured = hex;
                deleteButton.onClick.AddListener(() => Remove(captured));
                TMP_Text deleteLabel = delete.GetComponentInChildren<TMP_Text>(true);
                if (deleteLabel != null) deleteLabel.color = Readable(color);
            }

            spawned.Add(go);
        }
    }

    private static Color Readable(Color bg)
    {
        float luminance = 0.299f * bg.r + 0.587f * bg.g + 0.114f * bg.b;
        return luminance > 0.6f ? new Color(0.1f, 0.1f, 0.1f) : Color.white;
    }

    private void Load()
    {
        store.hex.Clear();
        try
        {
            if (!File.Exists(FilePath)) return;
            Store loaded = JsonUtility.FromJson<Store>(File.ReadAllText(FilePath));
            if (loaded != null && loaded.hex != null) store.hex.AddRange(loaded.hex);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[SavedColors] Could not read " + FilePath + ": " + e.Message);
        }
    }

    private void Persist()
    {
        try
        {
            File.WriteAllText(FilePath, JsonUtility.ToJson(store, true));
        }
        catch (Exception e)
        {
            Debug.LogError("[SavedColors] Could not write " + FilePath + ": " + e.Message);
        }
    }
}
