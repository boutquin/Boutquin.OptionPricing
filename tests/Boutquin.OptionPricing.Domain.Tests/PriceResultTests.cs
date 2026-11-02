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

using Boutquin.Domain.Enumerations;
using Boutquin.Domain.Extensions;
using Boutquin.OptionPricing.Domain.Enums;
using Boutquin.OptionPricing.Domain.Results;
using FluentAssertions;

namespace Boutquin.OptionPricing.Domain.Tests;

public sealed class PriceResultTests
{
    [Fact]
    public void IsSuccess_ShouldBeTrue_ForSuccessStatus()
    {
        var result = new PriceResult(Currency.USD.Amount(1.23m), Status: CalculationStatus.Success);

        result.IsSuccess.Should().BeTrue();
    }

    // IRODICT-INDEXER-004 — the empty-diagnostics indexer must honour the
    // IReadOnlyDictionary<TKey,TValue> contract: a missing key throws KeyNotFoundException
    // (it must NOT silently return null).
    [Fact]
    public void GetDiagnostics_EmptyIndexer_MissingKey_ThrowsKeyNotFound()
    {
        var result = new PriceResult(Currency.USD.Amount(1.23m), Diagnostics: null);
        IReadOnlyDictionary<string, object?> diagnostics = result.GetDiagnostics();

        Action act = () => _ = diagnostics["missing"];

        act.Should().Throw<KeyNotFoundException>();
    }

    // IRODICT-INDEXER-004 — TryGetValue / ContainsKey keep returning false for the empty map.
    [Fact]
    public void GetDiagnostics_Empty_TryGetValueAndContainsKey_ReturnFalse()
    {
        var result = new PriceResult(Currency.USD.Amount(1.23m), Diagnostics: null);
        IReadOnlyDictionary<string, object?> diagnostics = result.GetDiagnostics();

        diagnostics.ContainsKey("missing").Should().BeFalse();
        diagnostics.TryGetValue("missing", out object? value).Should().BeFalse();
        value.Should().BeNull();
    }
}
