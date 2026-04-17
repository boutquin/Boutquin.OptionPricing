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

using Boutquin.MarketData.Abstractions.Calendars;
using Boutquin.OptionPricing.Domain.Enums;

namespace Boutquin.OptionPricing.Domain.Contracts;

/// <summary>
/// Base record for all option contracts, capturing the call/put flag, strike price,
/// and expiry date that are common to every exercise style.
/// </summary>
/// <remarks>
/// Concrete subtypes (<see cref="EuropeanOption"/>, <see cref="AmericanOption"/>,
/// <see cref="BarrierOption"/>) add exercise-style-specific fields. The strike must
/// be positive; construction throws <see cref="ArgumentOutOfRangeException"/> otherwise.
/// </remarks>
public abstract record OptionContract
{
    /// <summary>
    /// Initializes the common option contract fields.
    /// </summary>
    /// <param name="callPut">Whether the option is a call or a put.</param>
    /// <param name="strike">
    /// The strike price at which the holder may buy (call) or sell (put) the underlying.
    /// Must be positive.
    /// </param>
    /// <param name="expiryDate">The date on which the option expires.</param>
    /// <param name="settlementCalendar">
    /// Optional business-day calendar for settlement date adjustment.
    /// </param>
    /// <param name="settlementAdjustment">
    /// Optional business-day adjustment rule applied to the expiry date.
    /// </param>
    /// <param name="settlementLagDays">
    /// Number of business days between the adjusted expiry and cash settlement. Defaults to 0.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="strike"/> is zero or negative.
    /// </exception>
    protected OptionContract(
        CallPut callPut,
        decimal strike,
        DateOnly expiryDate,
        IBusinessCalendar? settlementCalendar = null,
        BusinessDayAdjustment? settlementAdjustment = null,
        int settlementLagDays = 0)
    {
        if (strike <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(strike), "Strike must be positive.");
        }

        CallPut = callPut;
        Strike = strike;
        ExpiryDate = expiryDate;
        SettlementCalendar = settlementCalendar;
        SettlementAdjustment = settlementAdjustment;
        SettlementLagDays = settlementLagDays;
    }

    /// <summary>
    /// Whether this option is a call (right to buy) or a put (right to sell).
    /// </summary>
    public CallPut CallPut { get; init; }

    /// <summary>
    /// The strike price at which the holder may exercise, expressed in the same
    /// currency units as the spot price.
    /// </summary>
    public decimal Strike { get; init; }

    /// <summary>
    /// The date on which the option expires and can no longer be exercised.
    /// </summary>
    public DateOnly ExpiryDate { get; init; }

    /// <summary>
    /// Optional business-day calendar used to adjust the settlement date.
    /// When <see langword="null"/>, the settlement date equals the expiry date.
    /// </summary>
    public IBusinessCalendar? SettlementCalendar { get; init; }

    /// <summary>
    /// Business-day adjustment rule applied to the expiry date when computing
    /// the settlement date. Ignored when <see cref="SettlementCalendar"/> is <see langword="null"/>.
    /// </summary>
    public BusinessDayAdjustment? SettlementAdjustment { get; init; }

    /// <summary>
    /// Number of business days between the (adjusted) expiry date and the
    /// cash settlement date. Ignored when <see cref="SettlementCalendar"/> is <see langword="null"/>.
    /// </summary>
    public int SettlementLagDays { get; init; }

    /// <summary>
    /// The settlement date, computed by adjusting the expiry date with the
    /// settlement calendar and advancing by the settlement lag. When no calendar
    /// is provided, returns the expiry date unchanged.
    /// </summary>
    public DateOnly SettlementDate
    {
        get
        {
            if (SettlementCalendar is null)
            {
                return ExpiryDate;
            }

            var adjusted = SettlementAdjustment.HasValue
                ? SettlementCalendar.Adjust(ExpiryDate, SettlementAdjustment.Value)
                : ExpiryDate;

            return SettlementLagDays > 0
                ? SettlementCalendar.Advance(adjusted, SettlementLagDays)
                : adjusted;
        }
    }

    /// <summary>
    /// Computes the intrinsic value of the option at the given spot price:
    /// max(S − K, 0) for calls, max(K − S, 0) for puts.
    /// </summary>
    /// <param name="spot">
    /// The current underlying asset price. Must be positive.
    /// </param>
    /// <returns>
    /// The non-negative intrinsic payoff, which is the option's value if exercised
    /// immediately with zero time value.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="spot"/> is zero or negative.
    /// </exception>
    public decimal IntrinsicValue(decimal spot)
    {
        if (spot <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(spot), "Spot must be positive.");
        }

        return CallPut switch
        {
            CallPut.Call => decimal.Max(spot - Strike, 0m),
            CallPut.Put => decimal.Max(Strike - spot, 0m),
            _ => 0m
        };
    }
}
