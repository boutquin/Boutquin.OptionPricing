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
/// Assembles a <see cref="VolatilitySurface"/> from a flat collection of surface points by
/// grouping them into maturity-ordered smile slices, each internally sorted by strike.
/// </summary>
public sealed class VolatilitySurfaceBuilder
{
    /// <summary>
    /// Groups <paramref name="points"/> by maturity, orders each group by strike, and
    /// returns the resulting volatility surface with maturity-ascending slices.
    /// </summary>
    /// <param name="points">Unordered collection of surface points spanning one or more maturities.</param>
    /// <returns>A fully constructed <see cref="VolatilitySurface"/> ready for calibration or arbitrage validation.</returns>
    public VolatilitySurface Build(IEnumerable<SurfacePoint> points)
    {
        List<SmileSlice> slices = points
            .GroupBy(point => point.MaturityInYears)
            .OrderBy(group => group.Key)
            .Select(group => new SmileSlice(group.Key, group.OrderBy(point => point.Strike).ToList()))
            .ToList();

        return new VolatilitySurface(slices);
    }
}
