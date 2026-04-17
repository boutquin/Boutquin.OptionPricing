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

using System.Reflection;
using NetArchTest.Rules;

namespace Boutquin.OptionPricing.ArchitectureTests.Helpers;

/// <summary>
/// Base class for architecture tests, providing assembly references for all source projects
/// and a helper method for extracting failing type names from test results.
/// </summary>
public abstract class BaseArchitectureTest
{
    /// <summary>Gets the Abstractions assembly.</summary>
    protected static Assembly AbstractionsAssembly =>
        typeof(Abstractions.IOptionPricer<,,>).Assembly;

    /// <summary>Gets the Domain assembly.</summary>
    protected static Assembly DomainAssembly =>
        typeof(Domain.Contracts.OptionContract).Assembly;

    /// <summary>Gets the Analytics assembly.</summary>
    protected static Assembly AnalyticsAssembly =>
        typeof(Analytics.BlackScholes.BlackScholesPricer).Assembly;

    /// <summary>Gets the Trees assembly.</summary>
    protected static Assembly TreesAssembly =>
        typeof(Trees.Binomial.BinomialTree).Assembly;

    /// <summary>Gets the Calibration assembly.</summary>
    protected static Assembly CalibrationAssembly =>
        typeof(Calibration.Smile.VolatilitySurface).Assembly;

    /// <summary>Gets the Exotics assembly.</summary>
    protected static Assembly ExoticsAssembly =>
        typeof(Exotics.Barriers.BarrierTreePricer).Assembly;

    /// <summary>
    /// Returns a comma-separated list of failing type names from a test result,
    /// or an empty string if none failed.
    /// </summary>
    protected static string GetFailingTypes(TestResult result)
    {
        return result.FailingTypes != null
            ? string.Join(", ", result.FailingTypes.Select(t => t.Name))
            : string.Empty;
    }
}
