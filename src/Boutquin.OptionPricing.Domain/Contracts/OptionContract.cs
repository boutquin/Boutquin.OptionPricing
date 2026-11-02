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

using Boutquin.Domain.Enumerations;
using Boutquin.Domain.Exceptions;
using Boutquin.Domain.Extensions;
using Boutquin.Domain.ValueObjects;
using Boutquin.MarketData.Abstractions.Calendars;
using Boutquin.OptionPricing.Domain.Enums;

namespace Boutquin.OptionPricing.Domain.Contracts;

/// <summary>
/// Base record for all option contracts, capturing the call/put flag, strike price,
/// expiry date, and contract <see cref="Currency"/> that are common to every exercise style.
/// </summary>
/// <remarks>
/// Concrete subtypes (<see cref="EuropeanOption"/>, <see cref="AmericanOption"/>,
/// <see cref="BarrierOption"/>) add exercise-style-specific fields. The strike must
/// be positive and carry the contract currency; construction throws
/// <see cref="ArgumentOutOfRangeException"/> for a non-positive strike and
/// <see cref="ArgumentException"/> when the contract currency is
/// <see cref="Currency.Unspecified"/>.
/// </remarks>
public abstract record OptionContract
{
    /// <summary>
    /// Initializes the common option contract fields.
    /// </summary>
    /// <param name="callPut">Whether the option is a call or a put.</param>
    /// <param name="strike">
    /// The strike price at which the holder may buy (call) or sell (put) the underlying,
    /// expressed in the contract <paramref name="currency"/>. Must be positive.
    /// </param>
    /// <param name="currency">
    /// The currency in which the strike and the option's monetary quantities are denominated.
    /// Must not be <see cref="Currency.Unspecified"/>.
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
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="currency"/> is <see cref="Currency.Unspecified"/>.
    /// </exception>
    protected OptionContract(
        CallPut callPut,
        decimal strike,
        Currency currency,
        DateOnly expiryDate,
        IBusinessCalendar? settlementCalendar = null,
        BusinessDayAdjustment? settlementAdjustment = null,
        int settlementLagDays = 0)
    {
        if (strike <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(strike), "Strike must be positive.");
        }

        if (currency == Currency.Unspecified)
        {
            throw new ArgumentException("Contract currency must not be Currency.Unspecified.", nameof(currency));
        }

        CallPut = callPut;
        Currency = currency;
        Strike = currency.Amount(strike);
        ExpiryDate = expiryDate;
        SettlementCalendar = settlementCalendar;
        SettlementAdjustment = settlementAdjustment;
        SettlementLagDays = settlementLagDays;
    }

    /// <summary>
    /// Whether this option is a call (right to buy) or a put (right to sell).
    /// </summary>
    public CallPut CallPut
    {
        get => _callPut;
        init
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), "A defined call/put flag is required.");
            }
            _callPut = value;
        }
    }

    private readonly CallPut _callPut;

    /// <summary>
    /// The currency in which this contract's strike and monetary quantities are denominated.
    /// Never <see cref="Currency.Unspecified"/>, and never divergent from
    /// <see cref="Strike"/>'s currency: both checks are enforced on <c>init</c> so they cannot
    /// be bypassed via a <c>with</c> expression. A <c>with { Currency = … }</c> that sets a
    /// currency differing from the already-set <see cref="Strike"/> currency throws, eliminating
    /// the cross-currency desync where the contract currency and strike currency disagree.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown on <c>init</c> when the value is <see cref="Currency.Unspecified"/>, or when it
    /// diverges from the already-set <see cref="Strike"/> currency (a <c>with</c>-expression
    /// re-set may not desync the contract currency from the strike currency).
    /// </exception>
    public Currency Currency
    {
        get => _currency;
        init
        {
            if (value == Currency.Unspecified)
            {
                throw new ArgumentException(
                    "Contract currency must not be Currency.Unspecified.",
                    nameof(value));
            }

            // The constructor assigns Currency BEFORE Strike, so on first assignment _strike is
            // default(Money) (Amount 0, Currency Unspecified); the cross-check is skipped until a
            // strike has been set. A later with-expression re-set must keep the strike's currency.
            if (_strike.Amount > 0m && value != _strike.Currency)
            {
                throw new ArgumentException(
                    $"Contract currency ({value}) must equal the strike currency ({_strike.Currency}); " +
                    "a with-expression may not desync the contract currency from the strike.",
                    nameof(value));
            }

            _currency = value;
        }
    }

    private readonly Currency _currency;

    /// <summary>
    /// The strike price at which the holder may exercise, as a <see cref="Money"/> denominated
    /// in the contract <see cref="Currency"/>. The amount must be positive and the currency must
    /// equal <see cref="Currency"/>; both checks are enforced on <c>init</c> so they cannot be
    /// bypassed via a <c>with</c> expression.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown on <c>init</c> when the strike amount is zero or negative.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown on <c>init</c> when the strike currency differs from the contract
    /// <see cref="Currency"/>.
    /// </exception>
    public Money Strike
    {
        get => _strike;
        init
        {
            if (value.Amount <= 0m)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Strike must be positive.");
            }

            if (value.Currency != Currency)
            {
                throw new ArgumentException(
                    $"Strike currency ({value.Currency}) must equal the contract currency ({Currency}).",
                    nameof(value));
            }

            _strike = value;
        }
    }

    private readonly Money _strike;

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
    public int SettlementLagDays
    {
        get => _settlementLagDays;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _settlementLagDays = value;
        }
    }

    private readonly int _settlementLagDays;

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
    /// max(S − K, 0) for calls, max(K − S, 0) for puts, returned as a <see cref="Money"/>
    /// in the contract <see cref="Currency"/>.
    /// </summary>
    /// <param name="spot">
    /// The current underlying asset price as a <see cref="Money"/>. Its amount must be positive
    /// and its currency must equal the contract <see cref="Currency"/>.
    /// </param>
    /// <returns>
    /// The non-negative intrinsic payoff in the contract currency, which is the option's value
    /// if exercised immediately with zero time value.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when the <paramref name="spot"/> amount is zero or negative.
    /// </exception>
    /// <exception cref="CurrencyMismatchException">
    /// Thrown when the <paramref name="spot"/> currency differs from the contract
    /// <see cref="Currency"/>.
    /// </exception>
    public Money IntrinsicValue(Money spot)
    {
        if (spot.Amount <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(spot), "Spot must be positive.");
        }

        if (spot.Currency != Currency)
        {
            throw new CurrencyMismatchException(Currency, spot.Currency);
        }

        decimal payoff = CallPut switch
        {
            CallPut.Call => decimal.Max(spot.Amount - _strike.Amount, 0m),
            CallPut.Put => decimal.Max(_strike.Amount - spot.Amount, 0m),
            _ => throw new NotSupportedException($"Unsupported call/put flag: {CallPut}.")
        };

        return Currency.Amount(payoff);
    }
}
