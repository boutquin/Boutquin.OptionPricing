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
/// Represents a single discrete dividend payment with its ex-dividend date and
/// cash amount per share.
/// </summary>
/// <param name="ExDate">
/// The ex-dividend date on which the underlying price is expected to drop by the
/// dividend amount at market open.
/// </param>
/// <param name="Amount">
/// The dividend cash amount per share, in the same currency as the spot price.
/// </param>
/// <remarks>The amount uses the currency of the market/option receiving this schedule. A schedule must not mix currencies.</remarks>
public sealed record DividendPayment(DateOnly ExDate, decimal Amount);
