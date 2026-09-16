using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Applies AppSettings.UiScale to the CanvasScaler: a bigger scale means a
/// smaller reference resolution, so everything renders larger.
/// </summary>
[RequireComponent(typeof(CanvasScaler))]
public class UiScaler : MonoBehaviour
{
    private CanvasScaler scaler;
    private Vector2 baseReference;

    private void Awake()
    {
        scaler = GetComponent<CanvasScaler>();
        baseReference = scaler.referenceResolution;
    }

    private void OnEnable()
    {
        AppSettings.Changed += Apply;
        Apply();
    }

    private void OnDisable()
    {
        AppSettings.Changed -= Apply;
    }

    private void Apply()
    {
        scaler.referenceResolution = baseReference / AppSettings.UiScale;
    }
}
