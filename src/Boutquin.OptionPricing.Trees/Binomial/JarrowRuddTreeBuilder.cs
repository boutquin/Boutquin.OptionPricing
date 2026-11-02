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

namespace Boutquin.OptionPricing.Trees.Binomial;

/// <summary>
/// Builds a recombining binomial tree using the Jarrow-Rudd (equal probability)
/// parameterization — specifically the <b>risk-neutral</b> variant, which embeds the
/// risk-neutral drift <c>(r − q − σ²/2)Δt</c> in the up/down factors so that <c>p = 0.5</c>
/// is the correct risk-neutral probability (no re-weighting required). This differs from
/// the classical Jarrow-Rudd form, which uses the physical drift and is not risk-neutral.
/// </summary>
/// <remarks>
/// <para>
/// The Jarrow-Rudd model sets the risk-neutral probability to exactly 0.5 and adjusts
/// the up and down factors to match the drift and volatility (note <c>d ≠ 1/u</c> here,
/// unlike CRR):
/// <c>u = exp((r − q − σ²/2)Δt + σ√Δt)</c>,
/// <c>d = exp((r − q − σ²/2)Δt − σ√Δt)</c>,
/// <c>p = 0.5</c>.
/// </para>
/// <para>
/// Like CRR, the Jarrow-Rudd tree converges to the Black-Scholes price as the number of
/// time steps increases, with convergence rate O(1/N).
/// </para>
/// </remarks>
public sealed class JarrowRuddTreeBuilder : ITreeBuilder
{
    /// <summary>
    /// Constructs a Jarrow-Rudd binomial tree by computing up/down factors with equal
    /// risk-neutral probability and the full spot lattice from the supplied model inputs.
    /// </summary>
    /// <param name="input">
    /// Market and contract parameters. Spot and volatility must be strictly positive,
    /// time to expiry must be positive, and step count must be between one and
    /// <see cref="TreeBuilderLimits.MaxSteps"/> inclusive.
    /// </param>
    /// <returns>A fully populated <see cref="BinomialTree"/> with Jarrow-Rudd lattice dynamics.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="input"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="input"/> has non-positive spot, non-positive volatility,
    /// non-positive time to expiry, or a step count outside the range
    /// [1, <see cref="TreeBuilderLimits.MaxSteps"/>].
    /// </exception>
    public BinomialTree Build(TreeModelInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (input.Spot <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(input));
        }

        if (!double.IsFinite(input.Volatility) || input.Volatility <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(input));
        }

        double timeToExpiry = input.TimeToExpiryInYears();
        if (!double.IsFinite(timeToExpiry) || timeToExpiry <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(input));
        }

        if (input.Steps <= 0 || input.Steps > TreeBuilderLimits.MaxSteps)
        {
            throw new ArgumentOutOfRangeException(nameof(input));
        }

        double dt = timeToExpiry / input.Steps;
        double r = input.RiskFreeRate();
        double q = input.DividendYield() + input.BorrowRate();
        double drift = (r - q - (0.5d * input.Volatility * input.Volatility)) * dt;
        double volSqrtDt = input.Volatility * Math.Sqrt(dt);

        double u = Math.Exp(drift + volSqrtDt);
        double d = Math.Exp(drift - volSqrtDt);
        const double p = 0.5d;
        if (!double.IsFinite(u) || !double.IsFinite(d) || d <= 0d || u <= d)
        {
            throw new InvalidOperationException("The Jarrow-Rudd factors cannot be represented as finite distinct positive values.");
        }

        List<IReadOnlyList<double>> lattice = new(input.Steps + 1);
        double s0 = (double)input.Spot;

        for (int i = 0; i <= input.Steps; i++)
        {
            List<double> layer = new(i + 1);
            for (int j = 0; j <= i; j++)
            {
                double spot = s0 * Math.Pow(u, i - j) * Math.Pow(d, j);
                layer.Add(spot);
            }

            lattice.Add(layer);
        }

        return new BinomialTree(input.Steps, dt, u, d, p, lattice) { RiskFreeRate = r };
    }
}
