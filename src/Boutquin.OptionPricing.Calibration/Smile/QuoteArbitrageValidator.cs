// Copyright (c) 2026 Pierre G. Boutquin. All rights reserved.
//
//   Licensed under the Apache License, Version 2.0 (the "License").
//   You may not use this file except in compliance with the License.
//   You may obtain a copy of the License at
//
//       http://www.apache.org/licenses/LICENSE-2.0
//
//   Unless required by applicable law or agreed to in writing, software
//   distributed under the License is distributed on an "AS IS" BASIS,
//   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//
//   See the License for the specific language governing permissions and
//   limitations under the License.
//

namespace Boutquin.OptionPricing.Calibration.Smile;

/// <summary>
/// Validates smile slices and volatility surfaces against no-arbitrage constraints on
/// implied volatility quotes: positivity, butterfly spread (intra-slice convexity),
/// and calendar spread (inter-slice total variance monotonicity).
/// </summary>
public sealed class QuoteArbitrageValidator
{
    /// <summary>
    /// Scans a single smile slice for positivity and butterfly spread arbitrage violations.
    /// </summary>
    /// <param name="slice">The smile slice whose implied volatility quotes are being validated.</param>
    /// <returns>
    /// An <see cref="ArbitrageCheckResult"/> that is valid when all quotes pass, or contains
    /// one finding per detected violation.
    /// </returns>
    public ArbitrageCheckResult Validate(SmileSlice slice)
    {
        List<string> findings = new();

        foreach (SurfacePoint point in slice.Points)
        {
            if (point.ImpliedVolatility <= 0d)
            {
                findings.Add($"Non-positive implied volatility at strike {point.Strike}.");
            }
        }

        // Butterfly spread: for consecutive triplets, middle vol must not exceed
        // linear interpolation of neighbors (convexity constraint on call prices)
        IReadOnlyList<SurfacePoint> points = slice.Points;
        for (int i = 1; i < points.Count - 1; i++)
        {
            decimal k1 = points[i - 1].Strike;
            decimal k2 = points[i].Strike;
            decimal k3 = points[i + 1].Strike;

            if (k3 == k1)
            {
                continue;
            }

            double weight = (double)(k2 - k1) / (double)(k3 - k1);
            double interpolatedVol = points[i - 1].ImpliedVolatility + (weight * (points[i + 1].ImpliedVolatility - points[i - 1].ImpliedVolatility));

            if (points[i].ImpliedVolatility > interpolatedVol + 1e-10d)
            {
                findings.Add($"Butterfly spread arbitrage at strikes {k1}/{k2}/{k3}.");
            }
        }

        return new ArbitrageCheckResult(findings.Count == 0, findings);
    }

    /// <summary>
    /// Validates a complete volatility surface for intra-slice (butterfly) and inter-slice
    /// (calendar spread) arbitrage violations.
    /// </summary>
    /// <param name="surface">The volatility surface to validate.</param>
    /// <returns>
    /// An <see cref="ArbitrageCheckResult"/> combining findings from all slices and cross-maturity checks.
    /// Calendar spread violation occurs when total variance decreases with maturity at any strike.
    /// </returns>
    public ArbitrageCheckResult Validate(VolatilitySurface surface)
    {
        List<string> findings = new();

        // Intra-slice checks (positivity + butterfly)
        foreach (SmileSlice slice in surface.Slices)
        {
            ArbitrageCheckResult sliceResult = Validate(slice);
            findings.AddRange(sliceResult.Findings);
        }

        // Calendar spread: total variance must be non-decreasing in maturity
        // σ²(T2) * T2 ≥ σ²(T1) * T1 for T2 > T1
        for (int s = 1; s < surface.Slices.Count; s++)
        {
            SmileSlice earlier = surface.Slices[s - 1];
            SmileSlice later = surface.Slices[s];

            foreach (SurfacePoint laterPoint in later.Points)
            {
                // Find matching strike in earlier slice
                SurfacePoint? earlierPoint = FindByStrike(earlier.Points, laterPoint.Strike);
                if (earlierPoint is null)
                {
                    continue;
                }

                double totalVarEarlier = earlierPoint.ImpliedVolatility * earlierPoint.ImpliedVolatility * earlier.MaturityInYears;
                double totalVarLater = laterPoint.ImpliedVolatility * laterPoint.ImpliedVolatility * later.MaturityInYears;

                if (totalVarLater < totalVarEarlier - 1e-10d)
                {
                    findings.Add($"Calendar spread arbitrage at strike {laterPoint.Strike} between maturities {earlier.MaturityInYears} and {later.MaturityInYears}.");
                }
            }
        }

        return new ArbitrageCheckResult(findings.Count == 0, findings);
    }

    private static SurfacePoint? FindByStrike(IReadOnlyList<SurfacePoint> points, decimal strike)
    {
        foreach (SurfacePoint point in points)
        {
            if (point.Strike == strike)
            {
                return point;
            }
        }

        return null;
    }
}
