namespace Momoka.Shell;

/// <summary>Probe-only snapshot of the actual image generation and layout owner; no rendering policy.</summary>
internal readonly record struct GaussianProbeReading(int Radius, int Generation, bool RequestedGaussian,
    bool Rendering, double PageWidth, bool Compact, bool Showcase, bool Ready, bool Applied)
{
    public override string ToString() => $"radius={Radius}, generation={Generation}, requested={RequestedGaussian}, "
        + $"rendering={Rendering}, width={PageWidth:0.###}, compact={Compact}, showcase={Showcase}, ready={Ready}, applied={Applied}";
}
