using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

/// <summary>
/// HTTP tab (mini Postman): method + URL + headers + body -> status, timing,
/// response headers and body. JSON responses are pretty-printed and can be
/// sent to the Formatters tab.
/// </summary>
public class HttpClientPanel : MonoBehaviour
{
    [Header("Request")]
    [SerializeField] private TMP_Dropdown methodDropdown;
    [SerializeField] private TMP_InputField urlInput;
    [SerializeField] private TMP_InputField headersInput;   // one "Key: Value" per line
    [SerializeField] private TMP_InputField bodyInput;
    [SerializeField] private Button sendButton;
    [SerializeField] private Button cancelButton;
    [SerializeField] private int timeoutSeconds = 30;

    [Header("Response")]
    [SerializeField] private TMP_Text statusLabel;
    [SerializeField] private TMP_Text responseHeaders;
    [SerializeField] private TMP_Text responseBody;
    [SerializeField] private ScrollRect responseScroll;
    [SerializeField] private Button copyBodyButton;
    [SerializeField] private Button toFormatterButton;
    [SerializeField] private Toggle prettyJson;

    private static readonly string[] Methods = { "GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS" };
    private const string ColorOk = "#7AFFB0";
    private const string ColorWarn = "#FFD27A";
    private const string ColorErr = "#FF7A7A";
    private const string ColorDim = "#9A8FBF";
    private const string UrlKey = "DevToolBox.Http.LastUrl";

    private UnityWebRequest current;
    private Coroutine running;
    private string rawBody = "";

    private void Awake()
    {
        if (methodDropdown != null)
        {
            methodDropdown.ClearOptions();
            methodDropdown.AddOptions(new List<string>(Methods));
        }
        if (sendButton != null) sendButton.onClick.AddListener(Send);
        if (cancelButton != null) cancelButton.onClick.AddListener(Cancel);
        if (urlInput != null)
        {
            urlInput.onSubmit.AddListener(_ => Send());
            if (string.IsNullOrEmpty(urlInput.text)) urlInput.text = PlayerPrefs.GetString(UrlKey, "https://httpbin.org/get");
        }
        if (copyBodyButton != null) copyBodyButton.onClick.AddListener(() => { if (rawBody.Length > 0) GUIUtility.systemCopyBuffer = rawBody; });
        if (toFormatterButton != null) toFormatterButton.onClick.AddListener(() =>
        {
            if (rawBody.Length == 0 || JsonFormatterPanel.Instance == null) return;
            JsonFormatterPanel.Instance.SetInput(rawBody, true);
            TabsSwitcher.Instance?.Open("formatters");
        });
        if (prettyJson != null) prettyJson.onValueChanged.AddListener(_ => RenderBody());
        SetStatus("Enter a URL and press Send.", ColorDim);
    }

    public void Send()
    {
        if (running != null) return;
        string url = urlInput != null ? urlInput.text.Trim() : "";
        if (url.Length == 0) { SetStatus("URL is empty.", ColorErr); return; }
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            url = "https://" + url;

        PlayerPrefs.SetString(UrlKey, url);
        running = StartCoroutine(SendRoutine(url));
    }

    public void Cancel()
    {
        if (running == null) return;
        StopCoroutine(running);
        running = null;
        current?.Abort();
        current?.Dispose();
        current = null;
        SetStatus("Cancelled.", ColorWarn);
    }

    private IEnumerator SendRoutine(string url)
    {
        string method = methodDropdown != null ? Methods[Mathf.Clamp(methodDropdown.value, 0, Methods.Length - 1)] : "GET";
        string body = bodyInput != null ? bodyInput.text : "";
        bool hasBody = body.Length > 0 && method != "GET" && method != "HEAD";

        current = new UnityWebRequest(url, method)
        {
            downloadHandler = new DownloadHandlerBuffer(),
            timeout = timeoutSeconds
        };
        if (hasBody)
        {
            current.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            current.SetRequestHeader("Content-Type", LooksLikeJson(body) ? "application/json" : "text/plain; charset=utf-8");
        }
        current.SetRequestHeader("User-Agent", "DevToolBox/" + Application.version);

        foreach (string line in (headersInput != null ? headersInput.text : "").Split('\n'))
        {
            int colon = line.IndexOf(':');
            if (colon <= 0) continue;
            string key = line.Substring(0, colon).Trim();
            string value = line.Substring(colon + 1).Trim();
            if (key.Length == 0) continue;
            try { current.SetRequestHeader(key, value); }
            catch (Exception e) { SetStatus("Header '" + key + "' rejected: " + e.Message, ColorWarn); }
        }

        SetStatus(method + " " + url + " ...", ColorDim);
        if (responseHeaders != null) responseHeaders.text = "";
        rawBody = "";
        RenderBody();

        var sw = Stopwatch.StartNew();
        yield return current.SendWebRequest();
        sw.Stop();

        if (current == null) yield break; // cancelled meanwhile

        long code = current.responseCode;
        rawBody = current.downloadHandler != null ? current.downloadHandler.text ?? "" : "";
        int bytes = current.downloadHandler != null && current.downloadHandler.data != null ? current.downloadHandler.data.Length : 0;

        var hb = new StringBuilder();
        Dictionary<string, string> headers = current.GetResponseHeaders();
        if (headers != null)
            foreach (var kv in headers) hb.Append("<color=" + ColorDim + ">").Append(kv.Key).Append(":</color> ").Append(kv.Value).Append('\n');
        if (responseHeaders != null) responseHeaders.text = hb.ToString().TrimEnd();

        if (current.result == UnityWebRequest.Result.ConnectionError || current.result == UnityWebRequest.Result.DataProcessingError)
            SetStatus("Error: " + current.error + "  (" + sw.ElapsedMilliseconds + " ms)", ColorErr);
        else
        {
            string color = code < 300 ? ColorOk : code < 400 ? ColorWarn : ColorErr;
            SetStatus(code + " " + ReasonPhrase(code) + "  -  " + sw.ElapsedMilliseconds + " ms  -  " + FormatBytes(bytes), color);
        }

        RenderBody();
        current.Dispose();
        current = null;
        running = null;
    }

    private void RenderBody()
    {
        if (responseBody == null) return;
        string text = rawBody;
        if (text.Length > 0 && prettyJson != null && prettyJson.isOn && LooksLikeJson(text))
        {
            try { text = JsonTool.Format(text, 2); } catch (JsonTool.JsonError) { }
        }
        const int maxChars = 60000; // TMP mesh limit safety
        if (text.Length > maxChars) text = text.Substring(0, maxChars) + "\n<color=" + ColorDim + ">... truncated (" + rawBody.Length + " characters total, Copy gives you everything)</color>";
        responseBody.text = text.Replace("<", "<​"); // keep the response from being parsed as TMP rich text
        if (responseScroll != null) responseScroll.verticalNormalizedPosition = 1f;
    }

    private static bool LooksLikeJson(string s)
    {
        string t = s.TrimStart();
        return t.StartsWith("{") || t.StartsWith("[");
    }

    private static string FormatBytes(int bytes) =>
        bytes < 1024 ? bytes + " B" : bytes < 1024 * 1024 ? (bytes / 1024f).ToString("0.0") + " KB" : (bytes / 1048576f).ToString("0.00") + " MB";

    private static string ReasonPhrase(long code)
    {
        switch (code)
        {
            case 200: return "OK"; case 201: return "Created"; case 204: return "No Content";
            case 301: return "Moved Permanently"; case 302: return "Found"; case 304: return "Not Modified";
            case 400: return "Bad Request"; case 401: return "Unauthorized"; case 403: return "Forbidden"; case 404: return "Not Found";
            case 405: return "Method Not Allowed"; case 408: return "Request Timeout"; case 429: return "Too Many Requests";
            case 500: return "Internal Server Error"; case 502: return "Bad Gateway"; case 503: return "Service Unavailable"; case 504: return "Gateway Timeout";
            default: return "";
        }
    }

    private void SetStatus(string text, string color)
    {
        if (statusLabel != null) statusLabel.text = "<color=" + color + ">" + text + "</color>";
    }
}
