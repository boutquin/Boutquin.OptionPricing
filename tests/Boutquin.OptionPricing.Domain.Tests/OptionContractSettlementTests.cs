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
using Boutquin.MarketData.Abstractions.Calendars;
using Boutquin.MarketData.Calendars;
using Boutquin.OptionPricing.Domain.Contracts;
using Boutquin.OptionPricing.Domain.Enums;
using FluentAssertions;

namespace Boutquin.OptionPricing.Domain.Tests;

public sealed class OptionContractSettlementTests
{
    [Fact]
    public void SettlementDate_ShouldEqualExpiryDate_WhenNoCalendar()
    {
        var expiry = new DateOnly(2025, 7, 18); // Friday
        var option = new EuropeanOption(CallPut.Call, 100m, Currency.USD, expiry);

        option.SettlementDate.Should().Be(expiry);
    }

    [Fact]
    public void SettlementDate_ShouldAdjustToNextBusinessDay_WhenExpiryFallsOnWeekend()
    {
        // 2025-07-19 is Saturday
        var expiry = new DateOnly(2025, 7, 19);
        var calendar = new WeekendOnlyCalendar();
        var option = new EuropeanOption(CallPut.Call, 100m, Currency.USD, expiry,
            settlementCalendar: calendar,
            settlementAdjustment: BusinessDayAdjustment.Following);

        // Should adjust to Monday 2025-07-21
        option.SettlementDate.Should().Be(new DateOnly(2025, 7, 21));
    }

    [Fact]
    public void SettlementDate_WithSettlementLag_ShouldAdvanceBusinessDays()
    {
        // 2025-07-18 is Friday
        var expiry = new DateOnly(2025, 7, 18);
        var calendar = new WeekendOnlyCalendar();
        var option = new EuropeanOption(CallPut.Call, 100m, Currency.USD, expiry,
            settlementCalendar: calendar,
            settlementAdjustment: BusinessDayAdjustment.Following,
            settlementLagDays: 1);

        // Expiry is Friday (business day), lag 1 business day → Monday 2025-07-21
        option.SettlementDate.Should().Be(new DateOnly(2025, 7, 21));
    }

    [Fact]
    public void EuropeanOption_ShouldAcceptCalendarAndAdjustment()
    {
        var expiry = new DateOnly(2025, 7, 18);
        var calendar = new WeekendOnlyCalendar();

        var option = new EuropeanOption(CallPut.Call, 100m, Currency.USD, expiry,
            settlementCalendar: calendar,
            settlementAdjustment: BusinessDayAdjustment.ModifiedFollowing);

        option.SettlementCalendar.Should().BeSameAs(calendar);
        option.SettlementAdjustment.Should().Be(BusinessDayAdjustment.ModifiedFollowing);
    }

    [Fact]
    public void AmericanOption_ShouldAcceptCalendarAndAdjustment()
    {
        var expiry = new DateOnly(2025, 7, 19); // Saturday
        var calendar = new WeekendOnlyCalendar();

        var option = new AmericanOption(CallPut.Put, 100m, Currency.USD, expiry,
            settlementCalendar: calendar,
            settlementAdjustment: BusinessDayAdjustment.Following);

        option.SettlementDate.Should().Be(new DateOnly(2025, 7, 21));
    }

    [Fact]
    public void BarrierOption_ShouldAcceptCalendarAndAdjustment()
    {
        var expiry = new DateOnly(2025, 7, 19); // Saturday
        var calendar = new WeekendOnlyCalendar();

        var option = new BarrierOption(CallPut.Call, 100m, Currency.USD, 120m,
            BarrierDirection.Up, BarrierKnockType.Out, expiry,
            settlementCalendar: calendar,
            settlementAdjustment: BusinessDayAdjustment.Following);

        option.SettlementDate.Should().Be(new DateOnly(2025, 7, 21));
    }

    [Fact]
    public void ExistingConstructor_ShouldContinueToWork()
    {
        var expiry = new DateOnly(2025, 7, 18);

        // These should compile and work exactly as before
        var european = new EuropeanOption(CallPut.Call, 100m, Currency.USD, expiry);
        var american = new AmericanOption(CallPut.Put, 100m, Currency.USD, expiry);
        var barrier = new BarrierOption(CallPut.Call, 100m, Currency.USD, 120m,
            BarrierDirection.Up, BarrierKnockType.Out, expiry);

        european.ExpiryDate.Should().Be(expiry);
        american.ExpiryDate.Should().Be(expiry);
        barrier.ExpiryDate.Should().Be(expiry);

        // No calendar → settlement = expiry
        european.SettlementDate.Should().Be(expiry);
        american.SettlementDate.Should().Be(expiry);
        barrier.SettlementDate.Should().Be(expiry);
    }
}
