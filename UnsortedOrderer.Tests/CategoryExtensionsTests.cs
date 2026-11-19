using Microsoft.Extensions.DependencyInjection;
using UnsortedOrderer.Application.DependencyInjection;
using UnsortedOrderer.Categories;
using UnsortedOrderer.Contracts.Categories;
using UnsortedOrderer.Models;
using Xunit;

namespace UnsortedOrderer.Tests;

public sealed class CategoryExtensionsTests
{
    [Fact]
    public void Registered_categories_have_only_explicit_extension_overlaps()
    {
        var settings = new AppSettings("source", "sorted", "Soft", "Archives", "Images", "Photos",
            "Music", "Guitar Tabs", "EBooks", "Repositories", "Drivers", "Firmware", "Metadata",
            "Web", "Graphics", "_Unknown", [], [], [], []);
        using var provider = new ServiceCollection().AddUnsortedOrdererApplication(settings).BuildServiceProvider();
        var categories = provider.GetRequiredService<IEnumerable<ICategory>>().ToArray();

        CategoryExtensionPolicy.Validate(categories);

        Assert.IsType<ConfigurationsCategory>(Assert.Single(categories.Where(c => c.Matches(".conf"))));
        var gpx = categories.Where(c => c.Matches(".gpx")).ToArray();
        Assert.Equal(2, gpx.Length);
        Assert.Contains(gpx, c => c is GpsTracksCategory);
        Assert.Contains(gpx, c => c is MusicalInstrumentsCategory);
    }

    [Fact]
    public void Accidental_overlap_is_rejected_ignoring_case_and_leading_dot()
    {
        ICategory[] categories = [new TestCategory("One", "CONF"), new TestCategory("Two", ".conf")];

        var error = Assert.Throws<InvalidOperationException>(() => CategoryExtensionPolicy.Validate(categories));

        Assert.Contains(".conf", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("One, Two", error.Message);
    }

    [Theory]
    [InlineData(".gpx")]
    [InlineData(".jpg")]
    public void Exception_does_not_allow_a_third_category(string extension)
    {
        ICategory[] categories = [new GpsTracksCategory(), new MusicalInstrumentsCategory("Guitar Tabs"),
            new ImagesCategory("Images"), new PhotosCategory("Photos"), new TestCategory("Third", extension)];
        var error = Assert.Throws<InvalidOperationException>(() => CategoryExtensionPolicy.Validate(categories));

        Assert.Contains(extension, error.Message);
        Assert.Contains("Third", error.Message);
    }

    private sealed class TestCategory(string name, string extension) : FileCategory(name, name, [extension]);
}
