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
    /// <exception cref="ArgumentNullException">The surface or market is null.</exception>
    public SurfaceDerivedPriceProvider(VolatilitySurface surface, OptionMarketData market, DateOnly expiryDate)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(market);
        _surface = surface;
        _market = market;
        _expiryDate = expiryDate;
    }

    /// <summary>Prices a European call using the surface volatility and curve carry at the requested year fraction.</summary>
    /// <param name="strike">Positive strike in the market spot currency.</param>
    /// <param name="maturityInYears">Finite non-negative year fraction; zero returns intrinsic value.</param>
    /// <returns>The call premium in the market currency.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The strike or maturity is invalid.</exception>
    public double GetCallPrice(decimal strike, double maturityInYears)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(strike);
        if (!double.IsFinite(maturityInYears) || maturityInYears < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(maturityInYears));
        }
        double sigma = _surface.GetVol(strike, maturityInYears);
        (double r, double q) = RatesAt(maturityInYears);
        return BlackScholesCall(sigma, strike, maturityInYears, r, q);
    }

    /// <summary>Prices a European put using the surface volatility and curve carry at the requested year fraction.</summary>
    /// <param name="strike">Positive strike in the market spot currency.</param>
    /// <param name="maturityInYears">Finite non-negative year fraction; zero returns intrinsic value.</param>
    /// <returns>The put premium in the market currency.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The strike or maturity is invalid.</exception>
    public double GetPutPrice(decimal strike, double maturityInYears)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(strike);
        if (!double.IsFinite(maturityInYears) || maturityInYears < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(maturityInYears));
        }
        double sigma = _surface.GetVol(strike, maturityInYears);
        (double r, double q) = RatesAt(maturityInYears);
        return BlackScholesPut(sigma, strike, maturityInYears, r, q);
    }

    // Derive the risk-free rate and effective yield at the queried maturity (not at the
    // fixed terminal expiry), so multi-maturity queries during forward induction respect
    // the curve term structure. For flat curves this is identical to the terminal rates.
    // The effective yield folds the borrow cost into the dividend yield (effectiveQ = q + b),
    // matching the library-wide cost-of-carry convention used by BlackScholesPricer, the tree
    // builders, and OptionMarketData.ForwardPrice (F = S·exp((r − q − b)·T)). This keeps the
    // surface-derived target prices on the same carry as the calibrated tree's forward.
    private (double Rate, double EffectiveYield) RatesAt(double maturityInYears)
    {
        if (maturityInYears <= 0d)
        {
            return (_market.RiskFreeRate(_expiryDate), _market.DividendYield(_expiryDate) + _market.BorrowRate(_expiryDate));
        }

        DateOnly date = _market.InvertYearFraction(maturityInYears);
        return (_market.RiskFreeRate(date), _market.DividendYield(date) + _market.BorrowRate(date));
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
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(strike);
        double sigma = _surface.GetVol(strike, _market.ValuationDate, expiryDate, _market.DiscountCurve.DayCount);
        double t = _market.TimeToExpiryInYears(expiryDate);
        // Effective yield folds borrow into the dividend yield (effectiveQ = q + b), matching the
        // library carry convention (BlackScholesPricer / OptionMarketData.ForwardPrice).
        return BlackScholesCall(sigma, strike, t, _market.RiskFreeRate(expiryDate), _market.DividendYield(expiryDate) + _market.BorrowRate(expiryDate));
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
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(strike);
        double sigma = _surface.GetVol(strike, _market.ValuationDate, expiryDate, _market.DiscountCurve.DayCount);
        double t = _market.TimeToExpiryInYears(expiryDate);
        // Effective yield folds borrow into the dividend yield (effectiveQ = q + b), matching the
        // library carry convention (BlackScholesPricer / OptionMarketData.ForwardPrice).
        return BlackScholesPut(sigma, strike, t, _market.RiskFreeRate(expiryDate), _market.DividendYield(expiryDate) + _market.BorrowRate(expiryDate));
    }

    private double BlackScholesCall(double sigma, decimal strike, double t, double r, double q)
    {
        if (t <= 0d)
        {
            return Math.Max((double)_market.Spot.Amount - (double)strike, 0d);
        }

        double s = (double)_market.Spot.Amount;
        double k = (double)strike;

        double sigmaSqrtT = sigma * Math.Sqrt(t);
        if (!double.IsFinite(sigmaSqrtT) || sigmaSqrtT > Math.Sqrt(double.MaxValue))
        {
            throw new InvalidOperationException("The surface quote produces non-representable total variance.");
        }
        double d1 = ((Math.Log(s / k) + ((r - q) * t)) / sigmaSqrtT) + (0.5d * sigmaSqrtT);
        double d2 = d1 - sigmaSqrtT;

        // N(d1), N(d2) via CumulativeNormal<double>.Evaluate — exponential-free erf,
        // ~14-digit precision (Laikov 2025, arXiv:2504.05068).
        return (s * Math.Exp(-q * t) * CumulativeNormal<double>.Evaluate(d1)) - (k * Math.Exp(-r * t) * CumulativeNormal<double>.Evaluate(d2));
    }

    private double BlackScholesPut(double sigma, decimal strike, double t, double r, double q)
    {
        if (t <= 0d)
        {
            return Math.Max((double)strike - (double)_market.Spot.Amount, 0d);
        }

        double s = (double)_market.Spot.Amount;
        double k = (double)strike;

        double sigmaSqrtT = sigma * Math.Sqrt(t);
        if (!double.IsFinite(sigmaSqrtT) || sigmaSqrtT > Math.Sqrt(double.MaxValue))
        {
            throw new InvalidOperationException("The surface quote produces non-representable total variance.");
        }
        double d1 = ((Math.Log(s / k) + ((r - q) * t)) / sigmaSqrtT) + (0.5d * sigmaSqrtT);
        double d2 = d1 - sigmaSqrtT;

        // Phase 5b: route N(-d1), N(-d2) through CumulativeNormal<double>.Evaluate directly —
        // Laikov 2025 exponential-free erf, ~14-digit precision (arXiv:2504.05068).
        return (k * Math.Exp(-r * t) * CumulativeNormal<double>.Evaluate(-d2)) - (s * Math.Exp(-q * t) * CumulativeNormal<double>.Evaluate(-d1));
    }
}
