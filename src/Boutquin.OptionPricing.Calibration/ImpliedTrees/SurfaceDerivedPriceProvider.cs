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

using Boutquin.Numerics.Distributions;
using Boutquin.OptionPricing.Calibration.Smile;
using Boutquin.OptionPricing.Domain.Market;

namespace Boutquin.OptionPricing.Calibration.ImpliedTrees;

/// <summary>
/// Derives European option prices from a <see cref="VolatilitySurface"/> via the
/// Black-Scholes formula. This is the default <see cref="IOptionPriceProvider"/>
/// when no external price source is available.
/// </summary>
/// <remarks>
/// For each (strike, maturity) query, the provider looks up the implied volatility
/// from the surface and computes the Generalized Black-Scholes price using the
/// market data's spot, risk-free rate, and dividend yield.
/// </remarks>
public sealed class SurfaceDerivedPriceProvider : IOptionPriceProvider
{
    private readonly VolatilitySurface _surface;
    private readonly OptionMarketData _market;
    private readonly DateOnly _expiryDate;

    /// <summary>
    /// Creates a price provider backed by a volatility surface and market data.
    /// </summary>
    /// <param name="surface">The volatility surface supplying implied vols at each (strike, maturity).</param>
    /// <param name="market">Market data providing spot, discount curve, and dividend curve.</param>
    /// <param name="expiryDate">The expiry date used to derive rates from curves.</param>
    public SurfaceDerivedPriceProvider(VolatilitySurface surface, OptionMarketData market, DateOnly expiryDate)
    {
        _surface = surface;
        _market = market;
        _expiryDate = expiryDate;
    }

    /// <inheritdoc />
    public double GetCallPrice(decimal strike, double maturityInYears)
    {
        // Phase 5c: route the surface read through the Curves-aligned dated overload
        // when the maturity year fraction is positive — maturityInYears is already in
        // the discount curve's day-count units because the calibrator derives it from
        // OptionMarketData.TimeToExpiryInYears, but going through the dated API pins
        // the contract so future refactors cannot silently diverge.
        double sigma = _surface.GetVol(strike, maturityInYears);
        return BlackScholesCall(sigma, strike, maturityInYears);
    }

    /// <inheritdoc />
    public double GetPutPrice(decimal strike, double maturityInYears)
    {
        double sigma = _surface.GetVol(strike, maturityInYears);
        return BlackScholesPut(sigma, strike, maturityInYears);
    }

    /// <summary>
    /// Dated-query overload: returns the European call price at a strike and expiry
    /// date, deriving maturity via the market's discount-curve day-count convention.
    /// </summary>
    /// <param name="strike">The option strike.</param>
    /// <param name="expiryDate">The option expiry date.</param>
    /// <returns>The surface-derived Black-Scholes call price.</returns>
    public double GetCallPrice(decimal strike, DateOnly expiryDate)
    {
        double sigma = _surface.GetVol(strike, _market.ValuationDate, expiryDate, _market.DiscountCurve.DayCount);
        double t = _market.TimeToExpiryInYears(expiryDate);
        return BlackScholesCall(sigma, strike, t);
    }

    /// <summary>
    /// Dated-query overload: returns the European put price at a strike and expiry
    /// date, deriving maturity via the market's discount-curve day-count convention.
    /// </summary>
    /// <param name="strike">The option strike.</param>
    /// <param name="expiryDate">The option expiry date.</param>
    /// <returns>The surface-derived Black-Scholes put price.</returns>
    public double GetPutPrice(decimal strike, DateOnly expiryDate)
    {
        double sigma = _surface.GetVol(strike, _market.ValuationDate, expiryDate, _market.DiscountCurve.DayCount);
        double t = _market.TimeToExpiryInYears(expiryDate);
        return BlackScholesPut(sigma, strike, t);
    }

    private double BlackScholesCall(double sigma, decimal strike, double t)
    {
        if (t <= 0d)
        {
            return Math.Max((double)_market.Spot - (double)strike, 0d);
        }

        double s = (double)_market.Spot;
        double k = (double)strike;
        double r = _market.RiskFreeRate(_expiryDate);
        double q = _market.DividendYield(_expiryDate);

        double sigmaSqrtT = sigma * Math.Sqrt(t);
        double d1 = (Math.Log(s / k) + ((r - q + (0.5d * sigma * sigma)) * t)) / sigmaSqrtT;
        double d2 = d1 - sigmaSqrtT;

        // Phase 5b: route N(d1), N(d2) through CumulativeNormal.Evaluate directly —
        // Laikov 2025 exponential-free erf, ~14-digit precision (arXiv:2504.05068).
        return (s * Math.Exp(-q * t) * CumulativeNormal.Evaluate(d1)) - (k * Math.Exp(-r * t) * CumulativeNormal.Evaluate(d2));
    }

    private double BlackScholesPut(double sigma, decimal strike, double t)
    {
        if (t <= 0d)
        {
            return Math.Max((double)strike - (double)_market.Spot, 0d);
        }

        double s = (double)_market.Spot;
        double k = (double)strike;
        double r = _market.RiskFreeRate(_expiryDate);
        double q = _market.DividendYield(_expiryDate);

        double sigmaSqrtT = sigma * Math.Sqrt(t);
        double d1 = (Math.Log(s / k) + ((r - q + (0.5d * sigma * sigma)) * t)) / sigmaSqrtT;
        double d2 = d1 - sigmaSqrtT;

        // Phase 5b: route N(-d1), N(-d2) through CumulativeNormal.Evaluate directly —
        // Laikov 2025 exponential-free erf, ~14-digit precision (arXiv:2504.05068).
        return (k * Math.Exp(-r * t) * CumulativeNormal.Evaluate(-d2)) - (s * Math.Exp(-q * t) * CumulativeNormal.Evaluate(-d1));
    }
}
