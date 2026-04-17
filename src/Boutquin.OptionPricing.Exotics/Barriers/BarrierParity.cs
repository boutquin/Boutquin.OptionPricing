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

namespace Boutquin.OptionPricing.Exotics.Barriers;

/// <summary>
/// Applies the barrier parity relationship: Vanilla = Knock-In + Knock-Out,
/// allowing one barrier variant to be implied from the other two prices.
/// </summary>
/// <remarks>
/// Barrier parity holds for matching barrier direction and level:
/// V_vanilla = V_knock-in + V_knock-out.
/// This relationship enables cross-checking barrier prices and deriving
/// one variant from the vanilla price and the complementary barrier price.
/// </remarks>
public static class BarrierParity
{
    /// <summary>
    /// Derives the knock-out price from the vanilla and knock-in prices using
    /// V_knock-out = V_vanilla − V_knock-in.
    /// </summary>
    /// <param name="vanillaPrice">
    /// The price of the equivalent vanilla option (same strike, expiry, and call/put flag).
    /// </param>
    /// <param name="knockInPrice">
    /// The price of the knock-in barrier option with matching barrier level and direction.
    /// </param>
    /// <returns>The implied knock-out barrier option price.</returns>
    public static decimal ImpliedKnockOutPrice(decimal vanillaPrice, decimal knockInPrice)
    {
        return vanillaPrice - knockInPrice;
    }

    /// <summary>
    /// Derives the knock-in price from the vanilla and knock-out prices using
    /// V_knock-in = V_vanilla − V_knock-out.
    /// </summary>
    /// <param name="vanillaPrice">
    /// The price of the equivalent vanilla option (same strike, expiry, and call/put flag).
    /// </param>
    /// <param name="knockOutPrice">
    /// The price of the knock-out barrier option with matching barrier level and direction.
    /// </param>
    /// <returns>The implied knock-in barrier option price.</returns>
    public static decimal ImpliedKnockInPrice(decimal vanillaPrice, decimal knockOutPrice)
    {
        return vanillaPrice - knockOutPrice;
    }
}
