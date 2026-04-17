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

namespace Boutquin.OptionPricing.Calibration.ImpliedTrees;

/// <summary>
/// Provides European option prices at arbitrary (strike, maturity) coordinates.
/// Used by implied tree calibrators to obtain market-consistent option prices
/// during forward induction without coupling to a specific pricing model.
/// </summary>
public interface IOptionPriceProvider
{
    /// <summary>
    /// Returns the European call option price at the given strike and maturity.
    /// </summary>
    /// <param name="strike">The option strike price.</param>
    /// <param name="maturityInYears">The time to expiry in year fractions.</param>
    /// <returns>The call option price.</returns>
    double GetCallPrice(decimal strike, double maturityInYears);

    /// <summary>
    /// Returns the European put option price at the given strike and maturity.
    /// </summary>
    /// <param name="strike">The option strike price.</param>
    /// <param name="maturityInYears">The time to expiry in year fractions.</param>
    /// <returns>The put option price.</returns>
    double GetPutPrice(decimal strike, double maturityInYears);
}
