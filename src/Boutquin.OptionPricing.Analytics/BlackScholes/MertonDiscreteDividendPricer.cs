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
using Boutquin.Domain.Extensions;
using Boutquin.OptionPricing.Abstractions;
using Boutquin.OptionPricing.Domain.Contracts;
using Boutquin.OptionPricing.Domain.Market;
using Boutquin.OptionPricing.Domain.Results;

namespace Boutquin.OptionPricing.Analytics.BlackScholes;

/// <summary>
/// Prices European options on a stock paying known, discrete cash dividends using
/// Merton's escrowed-dividend approximation (Chriss, <i>Black-Scholes and Beyond</i>, section 4.9).
/// </summary>
/// <remarks>
/// <para>
/// The escrowed-dividend model treats the present value of every cash dividend paid
/// before expiry as a deterministic, riskless component of the spot that is "escrowed"
/// out of the stochastic part. Writing the dividend ex-dates as <c>t_i &lt; T</c> with
/// per-share amounts <c>d_i</c> and risk-free discount factors <c>DF(t_i)</c> taken from
/// the market's discount curve:
/// </para>
/// <code>
/// PV(divs) = Σ_i d_i · DF(t_i)        present value of the escrowed dividends
/// S*       = S₀ − PV(divs)            escrowed (stochastic) spot
/// σ*       = (S₀ / S*) · σ            volatility uplift on the smaller stochastic base
/// </code>
/// <para>
/// The European option is then priced by the closed-form <see cref="BlackScholesPricer"/>
/// on the adjusted spot <c>S*</c> and uplifted volatility <c>σ*</c>, with the discount and
/// dividend curves otherwise left intact. The uplift <c>σ* = (S₀ / S*)·σ</c> preserves
/// instantaneous dollar volatility: <c>S*·σ* = S₀·σ</c>. It does not exactly preserve the
/// terminal stock-price variance, which depends nonlinearly on volatility and time.
/// The smaller stochastic base requires a larger return volatility, so <c>σ* &gt; σ</c> whenever
/// <c>PV(divs) &gt; 0</c> (asserted by <c>PositivePvDividends_UpliftsVolatilityStrictly</c>).
/// </para>
/// <para>
/// <b>Model assumption — escrowed-dividend approximation.</b> The cash dividends are assumed
/// known and deterministic, and the stock price is modeled as the sum of a riskless escrowed
/// component (the present value of the remaining dividends) and a lognormal stochastic
/// component. This is an <i>approximation</i>: the true discrete-dividend dynamics are not
/// lognormal at any horizon, and the escrowed model is most accurate for small dividends well
/// inside the option's life. It is the standard analytical European approximation; American
/// options require an appropriate dividend-aware numerical model rather than this
/// closed form.
/// </para>
/// <para>
/// <b>Reference:</b> N. Chriss, <i>Black-Scholes and Beyond: Option Pricing Models</i>,
/// McGraw-Hill, 1997, section 4.9 (the escrowed-dividend / Merton adjustment).
/// </para>
/// <para>
/// <b>Edge cases.</b> An empty schedule — or one whose ex-dates all fall on or after expiry —
/// yields <c>PV(divs) = 0</c>, so <c>S* = S₀</c> and <c>σ* = σ</c> and the result is identical
/// to <see cref="BlackScholesPricer"/> (pinned to 1e-12 by
/// <c>EmptySchedule_EqualsBlackScholesPricer_ToOneInTwelve</c>). When the present value of
/// dividends meets or exceeds the spot the escrowed spot would be non-positive and the model
/// is undefined; the pricer returns
/// <see cref="PriceResult.Unsupported(string, string, Boutquin.Domain.Enumerations.Currency)"/> for "Merton-DiscreteDividend"
/// rather than throwing.
/// </para>
/// <para>
/// The schedule is model configuration injected at construction; the pricer is otherwise
/// stateless with respect to the market snapshot, matching the
/// <see cref="IOptionPricer{TOption,TMarket,TResult}"/> contract.
/// </para>
/// </remarks>
public sealed class MertonDiscreteDividendPricer
    : IOptionPricer<EuropeanOption, OptionMarketData, PriceResult>
{
    /// <summary>The pricing model identifier stamped on every produced result.</summary>
    private const string Model = "Merton-DiscreteDividend";

    private readonly DividendSchedule _schedule;
    private readonly BlackScholesPricer _blackScholes = new();

    /// <summary>
    /// Creates a Merton discrete-dividend pricer for a fixed dividend schedule.
    /// </summary>
    /// <param name="schedule">
    /// The known, discrete cash-dividend schedule. Dividends with ex-dates on or after the
    /// option's expiry are not captured by the option holder and are excluded from
    /// <c>PV(divs)</c>. An empty schedule reduces the model to plain Black-Scholes. Must not
    /// be <see langword="null"/>.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="schedule"/> is <see langword="null"/>.
    /// </exception>
    public MertonDiscreteDividendPricer(DividendSchedule schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        _schedule = schedule;
    }

    /// <summary>
    /// Prices a European option under the escrowed-dividend approximation for the supplied market.
    /// </summary>
    /// <param name="option">
    /// The European option contract specifying strike, expiry, and call/put flag.
    /// </param>
    /// <param name="market">
    /// Market snapshot supplying the spot, flat volatility, and the discount curve used to
    /// present-value the escrowed dividends.
    /// </param>
    /// <returns>
    /// A <see cref="PriceResult"/> carrying the escrowed-model price with
    /// <see cref="Boutquin.OptionPricing.Domain.Enums.CalculationStatus.Success"/>, a
    /// <c>ModelName</c> of "Merton-DiscreteDividend", an assumption summary, and diagnostics
    /// for <c>pvDividends</c>, <c>escrowedSpot</c>, and <c>upliftedVolatility</c>. When the
    /// present value of dividends meets or exceeds the spot, returns
    /// <see cref="PriceResult.Unsupported(string, string, Boutquin.Domain.Enumerations.Currency)"/> instead.
    /// </returns>
    /// <exception cref="CurrencyMismatchException">
    /// Thrown when <paramref name="market"/>'s spot currency differs from <paramref name="option"/>'s
    /// contract currency.
    /// </exception>
    public PriceResult Price(EuropeanOption option, OptionMarketData market)
    {
        ArgumentNullException.ThrowIfNull(option);
        ArgumentNullException.ThrowIfNull(market);

        if (market.Spot.Currency != option.Currency)
        {
            throw new CurrencyMismatchException(option.Currency, market.Spot.Currency);
        }

        double pvDividends = PresentValueOfDividends(market, option.ExpiryDate);

        double spot = (double)market.Spot.Amount;
        double escrowedSpot = spot - pvDividends;

        // The escrowed spot is the stochastic base of the lognormal process. If the present
        // value of dividends meets or exceeds the spot, S* <= 0 and the lognormal model — and
        // the volatility uplift S0/S* — are undefined. Report this as an unsupported scenario
        // rather than constructing an OptionMarketData (whose Spot init guard would throw) or
        // producing a nonsense price.
        if (escrowedSpot <= 0d)
        {
            return PriceResult.Unsupported(
                Model,
                $"Present value of dividends ({pvDividends:R}) meets or exceeds the spot " +
                $"({spot:R}); the escrowed spot would be non-positive and the model is undefined.",
                option.Currency);
        }

        double sigma = market.FlatVolatility;
        double upliftedVolatility = (spot / escrowedSpot) * sigma;

        // Delegate to the closed-form Black-Scholes pricer on the adjusted inputs. Spot and
        // FlatVolatility are init-only with positivity guards; the escrowedSpot > 0 check above
        // and a strictly positive sigma (validated by the original market) keep the `with`
        // expression within those invariants. All other curves (discount, dividend, borrow,
        // projection) are carried over unchanged so curve-derived discounting is preserved.
        var escrowedMarket = market with
        {
            Spot = market.Spot.Currency.Amount((decimal)escrowedSpot),
            FlatVolatility = upliftedVolatility,
        };

        PriceResult inner = _blackScholes.Price(option, escrowedMarket);

        return new PriceResult(
            inner.Price,
            inner.Greeks,
            inner.Status,
            new Dictionary<string, object?>
            {
                ["pvDividends"] = pvDividends,
                ["escrowedSpot"] = escrowedSpot,
                ["upliftedVolatility"] = upliftedVolatility,
                ["originalSpot"] = spot,
                ["originalVolatility"] = sigma,
                ["dividendCount"] = CountCapturedDividends(market.ValuationDate, option.ExpiryDate),
            },
            Model,
            "Escrowed-dividend (Merton) approximation: present value of discrete cash dividends " +
            "removed from the stochastic spot (S* = S0 - PV(divs)) with an instantaneous-dollar-volatility-preserving " +
            "volatility uplift (sigma* = (S0/S*)*sigma), then priced by closed-form Black-Scholes. " +
            "Known deterministic dividends; European exercise.");
    }

    /// <summary>
    /// Sums the present value of every dividend whose ex-date falls strictly before
    /// <paramref name="expiryDate"/>, discounting each amount with the market's risk-free
    /// discount curve. Dividends on or after expiry are not captured by the option holder
    /// and contribute zero.
    /// </summary>
    private double PresentValueOfDividends(OptionMarketData market, DateOnly expiryDate)
    {
        double pv = 0d;
        foreach (DividendPayment payment in _schedule.Payments)
        {
            if (payment.ExDate < expiryDate && payment.ExDate >= market.ValuationDate)
            {
                pv += (double)payment.Amount * market.DiscountFactor(payment.ExDate);
            }
        }

        return pv;
    }

    /// <summary>
    /// Counts the dividends captured by an option (ex-date in [<paramref name="valuationDate"/>,
    /// <paramref name="expiryDate"/>)), mirroring the same filter applied by
    /// <see cref="PresentValueOfDividends"/>. Dividends with ex-dates before the valuation
    /// date have already gone ex and contribute zero present value; counting them would make
    /// the diagnostic inconsistent with the PV it accompanies.
    /// </summary>
    private int CountCapturedDividends(DateOnly valuationDate, DateOnly expiryDate)
    {
        int count = 0;
        foreach (DividendPayment payment in _schedule.Payments)
        {
            if (payment.ExDate < expiryDate && payment.ExDate >= valuationDate)
            {
                count++;
            }
        }

        return count;
    }
}
