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
using Boutquin.MarketData.Abstractions.Calendars;
using Boutquin.OptionPricing.Domain.Enums;

namespace Boutquin.OptionPricing.Domain.Contracts;

/// <summary>
/// Represents a barrier option contract whose payoff activation depends on whether
/// the underlying price breaches a specified barrier level during the option's life.
/// </summary>
/// <remarks>
/// Barrier options combine a vanilla call/put payoff with a knock-in or knock-out
/// condition. The four standard variants are up-and-in, up-and-out, down-and-in, and
/// down-and-out. Barrier parity relates knock-in and knock-out prices:
/// Vanilla = Knock-In + Knock-Out for matching direction and barrier level.
/// </remarks>
public sealed record BarrierOption : OptionContract
{
    /// <summary>
    /// Creates a barrier option contract.
    /// </summary>
    /// <param name="callPut">Whether the underlying vanilla payoff is a call or a put.</param>
    /// <param name="strike">The strike price, in the contract <paramref name="currency"/>. Must be positive.</param>
    /// <param name="currency">
    /// The contract currency. Must not be <see cref="Currency.Unspecified"/>.
    /// </param>
    /// <param name="barrierLevel">
    /// The barrier level that triggers knock-in or knock-out behavior. Must be positive.
    /// </param>
    /// <param name="direction">
    /// Whether the barrier is breached from below (up) or from above (down).
    /// </param>
    /// <param name="knockType">
    /// Whether touching the barrier activates (knock-in) or extinguishes (knock-out)
    /// the option.
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
    /// Thrown when <paramref name="strike"/> or <paramref name="barrierLevel"/> is zero
    /// or negative.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="currency"/> is <see cref="Currency.Unspecified"/>.
    /// </exception>
    public BarrierOption(
        CallPut callPut,
        decimal strike,
        Currency currency,
        decimal barrierLevel,
        BarrierDirection direction,
        BarrierKnockType knockType,
        DateOnly expiryDate,
        IBusinessCalendar? settlementCalendar = null,
        BusinessDayAdjustment? settlementAdjustment = null,
        int settlementLagDays = 0)
        : base(callPut, strike, currency, expiryDate, settlementCalendar, settlementAdjustment, settlementLagDays)
    {
        if (barrierLevel <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(barrierLevel), "Barrier level must be positive.");
        }

        BarrierLevel = barrierLevel;
        Direction = direction;
        KnockType = knockType;
    }

    /// <summary>
    /// The price level at which the barrier event (knock-in or knock-out) is triggered,
    /// expressed in the same currency units as the spot and strike prices. Must be positive;
    /// the validation is enforced on <c>init</c> so it cannot be bypassed via a
    /// <c>with</c> expression.
    /// </summary>
    /// <remarks>Denominated in the contract currency, exactly like the strike amount; no FX conversion is implied.</remarks>
    public decimal BarrierLevel
    {
        get => _barrierLevel;
        init
        {
            if (value <= 0m)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Barrier level must be positive.");
            }

            _barrierLevel = value;
        }
    }

    private readonly decimal _barrierLevel;

    /// <summary>
    /// Whether the underlying must approach the barrier from below (up) or from above
    /// (down) for the barrier event to trigger.
    /// </summary>
    public BarrierDirection Direction
    {
        get => _direction;
        init
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), "A defined barrier direction is required.");
            }
            _direction = value;
        }
    }

    private readonly BarrierDirection _direction;

    /// <summary>
    /// Whether breaching the barrier activates (knock-in) or extinguishes (knock-out)
    /// the option.
    /// </summary>
    public BarrierKnockType KnockType
    {
        get => _knockType;
        init
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), "A defined barrier knocktype is required.");
            }
            _knockType = value;
        }
    }

    private readonly BarrierKnockType _knockType;
}
