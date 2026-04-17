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

using Boutquin.OptionPricing.Exotics.Barriers;
using FluentAssertions;

namespace Boutquin.OptionPricing.Exotics.Tests;

public sealed class BarrierParityTests
{
    [Fact]
    public void ImpliedKnockOutPrice_ShouldSubtractKnockInFromVanilla()
    {
        decimal result = BarrierParity.ImpliedKnockOutPrice(10m, 4m);

        result.Should().Be(6m);
    }

    [Fact]
    public void ImpliedKnockInPrice_ShouldSubtractKnockOutFromVanilla()
    {
        decimal result = BarrierParity.ImpliedKnockInPrice(10m, 3m);

        result.Should().Be(7m);
    }
}
