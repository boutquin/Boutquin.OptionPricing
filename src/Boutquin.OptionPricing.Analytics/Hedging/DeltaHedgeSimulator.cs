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

using Boutquin.OptionPricing.Analytics.BlackScholes;
using Boutquin.OptionPricing.Domain.Enums;

namespace Boutquin.OptionPricing.Analytics.Hedging;

/// <summary>
/// Discrete delta-hedge simulator that walks a realized spot path, rebalances at each
/// step using Black-Scholes delta, and reports the hedging P/L versus the option payoff.
/// </summary>
/// <remarks>
/// <para>
/// The simulator maintains a self-financing portfolio: at inception the premium is
/// collected, shares are purchased at the initial delta, and the residual is invested
/// at the risk-free rate. At each rebalance the delta is recomputed and the share
/// position is adjusted, with the cost financed by the cash account.
/// </para>
/// <para>
/// Hedging error arises because rebalancing is discrete rather than continuous.
/// The standard deviation of the error shrinks as √(dt), so doubling the number of
/// rebalancing steps halves the error's standard deviation.
/// </para>
/// </remarks>
public sealed class DeltaHedgeSimulator
{
    /// <summary>
    /// Runs a discrete delta-hedge simulation along the spot path specified in
    /// <paramref name="request"/>.
    /// </summary>
    /// <param name="request">
    /// Simulation inputs: the European option, the initial market snapshot, and the
    /// chronological sequence of realized spot prices. The first element of
    /// <see cref="DeltaHedgeSimulationRequest.PathSpots"/> is the inception spot;
    /// the last is the terminal spot at expiry.
    /// </param>
    /// <returns>
    /// A <see cref="DeltaHedgeSimulationResult"/> containing the starting option price,
    /// delta at each rebalance, the hedging portfolio's terminal value, the option
    /// payoff, and the hedging error.
    /// </returns>
    public DeltaHedgeSimulationResult Simulate(DeltaHedgeSimulationRequest request)
    {
        var option = request.Option;
        var market = request.InitialMarket;
        var spots = request.PathSpots;
        int steps = spots.Count - 1;

        if (steps < 1)
        {
            return new DeltaHedgeSimulationResult(0m, [], spots, 0m, 0m, 0m);
        }

        var pricer = new BlackScholesPricer();
        var greekProvider = new BlackScholesGreeks();

        double totalTime = market.TimeToExpiryInYears(option.ExpiryDate);
        double dt = totalTime / steps;
        double riskFreeRate = market.RiskFreeRate(option.ExpiryDate);
        double growthPerStep = Math.Exp(riskFreeRate * dt);

        // Initial state: collect premium and establish delta hedge
        decimal startPrice = pricer.Price(option, market).Price;
        var deltas = new List<double>(steps);

        var initialGreeks = greekProvider.CalculateGreeks(option, market);
        double currentDelta = initialGreeks.Delta ?? 0d;
        deltas.Add(currentDelta);

        // Cash = premium collected minus initial stock purchase
        double cashPosition = (double)startPrice - currentDelta * (double)spots[0];

        // Walk the path, rebalancing at each step
        for (int i = 1; i < steps; i++)
        {
            // Cash earns the risk-free rate
            cashPosition *= growthPerStep;

            // Create market snapshot at this point in time.
            // Phase 5c: invert the step time fraction through the discount curve's
            // day-count (via OptionMarketData.InvertYearFraction) rather than a hard
            // 365.25 divisor. Keeps the simulator's step dates consistent with the T
            // that every greek along the path is computed against.
            DateOnly stepDate = market.InvertYearFraction(i * dt);
            if (stepDate >= option.ExpiryDate)
            {
                stepDate = option.ExpiryDate.AddDays(-1);
            }

            // Roll the market to stepDate. A plain `market with { ValuationDate = stepDate }`
            // would be wrong: the contained curves keep their original ValuationDate,
            // silently producing a time-stretched implied rate at every rebalance.
            // OptionMarketData.RolledTo propagates the roll into each curve via
            // IDiscountCurve.WithValuationDate so the implied rate is invariant under
            // time advancement, which is the correct semantics under forwards-realize.
            var stepMarket = market.RolledTo(stepDate, spots[i]);
            var stepGreeks = greekProvider.CalculateGreeks(option, stepMarket);
            double newDelta = stepGreeks.Delta ?? 0d;

            // Rebalance: adjust share position
            cashPosition -= (newDelta - currentDelta) * (double)spots[i];
            currentDelta = newDelta;
            deltas.Add(currentDelta);
        }

        // Terminal step: grow cash one more period
        cashPosition *= growthPerStep;

        // Final portfolio value: shares × terminal spot + cash
        double terminalSpot = (double)spots[^1];
        double finalPortfolio = currentDelta * terminalSpot + cashPosition;

        // Option payoff
        double payoff = option.CallPut switch
        {
            CallPut.Call => Math.Max(terminalSpot - (double)option.Strike, 0d),
            CallPut.Put => Math.Max((double)option.Strike - terminalSpot, 0d),
            _ => 0d
        };

        decimal optionPayoff = (decimal)payoff;
        decimal portfolioValue = (decimal)finalPortfolio;
        decimal hedgingError = portfolioValue - optionPayoff;

        return new DeltaHedgeSimulationResult(
            startPrice,
            deltas,
            spots,
            optionPayoff,
            portfolioValue,
            hedgingError);
    }
}
