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

namespace Boutquin.OptionPricing.Domain.Market;

/// <summary>
/// An ordered collection of discrete dividend payments expected during an option's life,
/// used by tree pricers that model spot-price drops on ex-dividend dates.
/// </summary>
/// <param name="Payments">
/// The chronologically ordered list of dividend payments. An empty list indicates
/// no discrete dividends; continuous dividend yield is modeled separately via the
/// dividend curve in <see cref="OptionMarketData"/>.
/// </param>
public sealed record DividendSchedule(IReadOnlyList<DividendPayment> Payments)
{
    /// <summary>
    /// A shared empty schedule representing no discrete dividend payments.
    /// </summary>
    public static DividendSchedule Empty { get; } = new(Array.Empty<DividendPayment>());
}
