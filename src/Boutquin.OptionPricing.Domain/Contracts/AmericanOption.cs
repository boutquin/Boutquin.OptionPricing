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
/// Represents an American-exercise option contract that can be exercised at any time
/// up to and including the expiry date, defined by its call/put flag, strike price,
/// and expiry date.
/// </summary>
/// <remarks>
/// American options require a pricing model that evaluates early exercise at each
/// decision point, such as a binomial tree with backward induction. The Black-Scholes
/// closed-form pricer accepts European contracts only; use an American tree pricer.
/// </remarks>
public sealed record AmericanOption : OptionContract
{
    /// <summary>
    /// Creates an American option contract.
    /// </summary>
    /// <param name="callPut">Whether the option is a call or a put.</param>
    /// <param name="strike">
    /// The strike price, in the contract <paramref name="currency"/>. Must be positive.
    /// </param>
    /// <param name="currency">
    /// The contract currency. Must not be <see cref="Currency.Unspecified"/>.
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
    public AmericanOption(
        CallPut callPut,
        decimal strike,
        Currency currency,
        DateOnly expiryDate,
        IBusinessCalendar? settlementCalendar = null,
        BusinessDayAdjustment? settlementAdjustment = null,
        int settlementLagDays = 0)
        : base(callPut, strike, currency, expiryDate, settlementCalendar, settlementAdjustment, settlementLagDays)
    {
    }
}
