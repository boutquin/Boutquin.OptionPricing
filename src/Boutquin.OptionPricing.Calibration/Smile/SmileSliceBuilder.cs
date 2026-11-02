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
/// Constructs a <see cref="SmileSlice"/> by filtering a flat collection of surface points
/// down to those matching a target maturity (within floating-point tolerance) and ordering
/// the survivors by strike.
/// </summary>
public sealed class SmileSliceBuilder
{
    /// <summary>
    /// Filters <paramref name="points"/> to retain only those whose maturity matches
    /// <paramref name="maturityInYears"/> within 1e-9, then returns them as a strike-ordered
    /// smile slice.
    /// </summary>
    /// <param name="maturityInYears">Target maturity year fraction used to select matching surface points.</param>
    /// <param name="points">Unordered collection of surface points that may span multiple maturities.</param>
    /// <returns>A <see cref="SmileSlice"/> containing only the points at the requested maturity, sorted by strike.</returns>
    public SmileSlice Build(double maturityInYears, IEnumerable<SurfacePoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        List<SurfacePoint> materialized = points
            .Where(point => Math.Abs(point.MaturityInYears - maturityInYears) < 1e-9d)
            .OrderBy(point => point.Strike)
            .ToList();

        return new SmileSlice(maturityInYears, materialized);
    }
}
