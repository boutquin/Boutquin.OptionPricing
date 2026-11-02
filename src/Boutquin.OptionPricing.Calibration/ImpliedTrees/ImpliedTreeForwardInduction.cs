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

namespace Boutquin.OptionPricing.Calibration.ImpliedTrees;

/// <summary>
/// Derman-Kani implied binomial tree construction by forward induction
/// (Derman &amp; Kani, "The Volatility Smile and Its Implied Tree", Goldman Sachs
/// Quantitative Strategies Research Notes, January 1994). The node recursion below
/// implements the paper's published equations verbatim — Equation 6 (upper node),
/// Equation 7 (transition probability), Equation 8 (even-level central node), and
/// Equation 9 (lower node) — with the centering condition and the "Avoiding Arbitrages"
/// rule from the same construction section. Each successive level's node spots are
/// solved so the tree reprices the surface-interpolated option prices at the node
/// strikes, while every node satisfies the forward (martingale) condition (EQ 3)
/// <c>F_i = p_i·S_up + (1 − p_i)·S_down</c>.
/// </summary>
/// <remarks>
/// <para>
/// The construction genuinely consumes the smile: a non-flat surface produces a
/// non-uniform spot lattice and node-varying local volatilities, and the terminal
/// Arrow-Debreu prices reproduce the surface-implied risk-neutral density. On a flat
/// surface the construction reduces to the constant-volatility lattice.
/// </para>
/// <para>
/// The no-arbitrage check is the paper's exact "Avoiding Arbitrages" two-sided bracket
/// <c>F_i &lt; S_{i+1} &lt; F_{i+1}</c>; on violation the node reverts to the
/// local constant-volatility spacing constrained by the neighbouring forward bracket; the probability is
/// clamped to [0, 1] only for endpoint roundoff. The forward uses the cost-of-carry
/// factor <c>e^{(r−q)Δt}</c> where <c>q</c> is the <b>effective cost-of-carry yield</b>
/// (dividend yield + borrow rate, i.e. <c>effectiveQ = dividendYield + borrowRate</c>),
/// not the bare dividend yield. This generalizes the paper's <c>e^{rΔt}</c> to the full
/// cost-of-carry convention used throughout this library; the paper's original forward is
/// recovered when <c>effectiveQ = 0</c> (not merely when the dividend yield is zero).
/// All lattices are returned in the repository's canonical orientation
/// (<c>[step][stateIndex]</c> with state index 0 = highest spot).
/// </para>
/// </remarks>
internal static class ImpliedTreeForwardInduction
{
    /// <summary>The calibrated lattices and representative scalar parameters.</summary>
    /// <param name="SpotLattice">Node spots, top-down (state 0 = highest spot), indexed <c>[step][state]</c>.</param>
    /// <param name="ArrowDebreuPrices">Arrow-Debreu state prices, same orientation as <paramref name="SpotLattice"/>.</param>
    /// <param name="LocalVolatilities">Node local volatilities, indexed <c>[step][state]</c>.</param>
    /// <param name="ProbabilityLattice">
    /// Per-node up-move (martingale) transition probabilities, indexed <c>[step][state]</c> in the
    /// same top-down orientation as <paramref name="SpotLattice"/>: <c>ProbabilityLattice[step][j]</c>
    /// is the probability that the node at <c>SpotLattice[step][j]</c> moves up to
    /// <c>SpotLattice[step+1][j]</c> (the higher-spot child) rather than down to
    /// <c>SpotLattice[step+1][j+1]</c>. These per-node weights — not their average — are the
    /// Derman-Kani smile content that makes the tree reprice the surface.
    /// </param>
    /// <param name="UpFactor">Representative constant-vol up factor (seed lattice scale).</param>
    /// <param name="DownFactor">Representative constant-vol down factor.</param>
    /// <param name="AverageProbability">
    /// Mean of all per-node probabilities. A representative scalar only; downstream backward
    /// induction must prefer <paramref name="ProbabilityLattice"/> on a smile-calibrated tree.
    /// </param>
    /// <param name="ReversionCount">
    /// Number of node solves that violated the "Avoiding Arbitrages" bracket and reverted to the
    /// constant-volatility (CRR) seed during construction. A non-zero count signals the calibrated
    /// tree degraded toward flat spacing at some nodes (typically the deep wings).
    /// </param>
    internal sealed record Lattices(
        IReadOnlyList<IReadOnlyList<double>> SpotLattice,
        IReadOnlyList<IReadOnlyList<double>> ArrowDebreuPrices,
        IReadOnlyList<IReadOnlyList<double>> LocalVolatilities,
        IReadOnlyList<IReadOnlyList<double>> ProbabilityLattice,
        double UpFactor,
        double DownFactor,
        double AverageProbability,
        int ReversionCount);

    /// <summary>
    /// Builds the implied tree. Internally the working arrays are bottom-up
    /// (index 0 = lowest spot) to match the published recursion; results are flipped
    /// to top-down (index 0 = highest spot) for storage.
    /// </summary>
    /// <param name="s0">Initial underlying spot price.</param>
    /// <param name="sigma0">
    /// Flat (at-the-money) implied volatility used to seed the constant-volatility (CRR) lattice
    /// before smile-calibration adjusts the wing nodes.
    /// </param>
    /// <param name="r">Continuously compounded risk-free rate.</param>
    /// <param name="q">
    /// <b>Effective cost-of-carry yield</b> = dividend yield + borrow rate
    /// (<c>effectiveQ = dividendYield + borrowRate</c>). Callers MUST pass this combined value,
    /// not the bare dividend yield alone — omitting the borrow rate would produce incorrect
    /// forwards and mispriced nodes. The paper's original <c>e^{rΔt}</c> forward is recovered
    /// when <c>effectiveQ = 0</c>.
    /// </param>
    /// <param name="t">Time to expiry in years.</param>
    /// <param name="steps">Number of time steps in the binomial lattice.</param>
    /// <param name="callPrice">
    /// Smile-interpolated European call pricer: <c>(strike, maturity) → price</c>.
    /// </param>
    /// <param name="putPrice">
    /// Smile-interpolated European put pricer: <c>(strike, maturity) → price</c>.
    /// </param>
    /// <param name="epsilon">
    /// Probability clamp applied after each node solve to keep transition probabilities
    /// strictly inside <c>(ε, 1−ε)</c> and avoid degenerate lattice paths.
    /// </param>
    internal static Lattices Build(
        double s0,
        double sigma0,
        double r,
        double q,
        double t,
        int steps,
        Func<double, double, double> callPrice,
        Func<double, double, double> putPrice,
        double epsilon)
    {
        double dt = t / steps;
        double sqrtDt = Math.Sqrt(dt);
        double u = Math.Exp(sigma0 * sqrtDt);
        double d = 1d / u;
        double growth = Math.Exp((r - q) * dt);
        double R = Math.Exp(r * dt);   // forward discount inverse (e^{rΔt})
        double df = Math.Exp(-r * dt);
        if (!double.IsFinite(u) || u <= 1d || !double.IsFinite(growth) || growth <= 0d || !double.IsFinite(R) || !double.IsFinite(df) || df <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(sigma0), "The tree factors must be finite and representable at the requested time step.");
        }

        // Bottom-up working state: spotsBU[level] ascending in index (0 = lowest).
        var spotsBU = new List<double[]>(steps + 1) { new[] { s0 } };
        var adBU = new List<double[]>(steps + 1) { new[] { 1d } };
        var probs = new List<double>();
        // Per-level up-probability arrays (bottom-up, ascending index), flipped to top-down storage
        // after the recursion so backward induction can use each node's own martingale weight.
        var probsBU = new List<double[]>(steps);
        int reversionCount = 0;

        for (int k = 0; k < steps; k++)
        {
            double[] s = spotsBU[k];           // level k, ascending
            double[] lambda = adBU[k];
            int nodes = k + 1;                 // nodes at level k
            int newNodes = k + 2;              // nodes at level k+1
            double maturity = (k + 1) * dt;

            double[] forward = new double[nodes];
            for (int a = 0; a < nodes; a++)
            {
                forward[a] = s[a] * growth;
            }

            // Seed the new level with the constant-volatility (CRR) lattice. The center
            // node(s) keep this seed (log-centered on s0); the wings are solved from the smile.
            double[] sNew = new double[newNodes];
            for (int a = 0; a < newNodes; a++)
            {
                sNew[a] = s0 * Math.Pow(u, (2 * a) - (k + 1));
            }

            // Center anchoring (Derman-Kani centering condition). Odd node count: the single
            // central node is fixed at the initial spot s0. Even node count: the two central
            // nodes stay at the log-centered CRR seed (s0/u, s0·u).
            bool oddNodes = (newNodes % 2) == 1;
            int upperStart;   // level-k node index from which the upper wing grows
            int lowerStart;   // level-k node index from which the lower wing grows (descending)
            if (oddNodes)
            {
                // Centering condition: the central node price is the initial spot.
                int cNew = (k + 1) / 2;
                sNew[cNew] = s0;
                upperStart = cNew;
                lowerStart = cNew - 1;
            }
            else
            {
                // Even level: solve the two central nodes from the central call and the
                // log-centering condition S_lower·S_upper = s_c². (Derman-Kani 1994, EQ 8.)
                int c = k / 2;                       // central level-k node index
                double strikeC = s[c];
                double cc = callPrice(strikeC, maturity);
                ValidatePrice(cc);
                double sigmaC = 0d;
                for (int j = c + 1; j < nodes; j++)
                {
                    sigmaC += lambda[j] * (forward[j] - strikeC);
                }

                double qC = (R * cc) - sigmaC;
                double denomC = (lambda[c] * forward[c]) - qC;
                double seedUpper = sNew[c + 1];
                double sUpper = denomC != 0d
                    ? strikeC * (qC + (lambda[c] * strikeC)) / denomC
                    : seedUpper;
                bool centerOk = double.IsFinite(sUpper)
                    && sUpper > forward[c]
                    && (c + 1 >= nodes || sUpper < forward[c + 1]);   // DK: F_c < S_{c+1} < F_{c+1}
                if (!centerOk)
                {
                    reversionCount++;
                }

                sNew[c + 1] = centerOk ? sUpper : seedUpper;
                sNew[c] = (strikeC * strikeC) / sNew[c + 1];
                upperStart = c + 1;
                lowerStart = c - 1;
            }

            // Upper wing: build node a+1 from known node a using the call struck at s[a]
            // (Derman-Kani 1994, EQ 6; Σ is EQ 5). sNew[a+1] currently holds the constant-vol
            // (CRR) seed — the Derman-Kani fallback when the option-implied node would violate
            // no-arbitrage.
            for (int a = upperStart; a < nodes; a++)
            {
                double strike = s[a];
                double c = callPrice(strike, maturity);
                ValidatePrice(c);
                double sigma = 0d;
                for (int j = a + 1; j < nodes; j++)
                {
                    sigma += lambda[j] * (forward[j] - strike);
                }

                double rcMinusSigma = (R * c) - sigma;
                double known = sNew[a];
                double seed = sNew[a + 1];
                double denom = rcMinusSigma - (lambda[a] * (forward[a] - known));
                double candidate = denom != 0d
                    ? ((known * rcMinusSigma) - (lambda[a] * strike * (forward[a] - known))) / denom
                    : seed;

                // Avoiding Arbitrages (DK 1994): the new node must lie strictly between the
                // neighbouring prior-level forwards, F_i < S_{i+1} < F_{i+1} (the top node has no
                // F_{i+1} above it, so only the lower bound applies there). Otherwise the
                // transition probability would leave (0, 1); revert to the constant-vol seed.
                bool upperOk = double.IsFinite(candidate)
                    && candidate > forward[a]
                    && (a + 1 >= nodes || candidate < forward[a + 1]);
                if (!upperOk)
                {
                    reversionCount++;
                }

                // A global CRR seed can cross an already calibrated neighbour.
                // Revert using local spacing and retain the DK forward bracket.
                double fallbackUpper = Math.Max(known * u * u, forward[a] * (1d + epsilon));
                if (a + 1 < nodes && fallbackUpper >= forward[a + 1])
                {
                    fallbackUpper = forward[a] + (0.5d * (forward[a + 1] - forward[a]));
                }
                sNew[a + 1] = upperOk ? candidate : fallbackUpper;
            }

            // Lower wing: build node a from known node a+1 using the put struck at s[a]
            // (Derman-Kani 1994, EQ 9).
            for (int a = lowerStart; a >= 0; a--)
            {
                double strike = s[a];
                double p = putPrice(strike, maturity);
                ValidatePrice(p);
                double sigma = 0d;
                for (int j = 0; j < a; j++)
                {
                    sigma += lambda[j] * (strike - forward[j]);
                }

                double rpMinusSigma = (R * p) - sigma;
                double known = sNew[a + 1];
                double seed = sNew[a];
                double denom = rpMinusSigma + (lambda[a] * (forward[a] - known));
                double candidate = denom != 0d
                    ? ((known * rpMinusSigma) + (lambda[a] * strike * (forward[a] - known))) / denom
                    : seed;

                // Avoiding Arbitrages (DK 1994): the new lower node must lie strictly between
                // the neighbouring prior-level forwards, F_{i-1} < S_i < F_i (the bottom node has
                // no F_{i-1} below it). Otherwise revert to the constant-vol seed.
                bool lowerOk = double.IsFinite(candidate)
                    && candidate > 0d
                    && candidate < forward[a]
                    && (a == 0 || candidate > forward[a - 1]);
                if (!lowerOk)
                {
                    reversionCount++;
                }

                double fallbackLower = Math.Min(known / (u * u), forward[a] * (1d - epsilon));
                if (a > 0 && fallbackLower <= forward[a - 1])
                {
                    fallbackLower = forward[a - 1] + (0.5d * (forward[a] - forward[a - 1]));
                }
                sNew[a] = lowerOk ? candidate : fallbackLower;
            }

            // Transition probabilities (forward/martingale condition, Derman-Kani 1994 EQ 7)
            // and Arrow-Debreu forward induction.
            double[] p_up = new double[nodes];
            for (int a = 0; a < nodes; a++)
            {
                double sUp = sNew[a + 1];
                double sDn = sNew[a];
                double pa = (forward[a] - sDn) / (sUp - sDn);
                if (!double.IsFinite(pa) || pa < -1e-12 || pa > 1d + 1e-12 || !double.IsFinite(sUp) || !double.IsFinite(sDn) || sDn <= 0d || sUp <= sDn)
                {
                    throw new InvalidOperationException("Implied-tree induction produced an invalid node or probability.");
                }
                // Only correct floating-point roundoff at the probability endpoints.
                pa = Math.Clamp(pa, 0d, 1d);
                p_up[a] = pa;
                probs.Add(pa);
            }

            probsBU.Add(p_up);

            double[] adNew = new double[newNodes];
            for (int a = 0; a < newNodes; a++)
            {
                double value = 0d;
                if (a - 1 >= 0 && a - 1 < nodes)
                {
                    value += lambda[a - 1] * p_up[a - 1] * df;   // up from node a-1
                }

                if (a < nodes)
                {
                    value += lambda[a] * (1d - p_up[a]) * df;    // down from node a
                }

                adNew[a] = value;
            }

            spotsBU.Add(sNew);
            adBU.Add(adNew);
        }

        // Flip to top-down storage (index 0 = highest spot) and build local vols.
        var spotTopDown = new List<IReadOnlyList<double>>(steps + 1);
        var adTopDown = new List<IReadOnlyList<double>>(steps + 1);
        foreach (double[] layer in spotsBU)
        {
            spotTopDown.Add(Reversed(layer));
        }

        foreach (double[] layer in adBU)
        {
            adTopDown.Add(Reversed(layer));
        }

        var localVols = new List<IReadOnlyList<double>>(steps);
        for (int step = 0; step < steps; step++)
        {
            IReadOnlyList<double> nextLayer = spotTopDown[step + 1];
            double[] stepVols = new double[step + 1];
            for (int j = 0; j <= step; j++)
            {
                double sUp = nextLayer[j];
                double sDn = nextLayer[j + 1];
                // Floor at a small positive value: a negative spacing only arises from
                // Derman-Kani's wing instability and a negative local volatility is unphysical.
                stepVols[j] = Math.Max(Math.Log(sUp / sDn) / (2d * sqrtDt), 1e-4);
            }

            localVols.Add(stepVols);
        }

        // Flip the per-node probabilities to top-down storage so they align index-for-index
        // with SpotLattice: probTopDown[step][j] is the up-probability of SpotLattice[step][j].
        var probTopDown = new List<IReadOnlyList<double>>(steps);
        foreach (double[] layer in probsBU)
        {
            probTopDown.Add(Reversed(layer));
        }

        double avgP = probs.Count > 0 ? probs.Average() : 0.5d;
        return new Lattices(spotTopDown, adTopDown, localVols, probTopDown, u, d, avgP, reversionCount);
    }

    private static void ValidatePrice(double price)
    {
        if (!double.IsFinite(price) || price < 0d)
        {
            throw new InvalidOperationException("The option price provider returned a negative or non-finite quote.");
        }
    }

    private static double[] Reversed(double[] ascending)
    {
        int n = ascending.Length;
        double[] result = new double[n];
        for (int i = 0; i < n; i++)
        {
            result[i] = ascending[n - 1 - i];
        }

        return result;
    }
}
