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

using Boutquin.Numerics.Solvers;
using Boutquin.OptionPricing.Analytics.BlackScholes;
using Boutquin.OptionPricing.Domain.Contracts;
using Boutquin.OptionPricing.Domain.Enums;

namespace Boutquin.OptionPricing.Calibration.ImpliedVolatility;

/// <summary>
/// Recovers the implied volatility of a European option by applying Brent's method to the
/// Black-Scholes pricing function. The solver constructs an objective whose root is the
/// volatility at which the model price equals the observed market price, then iterates until
/// the convergence tolerance is satisfied.
/// </summary>
/// <remarks>
/// <para>
/// The v1 implementation is restricted to <see cref="EuropeanOption"/> contracts. Requests
/// for other contract types return <see cref="CalculationStatus.UnsupportedScenario"/>
/// immediately, without invoking the root-finder.
/// </para>
/// <para>
/// The root-finder uses <see cref="BrentSolver"/> from <c>Boutquin.Numerics.Solvers</c>,
/// which combines inverse quadratic interpolation with the Oliveira-Della-Pasqua-Steffen
/// (2024) halving-interval guarantee — worst-case convergence in ⌈log₂((b−a)/tol)⌉
/// iterations (matching bisection) while typically converging super-linearly on smooth
/// objectives. This replaces the prior <c>BisectionSolver</c> under Phase 5b of the
/// Numerics migration; Brent strictly dominates bisection on iteration count for the
/// Black-Scholes IV objective (monotone in sigma away from the wings).
/// </para>
/// <para>
/// The Brent tolerance is taken as the tighter of <see cref="ImpliedVolatilityRequest.FunctionTolerance"/>
/// and <see cref="ImpliedVolatilityRequest.BracketTolerance"/>; Brent internally applies it
/// to both the residual and the bracket half-width via the standard
/// <c>2·ε·|b| + 0.5·tol</c> test. The bracket defaults to [1e-6, 5.0] (annualized volatility).
/// </para>
/// </remarks>
public sealed class ImpliedVolatilitySolver
{
    private readonly BlackScholesPricer _pricer = new();

    /// <summary>
    /// Solves for the implied volatility that equates the Black-Scholes model price to the
    /// observed market price carried in <paramref name="request"/>.
    /// </summary>
    /// <param name="request">
    /// Implied volatility request containing the option contract, market data, observed price,
    /// initial guess, and convergence tolerance.
    /// </param>
    /// <returns>
    /// An <see cref="ImpliedVolatilityResult"/> whose <see cref="ImpliedVolatilityResult.ImpliedVolatility"/>
    /// is populated when the solver converges, or <see langword="null"/> when the scenario is
    /// unsupported or convergence fails.
    /// </returns>
    public ImpliedVolatilityResult Solve(ImpliedVolatilityRequest request)
    {
        if (request.Option is not EuropeanOption europeanOption)
        {
            return new ImpliedVolatilityResult(
                null,
                CalculationStatus.UnsupportedScenario,
                0,
                new Dictionary<string, object?> { ["reason"] = "v1 solver supports European options only." });
        }

        Func<double, double> objective = sigma =>
        {
            var market = request.Market.WithFlatVolatility(sigma);
            return (double)_pricer.Price(europeanOption, market).Price - (double)request.ObservedPrice;
        };

        // Brent uses a single tolerance for both residual and bracket half-width; use the
        // tighter of the two request tolerances so behavior is at least as strict as the
        // prior BisectionSolver (which converged when EITHER tolerance was met).
        var solverTolerance = Math.Min(request.FunctionTolerance, request.BracketTolerance);
        var solver = new BrentSolver(tolerance: solverTolerance);

        try
        {
            var result = solver.Solve(objective, lowerBound: 1e-6, upperBound: 5.0);
            return new ImpliedVolatilityResult(
                result.Root,
                result.Converged ? CalculationStatus.Success : CalculationStatus.FailedToConverge,
                result.Iterations,
                new Dictionary<string, object?>
                {
                    ["finalResidual"] = result.FinalResidual,
                });
        }
        catch (InvalidOperationException ex)
        {
            // BrentSolver throws when the bracket does not contain a sign change.
            return new ImpliedVolatilityResult(
                null,
                CalculationStatus.FailedToConverge,
                0,
                new Dictionary<string, object?> { ["reason"] = ex.Message });
        }
    }
}
