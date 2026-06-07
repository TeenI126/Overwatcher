using System.Drawing;
using OwTracker.Core.Services.Interfaces;

namespace OwTracker.ML;

/// <summary>
/// Placeholder classifier used until a trained ONNX model and seed crops exist. Always returns
/// <see cref="HeroPrediction.None"/> (Unknown, 0 confidence) so every crop falls below the
/// review threshold and routes into the manual review queue (design §6.6). Swapped for a real
/// ONNX-backed classifier in a later milestone.
/// </summary>
public sealed class StubHeroClassifier : IHeroClassifier
{
    public HeroPrediction Predict(Bitmap crop)
    {
        ArgumentNullException.ThrowIfNull(crop);
        return HeroPrediction.None;
    }
}
