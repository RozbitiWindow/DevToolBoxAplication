using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

public class ButtonHoverScale : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler,
    IPointerDownHandler, IPointerUpHandler
{
    [Header("Hover (mouse only)")]
    [SerializeField] private float hoverScale = 1.1f;

    [Header("Press (mouse + touch)")]
    [SerializeField] private float pressScale = 0.9f;

    [SerializeField] private float scaleSpeed = 12f;

    private Vector3 baseScale;
    private Coroutine scaleRoutine;
    private bool isPressed;

    private void Awake()
    {
        baseScale = transform.localScale;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!isPressed)
            StartScale(baseScale * hoverScale);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (!isPressed)
            StartScale(baseScale);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        isPressed = true;
        StartScale(baseScale * pressScale);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        isPressed = false;

        // Myš, co po puštění pořád "hoveruje" nad tlačítkem, se vrátí do hover stavu.
        // Na dotyku eventData.pointerEnter po zvednutí prstu už není nastavené, takže spadne rovnou do baseScale.
        bool stillHovering = eventData.pointerEnter == gameObject;
        StartScale(stillHovering ? baseScale * hoverScale : baseScale);
    }

    private void StartScale(Vector3 target)
    {
        if (scaleRoutine != null)
            StopCoroutine(scaleRoutine);

        scaleRoutine = StartCoroutine(ScaleTo(target));
    }

    private IEnumerator ScaleTo(Vector3 target)
    {
        while (Vector3.Distance(transform.localScale, target) > 0.001f)
        {
            transform.localScale = Vector3.Lerp(transform.localScale, target, scaleSpeed * Time.unscaledDeltaTime);
            yield return null;
        }

        transform.localScale = target;
    }
}
