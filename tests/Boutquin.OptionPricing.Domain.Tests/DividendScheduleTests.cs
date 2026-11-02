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

using Boutquin.OptionPricing.Domain.Market;
using FluentAssertions;

namespace Boutquin.OptionPricing.Domain.Tests;

public sealed class DividendScheduleTests
{
    [Fact]
    public void Constructor_ShouldDefensivelyCopyPayments_SoLaterMutationOfCallerListIsNotObserved()
    {
        var payments = new List<DividendPayment>
        {
            new(new DateOnly(2026, 3, 15), 2.0m)
        };

        var schedule = new DividendSchedule(payments);

        // The caller mutates its original reference after construction. An immutable
        // record must not observe this — its snapshot was taken at construction time.
        payments.Add(new DividendPayment(new DateOnly(2026, 6, 15), 3.0m));

        schedule.Payments.Should().HaveCount(1);
        schedule.Payments[0].Amount.Should().Be(2.0m);
    }

    [Fact]
    public void Payments_ShouldNotBeCastableBackToMutableList()
    {
        var payments = new List<DividendPayment>
        {
            new(new DateOnly(2026, 3, 15), 2.0m)
        };

        var schedule = new DividendSchedule(payments);

        // A consumer must not be able to cast the exposed collection back to List<T>
        // and mutate the schedule's internal state through it.
        (schedule.Payments as List<DividendPayment>).Should().BeNull();
    }

    [Fact]
    public void Constructor_ShouldPreservePaymentOrderAndContents()
    {
        var p1 = new DividendPayment(new DateOnly(2026, 3, 15), 2.0m);
        var p2 = new DividendPayment(new DateOnly(2026, 6, 15), 3.0m);

        var schedule = new DividendSchedule(new[] { p1, p2 });

        schedule.Payments.Should().Equal(p1, p2);
    }

    [Fact]
    public void Empty_ShouldHaveNoPayments()
    {
        DividendSchedule.Empty.Payments.Should().BeEmpty();
    }
}
