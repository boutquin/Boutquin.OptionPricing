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

using Boutquin.Domain.Exceptions;
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
/// This solver is restricted to <see cref="EuropeanOption"/> contracts. Requests
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
/// <c>2·ε·|b| + 0.5·tol</c> test.
/// </para>
/// <para>
/// The volatility bracket starts at <c>[1e-6, max(2·InitialGuess, 5.0)]</c> — so a caller's
/// <see cref="ImpliedVolatilityRequest.InitialGuess"/> widens the search — and the upper bound
/// is doubled adaptively (up to 20.0) until it brackets the target. Before solving, the target
/// is checked against the no-arbitrage bounds: a price below the discounted intrinsic floor or
/// above the price at the maximum tested volatility returns
/// <see cref="CalculationStatus.FailedToConverge"/> with an explanatory reason rather than
/// throwing on a missing sign change.
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
    /// <exception cref="CurrencyMismatchException">
    /// Thrown when <paramref name="request"/>'s market spot currency differs from
    /// <paramref name="request"/>'s option contract currency.
    /// </exception>
    /// <exception cref="ArgumentNullException">The request, option, or market is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The premium is negative, or the initial guess or tolerances are invalid.</exception>
    public ImpliedVolatilityResult Solve(ImpliedVolatilityRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Option);
        ArgumentNullException.ThrowIfNull(request.Market);
        ArgumentOutOfRangeException.ThrowIfNegative(request.ObservedPrice);

        if (!double.IsFinite(request.InitialGuess))
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                request.InitialGuess,
                "InitialGuess must be a finite number.");
        }

        if (request.FunctionTolerance <= 0d || !double.IsFinite(request.FunctionTolerance))
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                request.FunctionTolerance,
                "FunctionTolerance must be a positive finite number.");
        }

        if (request.BracketTolerance <= 0d || !double.IsFinite(request.BracketTolerance))
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                request.BracketTolerance,
                "BracketTolerance must be a positive finite number.");
        }

        if (request.Option is not EuropeanOption europeanOption)
        {
            return new ImpliedVolatilityResult(
                null,
                CalculationStatus.UnsupportedScenario,
                0,
                new Dictionary<string, object?> { ["reason"] = "This solver supports European options only." });
        }

        if (request.Market.TimeToExpiryInYears(europeanOption.ExpiryDate) <= 0d)
        {
            return Failure("Implied volatility is not identifiable at or after expiry.");
        }
        Func<double, double> objective = sigma =>
        {
            var market = request.Market.WithFlatVolatility(sigma);
            return (double)_pricer.Price(europeanOption, market).Price.Amount - (double)request.ObservedPrice;
        };

        // The Black-Scholes price is monotone increasing in volatility, so the objective is
        // monotone too. Bracket the root in [VolFloor, upper], seeding the upper bound from the
        // caller's InitialGuess and expanding adaptively until it brackets the target price.
        const double volFloor = 1e-6;
        double upper = Math.Clamp(Math.Max(request.InitialGuess * 2d, DefaultUpperVol), DefaultUpperVol, MaxUpperVol);

        double residualAtFloor = objective(volFloor);
        if (residualAtFloor > 0d)
        {
            // Even at (effectively) zero volatility the model price exceeds the target: the
            // observed price is below the no-arbitrage floor (discounted intrinsic), so no
            // arbitrage-free implied volatility exists.
            return Failure("Observed price is below the no-arbitrage floor; no implied volatility exists.");
        }

        int expansions = 0;
        while (objective(upper) < 0d && upper < MaxUpperVol && expansions < MaxBracketExpansions)
        {
            upper = Math.Min(upper * 2d, MaxUpperVol);
            expansions++;
        }

        if (objective(upper) < 0d)
        {
            return Failure($"Observed price exceeds the price at the maximum tested volatility ({MaxUpperVol:F1}).");
        }

        // Brent uses a single tolerance for both residual and bracket half-width; use the
        // tighter of the two request tolerances so behavior is at least as strict as the
        // prior BisectionSolver (which converged when EITHER tolerance was met).
        var solverTolerance = Math.Min(request.FunctionTolerance, request.BracketTolerance);
        var solver = new BrentSolver<double>(tolerance: solverTolerance);

        try
        {
            var result = solver.Solve(objective, lowerBound: volFloor, upperBound: upper);
            return new ImpliedVolatilityResult(
                result.Root,
                result.Converged ? CalculationStatus.Success : CalculationStatus.FailedToConverge,
                result.Iterations,
                new Dictionary<string, object?>
                {
                    ["finalResidual"] = result.FinalResidual,
                    ["bracketUpper"] = upper,
                });
        }
        catch (InvalidOperationException ex)
        {
            // BrentSolver throws when the bracket does not contain a sign change.
            return Failure(ex.Message);
        }
    }

    private const double DefaultUpperVol = 5.0;
    private const double MaxUpperVol = 20.0;
    private const int MaxBracketExpansions = 8;

    private static ImpliedVolatilityResult Failure(string reason) =>
        new(null, CalculationStatus.FailedToConverge, 0, new Dictionary<string, object?> { ["reason"] = reason });
}
