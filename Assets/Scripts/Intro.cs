using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Logo intro: the logo starts darkened, a beam of light sweeps across it,
/// it darkens again and finally settles to its normal look. A sound plays
/// when the sweep starts. No shader needed - the beam is a child Image with a
/// generated gradient, clipped to the logo's shape by a Mask.
///
/// Setup: assign the logo Image (and optionally a CanvasGroup for fade-in,
/// an AudioClip and an AudioSource). Call PlayIntro() or let it run on Start.
/// </summary>
public class Intro : MonoBehaviour
{
    [Header("Targets")]
    [SerializeField] private Image logo;
    [SerializeField] private CanvasGroup logoCanvasGroup;   // optional: fades in at the beginning

    [Header("Timing")]
    [SerializeField] private bool playOnStart = true;
    [SerializeField] private float startDelay = 0.3f;
    [SerializeField] private float fadeInDuration = 0.35f;
    [SerializeField] private float shineDuration = 1.4f;
    [Tooltip("How long the finished logo stays on screen before onIntroFinished fires.")]
    [SerializeField] private float holdAfterShine = 3f;

    [Header("Look")]
    [Tooltip("Logo tint at its darkest (multiplied with the sprite).")]
    [SerializeField] private Color darkTint = new Color(0.35f, 0.35f, 0.4f, 1f);
    [Tooltip("0 = darkTint, 1 = normal. Default: dark -> bright while the beam passes -> dark -> normal.")]
    [SerializeField] private AnimationCurve brightness = new AnimationCurve(
        new Keyframe(0f, 0f),
        new Keyframe(0.45f, 1f),
        new Keyframe(0.7f, 0f),
        new Keyframe(1f, 1f));
    [SerializeField] private Color shineColor = new Color(1f, 1f, 1f, 0.85f);
    [Tooltip("Beam width as a fraction of the logo width.")]
    [Range(0.05f, 1f)] [SerializeField] private float shineWidth = 0.35f;
    [SerializeField] private float shineAngle = 20f;

    [Header("Sound")]
    [SerializeField] private AudioSource audioSource;   // optional: created on this object if missing
    [SerializeField] private AudioClip shineSound;
    [Range(0f, 1f)] [SerializeField] private float shineVolume = 1f;

    [Header("Events")]
    public UnityEvent onIntroFinished;

    private RectTransform shine;
    private Coroutine running;
    private static Sprite beamSprite;

    private void Start()
    {
        if (playOnStart) PlayIntro();
    }

    /// <summary>Starts the intro (restarts if it is already running).</summary>
    public Coroutine PlayIntro()
    {
        if (running != null) StopCoroutine(running);
        running = StartCoroutine(IntroRoutine());
        return running;
    }

    private IEnumerator IntroRoutine()
    {
        if (logo == null)
        {
            Debug.LogError("[Intro] No logo Image assigned.", this);
            yield break;
        }

        Color normalTint = logo.color;
        logo.color = darkTint * normalTint;
        if (logoCanvasGroup != null) logoCanvasGroup.alpha = 0f;

        if (startDelay > 0f) yield return new WaitForSecondsRealtime(startDelay);

        // optional fade-in
        if (logoCanvasGroup != null && fadeInDuration > 0f)
        {
            for (float t = 0f; t < fadeInDuration; t += Time.unscaledDeltaTime)
            {
                logoCanvasGroup.alpha = t / fadeInDuration;
                yield return null;
            }
            logoCanvasGroup.alpha = 1f;
        }

        yield return Shine(normalTint);

        if (holdAfterShine > 0f) yield return new WaitForSecondsRealtime(holdAfterShine);

        onIntroFinished?.Invoke();
        running = null;
    }

    /// <summary>
    /// The shine sweep alone. Can be yielded from other coroutines or started
    /// on its own via StartCoroutine(Shine()).
    /// </summary>
    public IEnumerator Shine() => Shine(logo != null ? logo.color : Color.white);

    private IEnumerator Shine(Color normalTint)
    {
        EnsureShineObject();
        RectTransform logoRect = logo.rectTransform;
        float width = logoRect.rect.width;
        float beamWidth = width * shineWidth;

        // travel from fully off the left edge to fully off the right edge
        float startX = -width * 0.5f - beamWidth;
        float endX = width * 0.5f + beamWidth;

        shine.gameObject.SetActive(true);
        PlaySound();

        for (float t = 0f; t < shineDuration; t += Time.unscaledDeltaTime)
        {
            float p = t / shineDuration;
            float eased = p * p * (3f - 2f * p); // smoothstep: slow in, fast middle, slow out
            shine.anchoredPosition = new Vector2(Mathf.Lerp(startX, endX, eased), 0f);
            logo.color = Color.Lerp(darkTint * normalTint, normalTint, brightness.Evaluate(p));
            yield return null;
        }

        shine.gameObject.SetActive(false);
        logo.color = normalTint;
    }

    private void PlaySound()
    {
        if (shineSound == null) return;
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
            }
        }
        audioSource.PlayOneShot(shineSound, shineVolume);
    }

    // Builds the masked beam once: Mask on the logo + a child Image with a soft gradient.
    private void EnsureShineObject()
    {
        if (shine != null) return;

        Mask mask = logo.GetComponent<Mask>();
        if (mask == null) mask = logo.gameObject.AddComponent<Mask>();
        mask.showMaskGraphic = true;

        var go = new GameObject("Shine", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(logo.transform, false);
        shine = go.GetComponent<RectTransform>();

        RectTransform logoRect = logo.rectTransform;
        float width = logoRect.rect.width;
        float height = logoRect.rect.height;

        shine.anchorMin = shine.anchorMax = new Vector2(0.5f, 0.5f);
        shine.pivot = new Vector2(0.5f, 0.5f);
        // taller than the logo so the rotated beam still covers the corners
        shine.sizeDelta = new Vector2(width * shineWidth, height + width);
        shine.localRotation = Quaternion.Euler(0f, 0f, shineAngle);

        Image img = go.GetComponent<Image>();
        img.sprite = GetBeamSprite();
        img.color = shineColor;
        img.raycastTarget = false;

        go.SetActive(false);
    }

    // Horizontal soft white band: transparent at the edges, opaque in the middle.
    private static Sprite GetBeamSprite()
    {
        if (beamSprite != null) return beamSprite;

        const int w = 128;
        var tex = new Texture2D(w, 2, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.DontSave
        };

        var pixels = new Color[w * 2];
        for (int x = 0; x < w; x++)
        {
            float u = x / (float)(w - 1);
            float a = Mathf.Sin(u * Mathf.PI);           // 0 -> 1 -> 0
            a *= a;                                       // sharper core, softer edges
            pixels[x] = pixels[x + w] = new Color(1f, 1f, 1f, a);
        }
        tex.SetPixels(pixels);
        tex.Apply();

        beamSprite = Sprite.Create(tex, new Rect(0, 0, w, 2), new Vector2(0.5f, 0.5f), 100f);
        beamSprite.hideFlags = HideFlags.DontSave;
        return beamSprite;
    }

    public void LoadScene()
    {
        SceneManager.LoadScene("Main");
    }
}
