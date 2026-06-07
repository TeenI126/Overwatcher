using System.Drawing;
using OwTracker.Core.Services.Interfaces;
using OwTracker.ML;

namespace OwTracker.Tests;

public class MlTests
{
    [Fact]
    public void HeroRoster_LoadsEmbeddedResource()
    {
        var provider = new HeroRosterProvider();

        var names = provider.GetHeroNames();

        Assert.NotEmpty(names);
        Assert.Contains("Reinhardt", names);
        Assert.Contains("Ana", names);
    }

    [Fact]
    public void StubClassifier_AlwaysReturnsUnknownLowConfidence()
    {
        var classifier = new StubHeroClassifier();
        using var crop = new Bitmap(64, 64);

        var prediction = classifier.Predict(crop);

        Assert.Equal(HeroPrediction.Unknown, prediction.HeroName);
        Assert.Equal(0f, prediction.Confidence);
    }
}
