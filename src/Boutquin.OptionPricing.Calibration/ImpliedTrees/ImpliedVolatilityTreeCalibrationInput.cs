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

using Boutquin.OptionPricing.Calibration.Smile;
using Boutquin.OptionPricing.Domain.Market;

namespace Boutquin.OptionPricing.Calibration.ImpliedTrees;

/// <summary>
/// Input bundle for implied tree calibration, pairing the target volatility surface
/// with the number of discrete time steps the tree should span. The surface supplies
/// the market-observed smile slices that the tree must reproduce, and the step count
/// determines the granularity of the resulting local volatility lattice.
/// </summary>
/// <param name="Surface">Volatility surface whose smile slices the implied tree must reproduce.</param>
/// <param name="Steps">Number of discrete time steps in the implied tree lattice.</param>
/// <param name="Market">Market data snapshot providing spot price and term structure curves for pricing.</param>
/// <param name="PriceProvider">
/// Optional external source of European option prices at arbitrary (strike, maturity) coordinates.
/// When <see langword="null"/>, the calibrator uses <see cref="SurfaceDerivedPriceProvider"/>
/// to derive prices from the surface via Black-Scholes.
/// </param>
public sealed record ImpliedVolatilityTreeCalibrationInput(
    VolatilitySurface Surface,
    int Steps,
    OptionMarketData Market,
    IOptionPriceProvider? PriceProvider = null);
