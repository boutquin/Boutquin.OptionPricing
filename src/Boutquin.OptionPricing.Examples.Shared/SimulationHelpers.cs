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

namespace Boutquin.OptionPricing.Examples.Shared;

/// <summary>
/// Shared Monte Carlo simulation utilities for all example projects.
/// </summary>
public static class SimulationHelpers
{
    /// <summary>
    /// Draws a standard normal sample using the Box-Muller transform.
    /// </summary>
    public static double NormalSample(Random rng)
    {
        double u1 = 1d - rng.NextDouble();
        double u2 = rng.NextDouble();
        return Math.Sqrt(-2d * Math.Log(u1)) * Math.Cos(2d * Math.PI * u2);
    }
}
