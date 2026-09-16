using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Generators tab: UUID, password (crypto-random) and unix timestamp <-> date.
/// Honors AppSettings.AutoCopy for generated values.
/// </summary>
public class Generators : MonoBehaviour
{
    [Header("UUID")]
    [SerializeField] private TMP_Text uuidOutput;
    [SerializeField] private Button uuidGenerate;
    [SerializeField] private Button uuidCopy;

    [Header("Password")]
    [SerializeField] private Slider passwordLength;
    [SerializeField] private TMP_Text passwordLengthLabel;
    [SerializeField] private Toggle useLower;
    [SerializeField] private Toggle useUpper;
    [SerializeField] private Toggle useDigits;
    [SerializeField] private Toggle useSymbols;
    [SerializeField] private TMP_Text passwordOutput;
    [SerializeField] private Button passwordGenerate;
    [SerializeField] private Button passwordCopy;

    [Header("Timestamp")]
    [SerializeField] private TMP_InputField timestampInput;
    [SerializeField] private Button timestampNow;
    [SerializeField] private Button timestampCopy;
    [SerializeField] private TMP_Text timestampOutput;

    private const string Lower = "abcdefghijklmnopqrstuvwxyz";
    private const string Upper = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string Digits = "0123456789";
    private const string Symbols = "!@#$%^&*()-_=+[]{};:,.<>?";

    private long? currentUnix;

    private void Awake()
    {
        if (uuidGenerate != null) uuidGenerate.onClick.AddListener(GenerateUuid);
        if (uuidCopy != null) uuidCopy.onClick.AddListener(() => Copy(uuidOutput));

        if (passwordLength != null)
        {
            passwordLength.wholeNumbers = true;
            passwordLength.minValue = 4;
            passwordLength.maxValue = 64;
            passwordLength.onValueChanged.AddListener(v => { if (passwordLengthLabel != null) passwordLengthLabel.text = ((int)v).ToString(); });
            if (passwordLengthLabel != null) passwordLengthLabel.text = ((int)passwordLength.value).ToString();
        }
        if (passwordGenerate != null) passwordGenerate.onClick.AddListener(GeneratePassword);
        if (passwordCopy != null) passwordCopy.onClick.AddListener(() => Copy(passwordOutput));

        if (timestampNow != null) timestampNow.onClick.AddListener(() =>
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (timestampInput != null) timestampInput.SetTextWithoutNotify(now.ToString(CultureInfo.InvariantCulture));
            ShowTimestamp(now);
        });
        if (timestampInput != null) timestampInput.onValueChanged.AddListener(ParseTimestamp);
        if (timestampCopy != null) timestampCopy.onClick.AddListener(() =>
        {
            if (currentUnix.HasValue) GUIUtility.systemCopyBuffer = currentUnix.Value.ToString(CultureInfo.InvariantCulture);
        });

        ShowTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }

    // ---------- UUID ----------

    public void GenerateUuid()
    {
        string uuid = Guid.NewGuid().ToString();
        if (uuidOutput != null) uuidOutput.text = uuid;
        if (AppSettings.AutoCopy) GUIUtility.systemCopyBuffer = uuid;
    }

    // ---------- Password ----------

    public void GeneratePassword()
    {
        var pool = new StringBuilder();
        if (useLower == null || useLower.isOn) pool.Append(Lower);
        if (useUpper != null && useUpper.isOn) pool.Append(Upper);
        if (useDigits != null && useDigits.isOn) pool.Append(Digits);
        if (useSymbols != null && useSymbols.isOn) pool.Append(Symbols);

        if (pool.Length == 0)
        {
            if (passwordOutput != null) passwordOutput.text = "Pick at least one character set";
            return;
        }

        int length = passwordLength != null ? (int)passwordLength.value : 16;
        string chars = pool.ToString();
        var result = new char[length];

        using (var rng = RandomNumberGenerator.Create())
        {
            var buffer = new byte[4];
            for (int i = 0; i < length; i++)
            {
                rng.GetBytes(buffer);
                uint value = BitConverter.ToUInt32(buffer, 0);
                result[i] = chars[(int)(value % (uint)chars.Length)];
            }
        }

        string password = new string(result);
        if (passwordOutput != null) passwordOutput.text = password;
        if (AppSettings.AutoCopy) GUIUtility.systemCopyBuffer = password;
    }

    // ---------- Timestamp ----------

    private void ParseTimestamp(string raw)
    {
        string text = (raw ?? "").Trim();
        if (text.Length == 0)
        {
            ShowTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            return;
        }

        if (long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long unix))
        {
            // 13+ digits: treat as milliseconds
            if (Math.Abs(unix) >= 100000000000L) unix /= 1000;
            ShowTimestamp(unix);
            return;
        }

        // Also accept a date string and convert it back to unix time.
        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime parsed))
        {
            ShowTimestamp(new DateTimeOffset(parsed).ToUnixTimeSeconds());
            return;
        }

        currentUnix = null;
        if (timestampOutput != null) timestampOutput.text = "Enter unix seconds, milliseconds or a date (2024-05-01 12:00)";
    }

    private void ShowTimestamp(long unix)
    {
        DateTimeOffset utc;
        try { utc = DateTimeOffset.FromUnixTimeSeconds(unix); }
        catch (ArgumentOutOfRangeException)
        {
            currentUnix = null;
            if (timestampOutput != null) timestampOutput.text = "Out of range";
            return;
        }

        currentUnix = unix;
        if (timestampOutput == null) return;

        DateTimeOffset local = utc.ToLocalTime();
        timestampOutput.text =
            "Unix:   " + unix.ToString(CultureInfo.InvariantCulture) + "\n" +
            "UTC:    " + utc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "\n" +
            "Local:  " + local.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " (UTC" + local.ToString("zzz", CultureInfo.InvariantCulture) + ")\n" +
            "ISO:    " + utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }

    private static void Copy(TMP_Text source)
    {
        if (source != null && !string.IsNullOrEmpty(source.text)) GUIUtility.systemCopyBuffer = source.text;
    }
}
