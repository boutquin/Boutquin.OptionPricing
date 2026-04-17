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

namespace Boutquin.OptionPricing.Calibration.Smile;

/// <summary>
/// A single maturity slice of a volatility smile — a fixed time-to-expiry paired with
/// the set of (strike, implied volatility) surface points observed or calibrated at that maturity.
/// Slices are ordered by strike within the list and represent one horizontal cross-section
/// of the full volatility surface.
/// </summary>
/// <param name="MaturityInYears">Time to expiry expressed as an annualized year fraction (e.g. 0.25 for three months).</param>
/// <param name="Points">Strike-ordered collection of surface points that define the smile at this maturity.</param>
public sealed record SmileSlice(
    double MaturityInYears,
    IReadOnlyList<SurfacePoint> Points);
