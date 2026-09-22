// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.DriverPacks;

/// <summary>Maps extraction progress synchronously so callbacks cannot escape their active deployment step.</summary>
internal sealed class CatalogExtractionProgress(IProgress<double> progress, double start, double end) : IProgress<double>
{
    /// <inheritdoc />
    public void Report(double value)
    {
        double normalized = Math.Clamp(value, 0d, 100d);
        progress.Report(start + (normalized / 100d * (end - start)));
    }
}
