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

using Boutquin.MarketData.DayCount;
using Boutquin.Numerics.Interpolation;

namespace Boutquin.OptionPricing.Calibration.Smile;

/// <summary>
/// A complete volatility surface composed of maturity-ordered smile slices.
/// Each slice represents one horizontal cross-section of the surface at a fixed expiry,
/// and together the slices span the full term structure of implied volatility.
/// Provides bivariate interpolation via <see cref="GetVol(decimal, double)"/> with
/// flat extrapolation beyond the grid boundary.
/// </summary>
/// <param name="Slices">Maturity-ordered collection of smile slices that define the surface.</param>
/// <remarks>
/// <para>
/// <b>Strike axis:</b> Implied volatility is interpolated across strikes within a slice
/// using <see cref="MonotoneCubicInterpolator{T}"/> (Fritsch-Carlson with the
/// arXiv:2402.01324 endpoint refinement — O(h²) boundary error, monotonicity-preserving,
/// no spurious oscillation). Adopted under Phase 5b of the Numerics migration to
/// reduce wing oscillation at off-grid strikes. Interpolating volatilities does not
/// enforce convex call prices or guarantee an arbitrage-free surface. With exactly two strikes, the
/// monotone-cubic Hermite reduces algebraically to linear interpolation, so two-point
/// slices behave identically to the prior linear implementation.
/// </para>
/// <para>
/// <b>Maturity axis:</b> Across slices the surface uses an adaptive cubic-spline scheme
/// from <see cref="CubicSplineInterpolator{T}"/>:
/// </para>
/// <list type="bullet">
///   <item>≥4 slices — <see cref="SplineBoundary.ModifiedNotAKnot"/> (Jarre 2025,
///         arXiv:2507.05083) — sharper endpoint behavior than classical not-a-knot for
///         vol-vs-time, where ATM term structures are typically smooth but exhibit
///         curvature near the short and long ends.</item>
///   <item>3 slices — <see cref="SplineBoundary.Natural"/> — the next-best C² option
///         when too few nodes exist for the not-a-knot family.</item>
///   <item>2 slices — two-point linear (the cubic boundaries require 3+ nodes).</item>
/// </list>
/// <para>
/// Extrapolation in both axes clamps to the boundary slice or boundary strike (flat).
/// The maturity-axis cubic spline natively extrapolates along the nearest segment, but
/// callers see the clamped behavior because the surface short-circuits the spline build
/// when the queried maturity lies outside the slice range.
/// </para>
/// </remarks>
public sealed record VolatilitySurface(
    IReadOnlyList<SmileSlice> Slices)
{
    /// <summary>
    /// Maturity-ordered collection of smile slices that define the surface.
    /// This field initializer validates on the primary-constructor path only — must be
    /// non-empty and strictly maturity-ascending so that GetVol's clamps and cubic-spline
    /// interpolation are correct (F-004). The <see cref="Slices"/> property's <c>init</c>
    /// accessor (not this initializer) is what covers the <see langword="with"/>-expression
    /// path; see that property's documentation.
    /// </summary>
    private readonly IReadOnlyList<SmileSlice> _slices = ValidateSlices(Slices, nameof(Slices));

    /// <summary>
    /// Maturity-ordered collection of smile slices that define the surface.
    /// Validated on every construction path — both <see langword="new"/> and
    /// <see langword="with"/> — to be non-empty and strictly maturity-ascending so that
    /// <see cref="GetVol(decimal, double)"/>'s boundary clamps and cubic-spline
    /// interpolation read the correct slice (F-004). The validation lives in the
    /// <c>init</c> accessor body (not merely a property initializer) precisely because a
    /// positional-record <c>with</c> expression invokes the init setter rather than the
    /// primary constructor — an initializer-only check would leave the <c>with</c> path
    /// unguarded.
    /// </summary>
    public IReadOnlyList<SmileSlice> Slices
    {
        get => _slices;
        init => _slices = ValidateSlices(value, nameof(Slices));
    }

    private static IReadOnlyList<SmileSlice> ValidateSlices(
        IReadOnlyList<SmileSlice> value, string paramName)
    {
        ArgumentNullException.ThrowIfNull(value, paramName);

        if (value.Count == 0)
        {
            throw new ArgumentException(
                "VolatilitySurface requires at least one SmileSlice.",
                paramName);
        }

        if (value.Any(slice => slice is null))
        {
            throw new ArgumentException("Slices must not contain null entries.", paramName);
        }
        for (var i = 1; i < value.Count; i++)
        {
            if (value[i].MaturityInYears <= value[i - 1].MaturityInYears)
            {
                throw new ArgumentException(
                    $"Slices must be in strictly ascending maturity order. " +
                    $"Found Slices[{i - 1}].MaturityInYears = {value[i - 1].MaturityInYears} " +
                    $">= Slices[{i}].MaturityInYears = {value[i].MaturityInYears}.",
                    paramName);
            }
        }

        foreach (SmileSlice slice in value)
        {
            if (slice is null || !double.IsFinite(slice.MaturityInYears) || slice.MaturityInYears <= 0d || slice.Points.Count == 0)
            {
                throw new ArgumentException("Slices require positive finite maturities and at least one quote.", paramName);
            }
            for (int i = 0; i < slice.Points.Count; i++)
            {
                SurfacePoint point = slice.Points[i];
                if (point is null || point.Strike <= 0m || !double.IsFinite(point.ImpliedVolatility) || point.ImpliedVolatility <= 0d
                    || !double.IsFinite(point.MaturityInYears) || Math.Abs(point.MaturityInYears - slice.MaturityInYears) >= 1e-9d
                    || (i > 0 && (double)point.Strike <= (double)slice.Points[i - 1].Strike))
                {
                    throw new ArgumentException("Quotes require positive finite volatility, the slice maturity, and strictly increasing representable positive strikes.", paramName);
                }
            }
        }
        return Array.AsReadOnly(value.ToArray());
    }

    /// <summary>
    /// Returns the implied volatility at the specified strike and maturity. Strike
    /// interpolation uses monotone-cubic Hermite (arXiv:2402.01324 endpoint fix);
    /// maturity interpolation uses an adaptive cubic spline (ModifiedNotAKnot, Natural,
    /// or two-point linear depending on slice count). Uses flat extrapolation when the
    /// query falls outside the strike or maturity range.
    /// </summary>
    /// <param name="strike">The option strike to query.</param>
    /// <param name="maturityInYears">The time-to-expiry in year fractions.</param>
    /// <returns>The interpolated (or extrapolated) implied volatility.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the surface contains no slices.</exception>
    public double GetVol(decimal strike, double maturityInYears)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(strike);
        if (!double.IsFinite(maturityInYears) || maturityInYears < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(maturityInYears), "Query maturity must be finite and non-negative.");
        }
        if (Slices.Count == 0)
        {
            throw new InvalidOperationException("Cannot query an empty volatility surface.");
        }

        // Single slice: interpolate strike only, ignore maturity
        if (Slices.Count == 1)
        {
            return InterpolateStrike(Slices[0], strike);
        }

        // Flat extrapolation outside the maturity grid
        if (maturityInYears <= Slices[0].MaturityInYears)
        {
            return InterpolateStrike(Slices[0], strike);
        }

        if (maturityInYears >= Slices[^1].MaturityInYears)
        {
            return InterpolateStrike(Slices[^1], strike);
        }

        // Two-slice surfaces stay on the original two-point linear path because the
        // cubic-spline boundary conditions require at least 3 nodes.
        if (Slices.Count == 2)
        {
            double v0 = InterpolateStrike(Slices[0], strike);
            double v1 = InterpolateStrike(Slices[1], strike);
            return TwoPointLinearInterpolator<double>.Interpolate(
                Slices[0].MaturityInYears, v0,
                Slices[1].MaturityInYears, v1,
                maturityInYears);
        }

        // Three or more slices — build a cubic spline across the full term structure
        // of σ(strike, T_i). Boundary scheme picked adaptively (see remarks on
        // VolatilitySurface). Spline construction is O(n) and evaluation is O(log n);
        // we rebuild per call because the σ values depend on the queried strike.
        var n = Slices.Count;
        var maturities = new double[n];
        var vols = new double[n];
        for (var i = 0; i < n; i++)
        {
            maturities[i] = Slices[i].MaturityInYears;
            vols[i] = InterpolateStrike(Slices[i], strike);
        }

        SplineBoundary boundary = n >= 4
            ? SplineBoundary.ModifiedNotAKnot
            : SplineBoundary.Natural;

        var spline = new CubicSplineInterpolator<double>(maturities, vols, boundary);
        double volatility = spline.Interpolate(maturityInYears);
        if (!double.IsFinite(volatility) || volatility <= 0d)
        {
            throw new InvalidOperationException("Maturity interpolation produced a non-positive or non-finite volatility; use a better-resolved term grid.");
        }
        return volatility;
    }

    /// <summary>
    /// Dated-query overload of <see cref="GetVol(decimal, double)"/>: derives the
    /// maturity year fraction from the supplied valuation date, expiry date, and
    /// day-count convention, then delegates to the year-fraction-based query.
    /// </summary>
    /// <param name="strike">The option strike to query.</param>
    /// <param name="valuationDate">The valuation date (surface as-of date).</param>
    /// <param name="expiryDate">The option expiry date.</param>
    /// <param name="dayCount">
    /// Day-count convention used to convert the (valuationDate, expiryDate) pair to a
    /// year fraction. Typically obtained from <c>IDiscountCurve.DayCount</c> so that
    /// the vol-surface read shares the exact time axis used by the discount stack.
    /// </param>
    /// <returns>The interpolated implied volatility at the requested coordinate.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="dayCount"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// Phase 5c — this is the Curves-layer-aligned entry point for surface reads.
    /// Callers that already hold dated contract state (expiry date and a discount curve
    /// with a day-count) should prefer this overload so that every year fraction the
    /// pricing stack consumes flows through a single <see cref="IYearFractionCalculator"/>.
    /// When <paramref name="expiryDate"/> is at or before <paramref name="valuationDate"/>,
    /// the maturity clamps to zero and the query returns the front-slice implied volatility.
    /// </remarks>
    public double GetVol(decimal strike, DateOnly valuationDate, DateOnly expiryDate, IYearFractionCalculator dayCount)
    {
        ArgumentNullException.ThrowIfNull(dayCount);

        double maturityInYears = expiryDate <= valuationDate
            ? 0d
            : dayCount.YearFraction(valuationDate, expiryDate);
        return GetVol(strike, maturityInYears);
    }

    private static double InterpolateStrike(SmileSlice slice, decimal strike)
    {
        IReadOnlyList<SurfacePoint> points = slice.Points;

        if (points.Count == 0)
        {
            throw new InvalidOperationException("Cannot interpolate an empty smile slice.");
        }

        if (points.Count == 1)
        {
            return points[0].ImpliedVolatility;
        }

        // MonotoneCubicInterpolator already implements flat extrapolation beyond the
        // node range (arXiv:2402.01324), so the boundary cases collapse into the same
        // Interpolate call as the interior.
        var n = points.Count;
        var xs = new double[n];
        var ys = new double[n];
        for (var i = 0; i < n; i++)
        {
            xs[i] = (double)points[i].Strike;
            ys[i] = points[i].ImpliedVolatility;
        }

        return MonotoneCubicInterpolator<double>.Instance.Interpolate((double)strike, xs, ys);
    }
}
