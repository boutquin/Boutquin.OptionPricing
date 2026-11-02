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

using Boutquin.OptionPricing.Domain.Results;
using Boutquin.OptionPricing.Trees.Binomial;

namespace Boutquin.OptionPricing.Trees.Pricing;

/// <summary>
/// Extracts the lattice-derivable Greeks — Delta (Δ), Gamma (Γ), and Theta (Θ) — directly
/// from a valued binomial lattice by finite differences over the nodes already present in
/// the tree, with no bump-and-reprice.
/// </summary>
/// <remarks>
/// <para>
/// <b>Model assumption — finite-difference scheme (Chriss, <i>Black-Scholes and Beyond</i>,
/// Figure 7.8.1 / Display 7.8.1, section 7.8).</b> On a recombining lattice the first two time layers
/// already straddle the root in spot, so the hedge parameters are read as divided differences
/// of the option value across adjacent nodes rather than by re-pricing perturbed inputs:
/// </para>
/// <code>
/// Δ ≈ ( V(1,1) − V(1,0) ) / ( S(1,1) − S(1,0) )
/// Γ ≈ ( (V(2,2)−V(2,1))/(S(2,2)−S(2,1)) − (V(2,1)−V(2,0))/(S(2,1)−S(2,0)) ) / ( (S(2,2)−S(2,0)) / 2 )
/// Θ ≈ ( V(2,1) − V(0,0) ) / ( 2·dt )            (per calendar day: annualized Θ / 365)
/// </code>
/// <para>
/// Indices are <c>(step, up-count)</c>; on this library's top-down lattice orientation the
/// up-count <c>j</c> maps to state index <c>step − j</c> (state 0 is the highest spot). On a
/// <i>spot-centered</i> (CRR-type, <c>u·d = 1</c>) tree the centre node <c>(2,1)</c> shares the
/// root's spot level, which is what makes the centred two-step <c>Θ</c> a forward difference in
/// calendar time. The raw lattice
/// <c>Θ</c> is an <i>annualized</i> derivative; it is divided by 365 so the reported value
/// matches the per-calendar-day convention of <see cref="GreekSet.Theta"/> and of
/// <c>BlackScholesGreeks</c>.
/// </para>
/// <para>
/// <b>Accuracy contract (spot-centered / CRR lattices only).</b> For a European option on a
/// <i>spot-centered</i> (CRR-type, <c>u·d = 1</c>) lattice the lattice <c>Δ</c>, <c>Γ</c>, and
/// <c>Θ</c> converge to the closed-form Black-Scholes Greeks as the step count grows; the
/// tested convergence is within 1% of the analytical
/// values at 400 steps on the tested at-the-money scenarios. At very small step counts the divided differences
/// remain finite (no division by zero, no <c>NaN</c>) because adjacent CRR spot levels are
/// always distinct for <c>u &gt; d</c>. The contract does <b>not</b> hold for non-spot-centered
/// lattices — e.g. Jarrow-Rudd, where <c>u·d = exp(2·drift) ≠ 1</c> places the step-2 centre
/// node at a drifted spot, biasing the two-step <c>Θ</c> difference by a step-independent term.
/// <see cref="Compute"/> therefore rejects such lattices with an <see cref="ArgumentException"/>
/// rather than return a silently mis-priced <c>Θ</c>.
/// </para>
/// <para>
/// <b>Out of scope (left <see langword="null"/>).</b> Vega and Rho are <i>not</i>
/// lattice-extractable from a single valued tree — they are derivatives with respect to
/// volatility and rate, which change the tree geometry itself and therefore require
/// re-pricing. They are returned as <see langword="null"/> in the produced
/// <see cref="GreekSet"/>. (<see cref="GreekSet"/> carries no Lambda member, so no Lambda is
/// reported.)
/// </para>
/// </remarks>
public sealed class TreeGreeksCalculator
{
    /// <summary>
    /// Computes Delta, Gamma, and Theta from a valued binomial lattice using the Display 7.8.1
    /// finite differences. The lattice must be spot-centered (CRR-type, <c>u·d = 1</c>) so the
    /// step-2 centre node sits at the root spot.
    /// </summary>
    /// <param name="valuedLattice">
    /// A lattice paired with its backward-induction option values, as produced by
    /// <see cref="EuropeanTreePricer.PriceWithLattice"/> or
    /// <see cref="AmericanTreePricer.PriceWithLattice"/>.
    /// </param>
    /// <returns>
    /// A <see cref="GreekSet"/> with <see cref="GreekSet.Delta"/>, <see cref="GreekSet.Gamma"/>,
    /// and <see cref="GreekSet.Theta"/> populated (Theta per calendar day); Vega and Rho left
    /// <see langword="null"/> because they require re-pricing.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="valuedLattice"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when the lattice has fewer than two time steps (the Gamma and Theta differences
    /// need the step-2 layer), or when the lattice is not spot-centered (CRR-type, <c>u·d = 1</c>):
    /// a lattice whose step-2 centre node is not at the root spot — e.g. a Jarrow-Rudd lattice —
    /// would yield a biased Theta and is rejected rather than mis-priced.
    /// </exception>
    public GreekSet Compute(LatticeValuationResult valuedLattice)
    {
        ArgumentNullException.ThrowIfNull(valuedLattice);
        valuedLattice.Validate();

        BinomialTree tree = valuedLattice.Tree;
        if (tree.Steps < 2)
        {
            throw new ArgumentException(
                "Lattice Greeks require at least two time steps to read the (2, ·) layer for Gamma and Theta.",
                nameof(valuedLattice));
        }

        IReadOnlyList<IReadOnlyList<double>> spot = tree.SpotLattice;
        IReadOnlyList<IReadOnlyList<double>> value = valuedLattice.ValueLattice;

        // State 0 is the highest spot (most up moves). On a recombining lattice the
        // step-2 layer is ordered [high, centre, low] and the centre node shares the
        // root's spot level — the geometry the Display 7.8.1 differences rely on.
        double s10 = spot[1][0];   // up child   (high)
        double s11 = spot[1][1];   // down child (low)
        double v10 = value[1][0];
        double v11 = value[1][1];

        double s20 = spot[2][0];   // high
        double s21 = spot[2][1];   // centre (== root spot level on a spot-centered lattice)
        double s22 = spot[2][2];   // low
        if (!(s10 > s11) || !(s20 > s21 && s21 > s22))
        {
            throw new ArgumentException("Lattice Greeks require distinct, descending spot nodes.", nameof(valuedLattice));
        }
        double v20 = value[2][0];
        double v21 = value[2][1];
        double v22 = value[2][2];

        double v00 = value[0][0];
        double s00 = spot[0][0];   // root spot level
        double dt = tree.DeltaTime;

        // The centred two-step Θ difference (V(2,centre) − V(0,0))/(2·dt) is a forward
        // difference in calendar time ONLY when the step-2 centre node sits at the root
        // spot. That holds for spot-centered (CRR, u·d = 1) lattices, but NOT for
        // Jarrow-Rudd (u·d = exp(2·drift) ≠ 1), whose centre node is at a drifted spot —
        // there (V(2,centre) − V(0,0)) conflates a 2·dt time move with a spot move and Θ
        // carries a step-independent bias. Reject such lattices loudly rather than return a
        // silently mis-priced Θ.
        const double spotCenteredTolerance = 1e-6d;
        if (Math.Abs(s21 - s00) / s00 > spotCenteredTolerance)
        {
            throw new ArgumentException(
                "Lattice Greeks require a spot-centered (CRR-type, u·d = 1) lattice: the step-2 " +
                "centre node must sit at the root spot for the two-step Theta difference to be a " +
                "pure time derivative. The supplied lattice's centre node is at a drifted spot " +
                "(for example a Jarrow-Rudd lattice, where u·d ≠ 1), which would yield a biased Theta.",
                nameof(valuedLattice));
        }

        // Δ ≈ (V_up − V_down) / (S_up − S_down). Up child is state 0 (higher spot).
        double delta = (v10 - v11) / (s10 - s11);

        // Γ from the two one-sided slopes around the centre node, divided by the average
        // spot spacing (S(2,high) − S(2,low)) / 2.
        double upperSlope = (v20 - v21) / (s20 - s21);
        double lowerSlope = (v21 - v22) / (s21 - s22);
        double gamma = (upperSlope - lowerSlope) / ((s20 - s22) / 2d);

        // Θ ≈ (V(2,centre) − V(0,0)) / (2·dt), annualized, then expressed per calendar day
        // (annualized / 365) to match GreekSet.Theta / BlackScholesGreeks.
        double thetaAnnual = (v21 - v00) / (2d * dt);
        double theta = thetaAnnual / 365d;

        return new GreekSet(Delta: delta, Gamma: gamma, Theta: theta);
    }
}
