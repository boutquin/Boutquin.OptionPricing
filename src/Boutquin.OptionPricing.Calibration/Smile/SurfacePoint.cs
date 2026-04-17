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
/// A single node on the volatility surface, identified by its maturity and strike coordinates,
/// carrying the implied volatility observed or calibrated at that point.
/// Surface points are the atomic building blocks from which smile slices and the full
/// volatility surface are assembled.
/// </summary>
/// <param name="MaturityInYears">Time to expiry expressed as an annualized year fraction.</param>
/// <param name="Strike">Option strike price at this surface node.</param>
/// <param name="ImpliedVolatility">Annualized implied volatility at this (maturity, strike) coordinate, expressed as a decimal (e.g. 0.20 for 20 %).</param>
public sealed record SurfacePoint(
    double MaturityInYears,
    decimal Strike,
    double ImpliedVolatility);
