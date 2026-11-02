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
/// Validates smile slices and volatility surfaces against arbitrage heuristics on implied
/// volatility quotes: positivity, an intra-slice butterfly heuristic (convexity in VOL
/// space — a fast smile-shape check, not the rigorous call-price butterfly condition), and
/// an inter-slice calendar check (total-variance monotonicity at co-located strikes).
/// </summary>
/// <remarks>
/// The calendar check compares only strikes present in both adjacent slices (matched by
/// exact value); arbitrage at strikes unique to one slice is not detected. Vertical-spread
/// no-arbitrage is not checked. These limitations follow from the validator operating on raw
/// vol quotes without spot/rate data to convert to prices.
/// </remarks>
public sealed class QuoteArbitrageValidator
{
    /// <summary>
    /// Checks a raw smile slice for valid coordinates, positive finite volatilities, and the vol-space convexity heuristic.
    /// </summary>
    /// <param name="slice">The smile slice whose implied volatility quotes are being validated.</param>
    /// <returns>
    /// An <see cref="ArbitrageCheckResult"/> that is valid when all quotes pass, or contains
    /// one finding per detected violation.
    /// </returns>
    public ArbitrageCheckResult Validate(SmileSlice slice)
    {
        List<string> findings = new();

        ArgumentNullException.ThrowIfNull(slice);
        if (!double.IsFinite(slice.MaturityInYears) || slice.MaturityInYears <= 0d || slice.Points.Count == 0)
        {
            findings.Add("A slice requires a positive finite maturity and at least one quote.");
        }
        for (int i = 0; i < slice.Points.Count; i++)
        {
            SurfacePoint point = slice.Points[i];
            if (point is null || point.Strike <= 0m || !double.IsFinite(point.MaturityInYears)
                || Math.Abs(point.MaturityInYears - slice.MaturityInYears) >= 1e-9d
                || (i > 0 && point.Strike <= slice.Points[i - 1].Strike))
            {
                findings.Add("Quotes require matching maturities and strictly increasing positive strikes.");
                return new ArbitrageCheckResult(false, findings);
            }
        }
        foreach (SurfacePoint point in slice.Points)
        {
            double totalStdDev = point.ImpliedVolatility * Math.Sqrt(slice.MaturityInYears);
            if (!double.IsFinite(point.ImpliedVolatility) || point.ImpliedVolatility <= 0d
                || !double.IsFinite(totalStdDev) || totalStdDev > Math.Sqrt(double.MaxValue))
            {
                findings.Add($"Non-positive, non-finite or non-representable implied volatility at strike {point.Strike}.");
            }
        }

        if (findings.Count > 0)
        {
            return new ArbitrageCheckResult(false, findings);
        }

        // Butterfly heuristic: for consecutive strike triplets, the middle implied vol
        // must not exceed the linear interpolation of its neighbours' vols (convexity in
        // VOL space). This is a fast smile-shape check, not the rigorous no-arbitrage
        // butterfly condition (convexity of the CALL PRICE in strike), which would require
        // spot and rates to convert vols to prices — data this slice-level check does not carry.
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
    /// Checks the surface using the slice vol-space heuristic and co-located-strike total-variance monotonicity.
    /// </summary>
    /// <param name="surface">The volatility surface to validate.</param>
    /// <returns>
    /// An <see cref="ArbitrageCheckResult"/> combining findings from all slices and cross-maturity checks.
    /// The calendar heuristic only compares strikes present in both adjacent slices. No rigorous call-price no-arbitrage guarantee is made.
    /// </returns>
    public ArbitrageCheckResult Validate(VolatilitySurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
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

                if (!double.IsFinite(totalVarEarlier) || !double.IsFinite(totalVarLater))
                {
                    findings.Add($"Non-representable total variance at strike {laterPoint.Strike}.");
                    continue;
                }
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
