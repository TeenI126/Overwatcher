using System.Drawing;

namespace OwTracker.Core.Services.Interfaces;

/// <summary>Result of classifying a hero portrait crop.</summary>
public readonly record struct HeroPrediction(string HeroName, float Confidence)
{
    public const string Unknown = "Unknown";

    public static HeroPrediction None => new(Unknown, 0f);
}

/// <summary>
/// Identifies a hero from a portrait crop (Teams-tab ending-hero icons).
/// Real ONNX inference is deferred; <c>StubHeroClassifier</c> implements this for now.
/// </summary>
public interface IHeroClassifier
{
    HeroPrediction Predict(Bitmap crop);
}
