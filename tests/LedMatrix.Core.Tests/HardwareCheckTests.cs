using LedMatrix.Core;

namespace LedMatrix.Core.Tests;

/// <summary>The "not a Framework Laptop 16" banner rules.</summary>
public class HardwareCheckTests
{
    [Theory]
    [InlineData("Framework", "Laptop 16 (AMD Ryzen 7040 Series)", "Laptop")]
    [InlineData("Framework", "Laptop 16 (AMD Ryzen AI 300 Series)", null)]
    [InlineData("  framework  ", "  LAPTOP 16 (AMD Ryzen 7040 Series)  ", null)]
    [InlineData("Framework Computer Inc.", "Laptop 16", null)]
    [InlineData("Framework", "Laptop16", null)]
    [InlineData("Framework", null, "Laptop 16 (AMD Ryzen 7040 Series)")]
    [InlineData("Framework", "To Be Filled By O.E.M.", "Laptop 16")]
    public void Framework_laptop_16_is_recognised(string manufacturer, string? product, string? family) =>
        Assert.Equal(HardwareMatch.Framework16, HardwareCheck.Classify(manufacturer, product, family));

    [Theory]
    [InlineData("Framework", "Laptop 13 (AMD Ryzen 7040 Series)", "Laptop")]
    [InlineData("Framework", "Laptop (12th Gen Intel Core)", "Laptop")]
    [InlineData("Framework", "Laptop (11th Gen Intel Core)", null)]
    [InlineData("Framework", "Laptop 12 (13th Gen Intel Core)", null)]
    [InlineData("Framework", "Desktop (AMD Ryzen AI Max 300 Series)", "Desktop")]
    [InlineData("Framework", "Laptop 160", null)]
    [InlineData("Framework", "Laptop 1616", "Laptop 160")]
    public void Other_framework_models_are_recognised(string manufacturer, string? product, string? family) =>
        Assert.Equal(HardwareMatch.OtherFramework, HardwareCheck.Classify(manufacturer, product, family));

    [Theory]
    [InlineData("Dell Inc.", "XPS 16 9640", "XPS")]
    [InlineData("LENOVO", "21K5CTO1WW", "ThinkPad P16 Gen 2")]
    [InlineData("Microsoft Corporation", "Surface Laptop 7", "Surface")]
    [InlineData("HP", "HP Laptop 16-ab0000", null)] // "Laptop 16" alone doesn't make it a Framework
    [InlineData("Frameworks Ltd", "Laptop 16", null)] // whole word only
    [InlineData("FrameworkComputer", "Laptop 16", null)]
    public void Other_manufacturers_are_not_framework(string manufacturer, string? product, string? family) =>
        Assert.Equal(HardwareMatch.NotFramework, HardwareCheck.Classify(manufacturer, product, family));

    [Theory]
    [InlineData(null, null, null)]
    [InlineData("", "Laptop 13", null)]
    [InlineData("   ", "XPS 16", null)]
    [InlineData("To Be Filled By O.E.M.", "To Be Filled By O.E.M.", "To Be Filled By O.E.M.")]
    [InlineData("to be filled by o.e.m.", "B650 AORUS ELITE", null)]
    [InlineData("System manufacturer", "System Product Name", "To be filled by O.E.M.")]
    [InlineData("Default string", "Default string", "Default string")]
    [InlineData("Framework", null, null)] // a Framework, but which one?
    [InlineData("Framework", "  ", "Laptop")]
    [InlineData("Framework", "Default string", null)]
    public void Missing_or_placeholder_values_are_unknown(string? manufacturer, string? product, string? family) =>
        Assert.Equal(HardwareMatch.Unknown, HardwareCheck.Classify(manufacturer, product, family));

    [Fact]
    public void Only_other_pcs_get_a_banner()
    {
        Assert.Null(HardwareCheck.BannerMessage(HardwareMatch.Framework16));
        Assert.Null(HardwareCheck.BannerMessage(HardwareMatch.Unknown));
        Assert.Equal(
            "Looks like you don't have a Framework... Why not buy one and support a great company?",
            HardwareCheck.BannerMessage(HardwareMatch.NotFramework));
        Assert.Equal(
            "Hey there! You have a Framework, but not the one this is meant for... Why not upgrade ;)",
            HardwareCheck.BannerMessage(HardwareMatch.OtherFramework));
    }
}
