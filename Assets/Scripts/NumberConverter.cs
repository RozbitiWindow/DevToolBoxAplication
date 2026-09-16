using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One universal converter: pick the input base, type a number, see it in all
/// other bases live. Copy buttons put the value in the system clipboard.
/// </summary>
public class NumberConverter : MonoBehaviour
{
    private enum Base { Decimal = 10, Binary = 2, Hexadecimal = 16, Octal = 8 }

    [Header("Input")]
    [SerializeField] private TMP_Dropdown fromDropdown;
    [SerializeField] private TMP_InputField input;
    [SerializeField] private TMP_Text errorLabel;

    [Header("Outputs")]
    [SerializeField] private TMP_Text decimalOutput;
    [SerializeField] private TMP_Text binaryOutput;
    [SerializeField] private TMP_Text hexOutput;
    [SerializeField] private TMP_Text octalOutput;

    [Header("Copy buttons")]
    [SerializeField] private Button copyDecimal;
    [SerializeField] private Button copyBinary;
    [SerializeField] private Button copyHex;
    [SerializeField] private Button copyOctal;

    // Binary grouping (1111 1111) is a global setting: AppSettings.GroupBinaryNibbles.

    private static readonly Base[] DropdownOrder = { Base.Decimal, Base.Binary, Base.Hexadecimal, Base.Octal };

    private long? currentValue;

    private void Awake()
    {
        if (fromDropdown != null)
        {
            fromDropdown.ClearOptions();
            fromDropdown.AddOptions(new List<string> { "Decimal", "Binary", "Hexadecimal", "Octal" });
            fromDropdown.onValueChanged.AddListener(_ => Convert());
        }
        if (input != null) input.onValueChanged.AddListener(_ => Convert());

        Bind(copyDecimal, () => currentValue?.ToString(CultureInfo.InvariantCulture));
        Bind(copyBinary, () => currentValue.HasValue ? System.Convert.ToString(currentValue.Value, 2) : null);
        Bind(copyHex, () => currentValue?.ToString("X"));
        Bind(copyOctal, () => currentValue.HasValue ? System.Convert.ToString(currentValue.Value, 8) : null);

        Convert();
    }

    private void OnEnable()
    {
        AppSettings.Changed += Convert;
    }

    private void OnDisable()
    {
        AppSettings.Changed -= Convert;
    }

    private void Bind(Button button, Func<string> getter)
    {
        if (button == null) return;
        button.onClick.AddListener(() =>
        {
            string value = getter();
            if (!string.IsNullOrEmpty(value)) GUIUtility.systemCopyBuffer = value;
        });
    }

    private Base SelectedBase =>
        fromDropdown != null && fromDropdown.value >= 0 && fromDropdown.value < DropdownOrder.Length
            ? DropdownOrder[fromDropdown.value]
            : Base.Decimal;

    public void Convert()
    {
        string raw = input != null ? input.text : "";
        string text = Normalize(raw, SelectedBase);

        if (string.IsNullOrEmpty(text))
        {
            currentValue = null;
            ShowOutputs("-", "-", "-", "-");
            SetError("");
            return;
        }

        if (!TryParse(text, SelectedBase, out long value))
        {
            currentValue = null;
            ShowOutputs("-", "-", "-", "-");
            SetError("Not a valid " + SelectedBase.ToString().ToLower() + " number");
            return;
        }

        currentValue = value;
        SetError("");

        string binary = System.Convert.ToString(value, 2);
        if (AppSettings.GroupBinaryNibbles) binary = GroupNibbles(binary);

        ShowOutputs(
            value.ToString(CultureInfo.InvariantCulture),
            binary,
            value.ToString("X"),
            System.Convert.ToString(value, 8));
    }

    private static string Normalize(string raw, Base b)
    {
        if (raw == null) return "";
        string s = raw.Trim().Replace(" ", "").Replace("_", "");
        string lower = s.ToLowerInvariant();
        if (b == Base.Hexadecimal && lower.StartsWith("0x")) s = s.Substring(2);
        else if (b == Base.Hexadecimal && s.StartsWith("#")) s = s.Substring(1);
        else if (b == Base.Binary && lower.StartsWith("0b")) s = s.Substring(2);
        else if (b == Base.Octal && lower.StartsWith("0o")) s = s.Substring(2);
        return s;
    }

    private static bool TryParse(string text, Base b, out long value)
    {
        value = 0;
        try
        {
            if (b == Base.Decimal)
                return long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);

            bool negative = text.StartsWith("-");
            if (negative) text = text.Substring(1);
            if (text.Length == 0) return false;

            // Convert.ToInt64 with a base treats a full 64-bit pattern as two's complement;
            // go through UInt64 so "FFFFFFFFFFFFFFFF" is rejected instead of becoming -1.
            ulong magnitude = System.Convert.ToUInt64(text, (int)b);
            if (magnitude > long.MaxValue) return false;
            value = negative ? -(long)magnitude : (long)magnitude;
            return true;
        }
        catch (FormatException) { return false; }
        catch (OverflowException) { return false; }
        catch (ArgumentException) { return false; }
    }

    private static string GroupNibbles(string binary)
    {
        bool negative = binary.StartsWith("-");
        if (negative) binary = binary.Substring(1);

        int pad = (4 - binary.Length % 4) % 4;
        binary = binary.PadLeft(binary.Length + pad, '0');

        var sb = new System.Text.StringBuilder(binary.Length + binary.Length / 4);
        for (int i = 0; i < binary.Length; i++)
        {
            if (i > 0 && i % 4 == 0) sb.Append(' ');
            sb.Append(binary[i]);
        }
        return (negative ? "-" : "") + sb;
    }

    private void ShowOutputs(string dec, string bin, string hex, string oct)
    {
        if (decimalOutput != null) decimalOutput.text = dec;
        if (binaryOutput != null) binaryOutput.text = bin;
        if (hexOutput != null) hexOutput.text = hex;
        if (octalOutput != null) octalOutput.text = oct;
    }

    private void SetError(string message)
    {
        if (errorLabel == null) return;
        errorLabel.text = message;
        errorLabel.gameObject.SetActive(!string.IsNullOrEmpty(message));
    }
}
