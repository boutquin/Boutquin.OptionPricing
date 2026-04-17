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
/// Builds a recombining binomial tree using the Cox-Ross-Rubinstein (CRR) parameterization.
/// </summary>
/// <remarks>
/// <para>
/// CRR up and down factors are symmetric in log-space:
/// <c>u = exp(sigma * sqrt(dt))</c>, <c>d = 1 / u</c>.
/// </para>
/// <para>
/// The risk-neutral probability is derived from the no-arbitrage growth factor:
/// <c>p = (exp((r - q) * dt) - d) / (u - d)</c>,
/// where <c>r</c> is the risk-free rate and <c>q</c> is the net carry cost
/// (dividend yield minus borrow rate).
/// </para>
/// <para>
/// The resulting tree converges to the Black-Scholes price as the number of
/// time steps increases, with convergence rate O(1/N).
/// </para>
/// </remarks>
public sealed class CoxRossRubinsteinTreeBuilder : ITreeBuilder
{
    /// <summary>
    /// Constructs a CRR binomial tree by computing up/down factors, risk-neutral
    /// probability, and the full spot lattice from the supplied model inputs.
    /// </summary>
    /// <param name="input">
    /// Market and contract parameters. Spot and volatility must be strictly positive,
    /// time to expiry must be positive, and step count must be at least one.
    /// </param>
    /// <returns>A fully populated <see cref="BinomialTree"/> with CRR lattice dynamics.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="input"/> has non-positive spot, non-positive volatility,
    /// non-positive time to expiry, or non-positive step count.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the derived risk-neutral probability falls outside the open interval (0, 1),
    /// indicating that the no-arbitrage condition is violated for the given inputs.
    /// </exception>
    public BinomialTree Build(TreeModelInput input)
    {
        if (input.Spot <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(input));
        }

        if (input.Volatility <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(input));
        }

        double timeToExpiry = input.TimeToExpiryInYears();
        if (timeToExpiry <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(input));
        }

        if (input.Steps <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(input));
        }

        double dt = timeToExpiry / input.Steps;
        double r = input.RiskFreeRate();
        double q = input.DividendYield() - input.BorrowRate();
        double u = Math.Exp(input.Volatility * Math.Sqrt(dt));
        double d = 1d / u;
        double growth = Math.Exp((r - q) * dt);
        double p = (growth - d) / (u - d);

        if (p <= 0d || p >= 1d)
        {
            throw new InvalidOperationException("Risk-neutral probability must lie strictly between zero and one.");
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

        return new BinomialTree(input.Steps, dt, u, d, p, lattice);
    }
}
