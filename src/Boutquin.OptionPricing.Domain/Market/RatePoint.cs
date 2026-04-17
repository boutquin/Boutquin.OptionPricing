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
/// A single point on a term-structure curve, pairing a tenor with its continuously
/// compounded rate.
/// </summary>
/// <param name="TimeInYears">
/// The tenor expressed as a year fraction from the valuation date (for example 0.25
/// for three months, 1.0 for one year).
/// </param>
/// <param name="ContinuouslyCompoundedRate">
/// The continuously compounded annualized rate at this tenor, as a decimal fraction
/// (for example 0.05 for 5%).
/// </param>
public sealed record RatePoint(double TimeInYears, double ContinuouslyCompoundedRate);
