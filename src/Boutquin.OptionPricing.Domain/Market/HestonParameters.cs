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

namespace Boutquin.OptionPricing.Domain.Market;

/// <summary>
/// The five risk-neutral parameters of the Heston (1993) stochastic-volatility model:
/// the dynamics under which the instantaneous variance follows a mean-reverting square-root
/// (CIR) process correlated with the spot's diffusion.
/// </summary>
/// <remarks>
/// <para>
/// Under the Heston model the spot <c>S</c> and its instantaneous variance <c>v</c> evolve as
/// </para>
/// <code>
/// dS_t = (r − q − b) S_t dt + √(v_t) S_t dW₁
/// dv_t = κ (θ − v_t) dt + σ_v √(v_t) dW₂
/// dW₁ dW₂ = ρ dt
/// </code>
/// <para>
/// where <c>q</c> is dividend yield, <c>b</c> is borrow rate, and <c>κ</c> (<see cref="Kappa"/>) is the mean-reversion speed of the variance toward its
/// long-run level <c>θ</c> (<see cref="Theta"/>), <c>σ_v</c> (<see cref="SigmaV"/>) is the
/// volatility of variance ("vol-of-vol"), <c>ρ</c> (<see cref="Rho"/>) is the instantaneous
/// correlation between the spot and variance shocks, and <c>v₀</c> (<see cref="V0"/>) is the
/// initial variance. A negative <c>ρ</c> produces the negatively-skewed implied-volatility
/// smile characteristic of equity index options.
/// </para>
/// <para>
/// This record stores parameter values; validation of admissible ranges is
/// the responsibility of the consuming pricer (for example
/// <c>Boutquin.OptionPricing.Analytics.StochasticVolatility.SimpleHestonMcPricer</c>), because
/// the admissible set depends on the discretization. It is placed in the domain layer so that
/// Monte Carlo and other stochastic-volatility models can share the same parameterization without an Analytics-layer
/// dependency.
/// </para>
/// <para>
/// <b>Feller condition.</b> When <c>2 κ θ ≥ σ_v²</c> the continuous-time variance process stays
/// strictly positive almost surely. This record does not enforce the condition: full-truncation
/// Euler discretization is well defined in the non-Feller regime, where the discretized variance
/// can touch zero. Consumers may inspect the condition for diagnostic reporting but must not gate
/// pricing on it.
/// </para>
/// <para>
/// <b>Reference:</b> S. L. Heston, "A Closed-Form Solution for Options with Stochastic Volatility
/// with Applications to Bond and Currency Options," <i>Review of Financial Studies</i>, 6(2),
/// 1993, 327–343; N. Chriss, <i>Black-Scholes and Beyond: Option Pricing Models</i>, McGraw-Hill,
/// 1997, Chapter 8.5.
/// </para>
/// </remarks>
/// <param name="V0">
/// The initial instantaneous variance <c>v₀</c> (in variance units, i.e. volatility squared).
/// A non-negative quantity for an admissible model.
/// </param>
/// <param name="Kappa">
/// The mean-reversion speed <c>κ</c> at which the variance is pulled toward its long-run level.
/// A non-negative quantity.
/// </param>
/// <param name="Theta">
/// The long-run variance level <c>θ</c> toward which the process mean-reverts (in variance units).
/// A non-negative quantity.
/// </param>
/// <param name="SigmaV">
/// The volatility of variance <c>σ_v</c> ("vol-of-vol") scaling the variance's own diffusion.
/// A non-negative quantity.
/// </param>
/// <param name="Rho">
/// The instantaneous correlation <c>ρ</c> between the spot and variance Brownian increments.
/// Must lie in <c>[−1, 1]</c>.
/// </param>
public sealed record HestonParameters(
    double V0,
    double Kappa,
    double Theta,
    double SigmaV,
    double Rho);
