using System.Globalization;

namespace MkMermaid.Theming;

/// <summary>Resolved Mermaid theme variables (flattened: nested objects use dotted keys such as <c>xyChart.titleColor</c>).</summary>
internal sealed class Theme
{
    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly IReadOnlyDictionary<string, string> _overrides;

    private Theme(string name, IReadOnlyDictionary<string, string> overrides)
    {
        Name = name;
        _overrides = overrides;
    }

    public static IReadOnlyList<string> Names { get; } = ["default", "base", "dark", "forest", "neutral"];

    public string Name { get; }

    public bool DarkMode => string.Equals(Get("darkMode"), "true", StringComparison.OrdinalIgnoreCase);

    public string this[string key] => Get(key) ?? "#000000";

    public string? Get(string key) => _values.TryGetValue(key, out var v) ? v : _overrides.TryGetValue(key, out var o) ? o : null;

    public Color C(string key) => Color.TryParse(Get(key), out var c) ? c : Color.Black;

    /// <summary>Color variable normalized to #RRGGBB(AA); falls back to <paramref name="fallback"/> when unset or invalid.</summary>
    public string Hex(string key, string? fallback = null) =>
        Color.TryParse(Get(key), out var c) ? c.ToString() : fallback is null ? "#000000" : Color.Parse(fallback).ToString();

    public double Number(string key, double fallback)
    {
        var value = Get(key);
        if (value is null)
        {
            return fallback;
        }

        value = value.Trim();
        if (value.EndsWith("px", StringComparison.OrdinalIgnoreCase))
        {
            value = value[..^2];
        }

        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && Config.MermaidConfig.InRange(n) ? n : fallback;
    }

    public static Theme Build(string? name, IReadOnlyDictionary<string, string>? overrides)
    {
        var normalized = Names.FirstOrDefault(n => n.Equals(name?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? "default";
        var theme = new Theme(normalized, overrides ?? new Dictionary<string, string>());
        switch (normalized)
        {
            case "base":
                theme.DefineBase();
                break;
            case "dark":
                theme.DefineDark();
                break;
            case "forest":
                theme.DefineForest();
                break;
            case "neutral":
                theme.DefineNeutral();
                break;
            default:
                theme.DefineDefault();
                break;
        }

        theme.DefineCommon();
        return theme;
    }

    private void V(string key, string value)
    {
        if (_values.ContainsKey(key))
        {
            return;
        }

        _values[key] = _overrides.TryGetValue(key, out var o) && o.Length > 0 ? o : value;
    }

    private void V(string key, Func<Color> value)
    {
        if (_values.ContainsKey(key))
        {
            return;
        }

        if (_overrides.TryGetValue(key, out var o) && o.Length > 0)
        {
            _values[key] = o;
            return;
        }

        _values[key] = value().ToString();
    }

    private Color Border(string key) => DarkMode ? C(key).Adjust(s: -40, l: 10) : C(key).Adjust(s: -40, l: -10);

    private Color Shade(Color color) => DarkMode ? color.Lighten(25) : color.Darken(25);

    private void DefineDefault()
    {
        V("darkMode", "false");
        V("background", "#FFFFFF");
        V("primaryColor", "#ECECFF");
        V("secondaryColor", "#FFFFDE");
        V("tertiaryColor", "hsl(80, 100%, 96.2745098039%)");
        V("primaryBorderColor", () => Border("primaryColor"));
        V("primaryTextColor", "#131300");
        V("lineColor", "#333333");
        V("textColor", "#333333");
        V("mainBkg", "#ECECFF");
        V("nodeBorder", "#9370DB");
        V("clusterBkg", "#FFFFDE");
        V("clusterBorder", "#AAAA33");
        V("titleColor", "#333333");
        V("edgeLabelBackground", "#E8E8E8CC");
        V("actorBorder", "hsl(259.6261682243, 59.7765363128%, 87.9019607843%)");
        V("actorBkg", "#ECECFF");
        V("actorTextColor", "#000000");
        V("actorLineColor", "#808080");
        V("signalColor", "#333333");
        V("signalTextColor", "#333333");
        V("labelBoxBkgColor", "#ECECFF");
        V("labelBoxBorderColor", () => C("actorBorder"));
        V("labelTextColor", "#000000");
        V("loopTextColor", "#000000");
        V("noteBkgColor", "#FFF5AD");
        V("noteBorderColor", "#AAAA33");
        V("noteTextColor", "#333333");
        V("activationBkgColor", "#F4F4F4");
        V("activationBorderColor", "#666666");
        V("sequenceNumberColor", "#FFFFFF");
        V("sectionBkgColor", "#6666FF7D");
        V("altSectionBkgColor", "#FFFFFF");
        V("sectionBkgColor2", "#FFF400");
        V("taskBorderColor", "#534FBC");
        V("taskBkgColor", "#8A90DD");
        V("taskTextColor", "#FFFFFF");
        V("taskTextOutsideColor", "#000000");
        V("taskTextDarkColor", "#000000");
        V("activeTaskBorderColor", "#534FBC");
        V("activeTaskBkgColor", "#BFC7FF");
        V("gridColor", "#D3D3D3");
        V("doneTaskBkgColor", "#D3D3D3");
        V("doneTaskBorderColor", "#808080");
        V("critBorderColor", "#FF8888");
        V("critBkgColor", "#FF0000");
        V("todayLineColor", "#FF0000");
        V("transitionColor", "#333333");
        V("stateBkg", "#ECECFF");
        V("compositeBackground", "#FFFFFF");
        V("compositeTitleBackground", "#F4F4F4");
        V("innerEndBackground", "#9370DB");
        V("specialStateColor", "#333333");
        V("classText", "#131300");
        V("attributeBackgroundColorOdd", "#FFFFFF");
        V("attributeBackgroundColorEven", "#F2F2F2");
        V("requirementBackground", "#ECECFF");
        V("requirementBorderColor", "#9370DB");
        V("relationColor", "#333333");
        V("relationLabelBackground", "#E8E8E8");
        V("relationLabelColor", "#000000");
        V("errorBkgColor", "#552222");
        V("errorTextColor", "#552222");
        V("git0", "hsl(240, 100%, 46.2745098039%)");
        V("git1", "hsl(60, 100%, 43.5294117647%)");
        V("git2", "hsl(80, 100%, 46.2745098039%)");
        V("git3", "hsl(210, 100%, 46.2745098039%)");
        V("git4", "hsl(180, 100%, 46.2745098039%)");
        V("git5", "hsl(150, 100%, 46.2745098039%)");
        V("git6", "hsl(300, 100%, 46.2745098039%)");
        V("git7", "hsl(0, 100%, 46.2745098039%)");
        V("commitLabelColor", "#000021");
        V("commitLabelBackground", "#FFFFDE");
        for (var i = 0; i < 12; i++)
        {
            var hue = i switch { 0 => 0, 1 => -120, 2 => 180, _ => 30 * (i - 2) };
            V("cScale" + i, () => i switch { 0 => C("primaryColor"), 1 => C("secondaryColor"), 2 => C("tertiaryColor"), _ => C("primaryColor").Adjust(h: hue) });
        }
    }

    private void DefineBase()
    {
        V("darkMode", "false");
        V("background", "#F4F4F4");
        V("primaryColor", "#FFF4DD");
        V("noteBkgColor", "#FFF5AD");
        V("noteTextColor", "#333333");
        V("primaryTextColor", () => DarkMode ? Color.Parse("#EEEEEE") : Color.Parse("#333333"));
        V("secondaryColor", () => C("primaryColor").Adjust(h: -120));
        V("tertiaryColor", () => C("primaryColor").Adjust(h: 180, l: 5));
        V("primaryBorderColor", () => Border("primaryColor"));
        V("secondaryBorderColor", () => Border("secondaryColor"));
        V("tertiaryBorderColor", () => Border("tertiaryColor"));
        V("noteBorderColor", () => Border("noteBkgColor"));
        V("secondaryTextColor", () => C("secondaryColor").Invert());
        V("tertiaryTextColor", () => C("tertiaryColor").Invert());
        V("lineColor", () => C("background").Invert());
        V("textColor", () => C("background").Invert());
        V("mainBkg", () => C("primaryColor"));
        V("nodeBorder", () => C("primaryBorderColor"));
        V("clusterBkg", () => C("tertiaryColor"));
        V("clusterBorder", () => C("tertiaryBorderColor"));
        V("titleColor", () => C("tertiaryTextColor"));
        V("edgeLabelBackground", () => DarkMode ? C("secondaryColor").Darken(30) : C("secondaryColor"));
        V("actorBorder", () => C("primaryBorderColor"));
        V("actorBkg", () => C("mainBkg"));
        V("actorTextColor", () => C("primaryTextColor"));
        V("actorLineColor", () => C("actorBorder"));
        V("signalColor", () => C("textColor"));
        V("signalTextColor", () => C("textColor"));
        V("labelBoxBkgColor", () => C("actorBkg"));
        V("labelBoxBorderColor", () => C("actorBorder"));
        V("labelTextColor", () => C("actorTextColor"));
        V("loopTextColor", () => C("actorTextColor"));
        V("activationBorderColor", () => C("secondaryColor").Darken(10));
        V("activationBkgColor", () => C("secondaryColor"));
        V("sequenceNumberColor", () => C("lineColor").Invert());
        V("sectionBkgColor", () => C("tertiaryColor"));
        V("altSectionBkgColor", "#FFFFFF");
        V("sectionBkgColor2", () => C("primaryColor"));
        V("taskBorderColor", () => C("primaryBorderColor"));
        V("taskBkgColor", () => C("primaryColor"));
        V("taskTextColor", () => C("textColor"));
        V("taskTextOutsideColor", () => C("textColor"));
        V("taskTextDarkColor", () => C("textColor"));
        V("activeTaskBorderColor", () => C("primaryColor"));
        V("activeTaskBkgColor", () => C("primaryColor").Lighten(23));
        V("gridColor", "#D3D3D3");
        V("doneTaskBkgColor", "#D3D3D3");
        V("doneTaskBorderColor", "#808080");
        V("critBorderColor", "#FF8888");
        V("critBkgColor", "#FF0000");
        V("todayLineColor", "#FF0000");
        V("transitionColor", () => C("lineColor"));
        V("stateBkg", () => C("mainBkg"));
        V("compositeBackground", () => C("background"));
        V("compositeTitleBackground", () => C("mainBkg"));
        V("innerEndBackground", () => C("primaryBorderColor"));
        V("specialStateColor", () => C("lineColor"));
        V("classText", () => C("primaryTextColor"));
        V("attributeBackgroundColorOdd", () => DarkMode ? C("background").Lighten(5) : Color.White);
        V("attributeBackgroundColorEven", () => DarkMode ? C("background").Lighten(10) : Color.Parse("#F2F2F2"));
        V("requirementBackground", () => C("primaryColor"));
        V("requirementBorderColor", () => C("primaryBorderColor"));
        V("relationColor", () => C("lineColor"));
        V("relationLabelBackground", () => DarkMode ? C("secondaryColor").Darken(30) : C("secondaryColor"));
        V("relationLabelColor", () => C("actorTextColor"));
        V("errorBkgColor", () => C("tertiaryColor"));
        V("errorTextColor", () => C("tertiaryTextColor"));
        V("git0", () => Shade(C("primaryColor")));
        V("git1", () => Shade(C("secondaryColor")));
        V("git2", () => Shade(C("tertiaryColor")));
        V("git3", () => Shade(C("primaryColor").Adjust(h: -30)));
        V("git4", () => Shade(C("primaryColor").Adjust(h: -60)));
        V("git5", () => Shade(C("primaryColor").Adjust(h: -90)));
        V("git6", () => Shade(C("primaryColor").Adjust(h: 60)));
        V("git7", () => Shade(C("primaryColor").Adjust(h: 120)));
        V("commitLabelColor", () => C("secondaryTextColor"));
        V("commitLabelBackground", () => C("secondaryColor"));
        for (var i = 0; i < 12; i++)
        {
            var hue = i switch { 0 => 0, 1 => -120, 2 => 180, _ => 30 * (i - 2) };
            V("cScale" + i, () =>
            {
                var c = i switch { 0 => C("primaryColor"), 1 => C("secondaryColor"), 2 => C("tertiaryColor"), _ => C("primaryColor").Adjust(h: hue) };
                return DarkMode ? c.Darken(75) : c.Darken(10);
            });
        }
    }

    private void DefineDark()
    {
        V("darkMode", "true");
        V("background", "#333333");
        V("primaryColor", "#1F2020");
        V("secondaryColor", () => C("primaryColor").Lighten(16));
        V("tertiaryColor", () => C("primaryColor").Adjust(h: -160));
        V("primaryBorderColor", () => C("background").Invert());
        V("primaryTextColor", () => C("primaryColor").Invert());
        V("lineColor", () => C("background").Invert());
        V("textColor", "#CCCCCC");
        V("mainBkg", "#1F2020");
        V("nodeBorder", "#CCCCCC");
        V("clusterBkg", "#302F3D");
        V("clusterBorder", "#FFFFFF40");
        V("titleColor", "#F9FFFE");
        V("edgeLabelBackground", "#585858");
        V("actorBorder", "#CCCCCC");
        V("actorBkg", "#1F2020");
        V("actorTextColor", "#D3D3D3");
        V("actorLineColor", "#CCCCCC");
        V("signalColor", "#D3D3D3");
        V("signalTextColor", "#D3D3D3");
        V("labelBoxBkgColor", "#1F2020");
        V("labelBoxBorderColor", "#CCCCCC");
        V("labelTextColor", "#D3D3D3");
        V("loopTextColor", "#D3D3D3");
        V("noteBkgColor", "#FFF5AD");
        V("noteBorderColor", "#AAAA33");
        V("noteTextColor", "#1F2020");
        V("activationBkgColor", "#454545");
        V("activationBorderColor", "#CCCCCC");
        V("sequenceNumberColor", "#000000");
        V("sectionBkgColor", "#545454");
        V("altSectionBkgColor", "#333333");
        V("sectionBkgColor2", "#EAE8D9");
        V("excludeBkgColor", () => C("sectionBkgColor").Darken(10));
        V("taskBorderColor", "#FFFFFF7F");
        V("taskBkgColor", "#BFB8A0");
        V("taskTextColor", "#000000");
        V("taskTextOutsideColor", "#D3D3D3");
        V("taskTextDarkColor", "#D3D3D3");
        V("activeTaskBorderColor", "#FFFFFF7F");
        V("activeTaskBkgColor", "#81B1DB");
        V("gridColor", "#D3D3D3");
        V("doneTaskBkgColor", "#D3D3D3");
        V("doneTaskBorderColor", "#808080");
        V("critBorderColor", "#E83737");
        V("critBkgColor", "#E83737");
        V("todayLineColor", "#DB5757");
        V("transitionColor", "#D3D3D3");
        V("stateBkg", "#1F2020");
        V("compositeBackground", "#333333");
        V("compositeTitleBackground", "#4A4A4A");
        V("innerEndBackground", "#CCCCCC");
        V("specialStateColor", "#F4F4F4");
        V("classText", () => C("primaryTextColor"));
        V("attributeBackgroundColorOdd", "#3A3A3A");
        V("attributeBackgroundColorEven", "#454545");
        V("requirementBackground", "#1F2020");
        V("requirementBorderColor", "#CCCCCC");
        V("relationColor", "#D3D3D3");
        V("relationLabelBackground", "#585858");
        V("relationLabelColor", "#D3D3D3");
        V("errorBkgColor", "#A44141");
        V("errorTextColor", "#DDDDDD");
        V("git0", "#7272FF");
        V("git1", "#FFFF6E");
        V("git2", "#B8FF6E");
        V("git3", "#6EB8FF");
        V("git4", "#6EFFFF");
        V("git5", "#6EFFB8");
        V("git6", "#FF6EFF");
        V("git7", "#FF6E6E");
        V("commitLabelColor", "#D3D3D3");
        V("commitLabelBackground", "#454545");
        for (var i = 0; i < 12; i++)
        {
            var hue = i switch { 0 => 0, 1 => -120, 2 => 180, _ => 30 * (i - 2) };
            V("cScale" + i, () => Color.Parse("#0B0B6E").Adjust(h: hue).Lighten(i == 0 ? 0 : 5));
        }
    }

    private void DefineForest()
    {
        V("darkMode", "false");
        V("background", "#FFFFFF");
        V("primaryColor", "#CDE498");
        V("secondaryColor", "#CDFFB2");
        V("tertiaryColor", () => C("primaryColor").Adjust(h: -160).Lighten(5));
        V("primaryBorderColor", "#13540C");
        V("primaryTextColor", "#000000");
        V("lineColor", "#008000");
        V("textColor", "#000000");
        V("mainBkg", "#CDE498");
        V("nodeBorder", "#13540C");
        V("clusterBkg", "#CDFFB2");
        V("clusterBorder", "#6EAA49");
        V("titleColor", "#333333");
        V("edgeLabelBackground", "#E8E8E8");
        V("actorBorder", "#13540C");
        V("actorBkg", "#CDE498");
        V("actorTextColor", "#000000");
        V("actorLineColor", "#13540C");
        V("signalColor", "#333333");
        V("signalTextColor", "#333333");
        V("labelBoxBkgColor", "#CDE498");
        V("labelBoxBorderColor", "#326932");
        V("labelTextColor", "#000000");
        V("loopTextColor", "#000000");
        V("noteBkgColor", "#FFF5AD");
        V("noteBorderColor", "#6EAA49");
        V("noteTextColor", "#000000");
        V("activationBkgColor", "#DADADA");
        V("activationBorderColor", "#666666");
        V("sequenceNumberColor", "#FFFFFF");
        V("sectionBkgColor", "#6EAA49");
        V("altSectionBkgColor", "#FFFFFF");
        V("sectionBkgColor2", "#6EAA49");
        V("taskBorderColor", "#13540C");
        V("taskBkgColor", "#487E3A");
        V("taskTextColor", "#FFFFFF");
        V("taskTextOutsideColor", "#000000");
        V("taskTextDarkColor", "#000000");
        V("activeTaskBorderColor", "#13540C");
        V("activeTaskBkgColor", "#CDE498");
        V("gridColor", "#D3D3D3");
        V("doneTaskBkgColor", "#D3D3D3");
        V("doneTaskBorderColor", "#808080");
        V("critBorderColor", "#FF8888");
        V("critBkgColor", "#FF0000");
        V("todayLineColor", "#FF0000");
        V("transitionColor", "#008000");
        V("stateBkg", "#CDE498");
        V("compositeBackground", "#FFFFFF");
        V("compositeTitleBackground", "#F4F4F4");
        V("innerEndBackground", "#13540C");
        V("specialStateColor", "#008000");
        V("classText", "#000000");
        V("attributeBackgroundColorOdd", "#FFFFFF");
        V("attributeBackgroundColorEven", "#F2F2F2");
        V("requirementBackground", "#CDE498");
        V("requirementBorderColor", "#13540C");
        V("relationColor", "#008000");
        V("relationLabelBackground", "#E8E8E8");
        V("relationLabelColor", "#000000");
        V("errorBkgColor", "#552222");
        V("errorTextColor", "#552222");
        V("git0", () => C("primaryColor").Darken(25));
        V("git1", () => C("secondaryColor").Darken(25));
        V("git2", () => C("tertiaryColor").Darken(25));
        V("git3", () => C("primaryColor").Adjust(h: -30).Darken(25));
        V("git4", () => C("primaryColor").Adjust(h: -60).Darken(25));
        V("git5", () => C("primaryColor").Adjust(h: -90).Darken(25));
        V("git6", () => C("primaryColor").Adjust(h: 60).Darken(25));
        V("git7", () => C("primaryColor").Adjust(h: 120).Darken(25));
        V("commitLabelColor", "#000000");
        V("commitLabelBackground", "#CDFFB2");
        for (var i = 0; i < 12; i++)
        {
            var hue = i switch { 0 => 0, 1 => -120, 2 => 180, _ => 30 * (i - 2) };
            V("cScale" + i, () => i switch { 0 => C("primaryColor"), 1 => C("secondaryColor"), 2 => C("tertiaryColor"), _ => C("primaryColor").Adjust(h: hue) });
        }
    }

    private void DefineNeutral()
    {
        V("darkMode", "false");
        V("background", "#FFFFFF");
        V("primaryColor", "#EEEEEE");
        V("secondaryColor", "#E1E1E1");
        V("tertiaryColor", "#F4F4F4");
        V("primaryBorderColor", "#999999");
        V("primaryTextColor", "#333333");
        V("lineColor", "#666666");
        V("textColor", "#333333");
        V("mainBkg", "#EEEEEE");
        V("nodeBorder", "#999999");
        V("clusterBkg", "#F4F4F4");
        V("clusterBorder", "#707070");
        V("titleColor", "#333333");
        V("edgeLabelBackground", "#FFFFFF");
        V("actorBorder", "#707070");
        V("actorBkg", "#EEEEEE");
        V("actorTextColor", "#333333");
        V("actorLineColor", "#666666");
        V("signalColor", "#333333");
        V("signalTextColor", "#333333");
        V("labelBoxBkgColor", "#EEEEEE");
        V("labelBoxBorderColor", "#707070");
        V("labelTextColor", "#333333");
        V("loopTextColor", "#333333");
        V("noteBkgColor", "#666666");
        V("noteBorderColor", "#999999");
        V("noteTextColor", "#FFFFFF");
        V("activationBkgColor", "#F4F4F4");
        V("activationBorderColor", "#666666");
        V("sequenceNumberColor", "#FFFFFF");
        V("sectionBkgColor", "#D9D9D9");
        V("altSectionBkgColor", "#FFFFFF");
        V("sectionBkgColor2", "#BFBFBF");
        V("taskBorderColor", "#707070");
        V("taskBkgColor", "#999999");
        V("taskTextColor", "#FFFFFF");
        V("taskTextOutsideColor", "#000000");
        V("taskTextDarkColor", "#000000");
        V("activeTaskBorderColor", "#707070");
        V("activeTaskBkgColor", "#D9D9D9");
        V("gridColor", "#D3D3D3");
        V("doneTaskBkgColor", "#BFBFBF");
        V("doneTaskBorderColor", "#666666");
        V("critBorderColor", "#D42E2E");
        V("critBkgColor", "#D42E2E");
        V("todayLineColor", "#D42E2E");
        V("transitionColor", "#666666");
        V("stateBkg", "#EEEEEE");
        V("compositeBackground", "#FFFFFF");
        V("compositeTitleBackground", "#F4F4F4");
        V("innerEndBackground", "#666666");
        V("specialStateColor", "#666666");
        V("classText", "#333333");
        V("attributeBackgroundColorOdd", "#FFFFFF");
        V("attributeBackgroundColorEven", "#F4F4F4");
        V("requirementBackground", "#EEEEEE");
        V("requirementBorderColor", "#999999");
        V("relationColor", "#666666");
        V("relationLabelBackground", "#FFFFFF");
        V("relationLabelColor", "#333333");
        V("errorBkgColor", "#552222");
        V("errorTextColor", "#552222");
        V("git0", "#404040");
        V("git1", "#5C5C5C");
        V("git2", "#737373");
        V("git3", "#8C8C8C");
        V("git4", "#A6A6A6");
        V("git5", "#595959");
        V("git6", "#4D4D4D");
        V("git7", "#262626");
        V("commitLabelColor", "#000000");
        V("commitLabelBackground", "#E1E1E1");
        for (var i = 0; i < 12; i++)
        {
            var l = 40 + (i * 37 % 45);
            V("cScale" + i, () => Color.FromHsl(0, 0, l));
        }
    }

    /// <summary>Variables every theme derives the same way from its base colors.</summary>
    private void DefineCommon()
    {
        V("fontFamily", "\"trebuchet ms\", verdana, arial, sans-serif");
        V("fontSize", "16px");
        V("secondaryBorderColor", () => Border("secondaryColor"));
        V("tertiaryBorderColor", () => Border("tertiaryColor"));
        V("secondaryTextColor", () => C("secondaryColor").Invert());
        V("tertiaryTextColor", () => C("tertiaryColor").Invert());
        V("nodeTextColor", () => C("primaryTextColor"));
        V("stateLabelColor", () => C("primaryTextColor"));
        V("transitionLabelColor", () => C("textColor"));
        V("compositeBorder", () => C("nodeBorder"));
        V("altBackground", () => DarkMode ? C("background").Lighten(10) : Color.Parse("#F0F0F0"));
        V("taskBorderOutside", () => C("taskBorderColor"));
        V("excludeBkgColor", "#EEEEEE");
        V("requirementTextColor", () => C("primaryTextColor"));
        V("tagLabelColor", () => C("primaryTextColor"));
        V("tagLabelBackground", () => C("primaryColor"));
        V("tagLabelBorder", () => C("primaryBorderColor"));
        for (var i = 0; i < 8; i++)
        {
            var key = "git" + i;
            V("gitBranchLabel" + i, () => C(key).Contrast);
            V("gitInv" + i, () => C(key).Invert());
        }

        for (var i = 0; i < 12; i++)
        {
            var key = "cScale" + i;
            V("cScaleInv" + i, () => C(key).Invert());
            V("cScalePeer" + i, () => DarkMode ? C(key).Lighten(10) : C(key).Darken(10));
            V("cScaleLabel" + i, () => C(key).Contrast);
        }

        V("pie1", () => C("primaryColor"));
        V("pie2", () => C("secondaryColor"));
        V("pie3", () => C("tertiaryColor"));
        V("pie4", () => C("primaryColor").Adjust(l: -10));
        V("pie5", () => C("secondaryColor").Adjust(l: -10));
        V("pie6", () => C("tertiaryColor").Adjust(l: -10));
        V("pie7", () => C("primaryColor").Adjust(h: 60, l: -10));
        V("pie8", () => C("primaryColor").Adjust(h: -60, l: -10));
        V("pie9", () => C("primaryColor").Adjust(h: 120));
        V("pie10", () => C("primaryColor").Adjust(h: 60, l: -20));
        V("pie11", () => C("primaryColor").Adjust(h: -60, l: -20));
        V("pie12", () => C("primaryColor").Adjust(h: 120, l: -10));
        V("pieTitleTextSize", "25px");
        V("pieTitleTextColor", () => C("taskTextDarkColor"));
        V("pieSectionTextSize", "17px");
        V("pieSectionTextColor", () => C("textColor"));
        V("pieLegendTextSize", "17px");
        V("pieLegendTextColor", () => C("taskTextDarkColor"));
        V("pieStrokeColor", () => DarkMode ? C("background").Invert().WithAlpha(0.6) : Color.Black);
        V("pieStrokeWidth", "2px");
        V("pieOuterStrokeWidth", "2px");
        V("pieOuterStrokeColor", () => DarkMode ? C("background").Invert().WithAlpha(0.6) : Color.Black);
        V("pieOpacity", "0.7");
        for (var i = 0; i < 8; i++)
        {
            var hue = i * 45;
            V("fillType" + i, () => (i % 2 == 0 ? C("primaryColor") : C("secondaryColor")).Adjust(h: hue));
        }

        V("quadrant1Fill", () => C("primaryColor"));
        V("quadrant2Fill", () => C("primaryColor").Adjust(r: 5, g: 5, b: 5));
        V("quadrant3Fill", () => C("primaryColor").Adjust(r: 10, g: 10, b: 10));
        V("quadrant4Fill", () => C("primaryColor").Adjust(r: 15, g: 15, b: 15));
        V("quadrant1TextFill", () => C("primaryTextColor"));
        V("quadrant2TextFill", () => C("primaryTextColor").Adjust(r: -5, g: -5, b: -5));
        V("quadrant3TextFill", () => C("primaryTextColor").Adjust(r: -10, g: -10, b: -10));
        V("quadrant4TextFill", () => C("primaryTextColor").Adjust(r: -15, g: -15, b: -15));
        V("quadrantPointFill", () => C("quadrant1Fill").IsDark ? C("quadrant1Fill").Lighten(20) : C("quadrant1Fill").Darken(40));
        V("quadrantPointTextFill", () => C("primaryTextColor"));
        V("quadrantXAxisTextFill", () => C("primaryTextColor"));
        V("quadrantYAxisTextFill", () => C("primaryTextColor"));
        V("quadrantInternalBorderStrokeFill", () => C("primaryBorderColor"));
        V("quadrantExternalBorderStrokeFill", () => C("primaryBorderColor"));
        V("quadrantTitleFill", () => C("primaryTextColor"));
        V("xyChart.backgroundColor", () => C("background"));
        V("xyChart.titleColor", () => C("primaryTextColor"));
        V("xyChart.xAxisTitleColor", () => C("primaryTextColor"));
        V("xyChart.xAxisLabelColor", () => C("primaryTextColor"));
        V("xyChart.xAxisTickColor", () => C("primaryTextColor"));
        V("xyChart.xAxisLineColor", () => C("primaryTextColor"));
        V("xyChart.yAxisTitleColor", () => C("primaryTextColor"));
        V("xyChart.yAxisLabelColor", () => C("primaryTextColor"));
        V("xyChart.yAxisTickColor", () => C("primaryTextColor"));
        V("xyChart.yAxisLineColor", () => C("primaryTextColor"));
        V("xyChart.plotColorPalette", DarkMode
            ? "#3498DB,#2ECC71,#E74C3C,#F1C40F,#BDC3C7,#FFFFFF,#34495E,#9B59B6,#1ABC9C,#E67E22"
            : "#ECECFF,#8493A6,#FFC3A0,#DCDDE1,#B8E994,#D1A36F,#C3CDE6,#FFB6C1,#496078,#F8F3E3");
        V("radar.axisColor", () => C("lineColor"));
        V("radar.axisStrokeWidth", "2");
        V("radar.axisLabelFontSize", "12");
        V("radar.curveOpacity", "0.5");
        V("radar.curveStrokeWidth", "2");
        V("radar.graticuleColor", "#DEDEDE");
        V("radar.graticuleStrokeWidth", "1");
        V("radar.graticuleOpacity", "0.3");
        V("radar.legendBoxSize", "12");
        V("radar.legendFontSize", "12");
        V("archEdgeColor", () => C("lineColor"));
        V("archEdgeArrowColor", () => C("lineColor"));
        V("archEdgeWidth", "3");
        V("archGroupBorderColor", () => C("primaryBorderColor"));
        V("archGroupBorderWidth", "2px");
    }
}
