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

using BenchmarkDotNet.Attributes;
using Boutquin.Numerics.Solvers;

namespace Boutquin.OptionPricing.Benchmarks;

/// <summary>
/// Benchmarks comparing root solver convergence: bisection, Brent, and Newton-Raphson.
/// </summary>
[MemoryDiagnoser]
public class NumericalBenchmarks
{
    private BisectionSolver<double> _bisection = null!;
    private BrentSolver<double> _brent = null!;
    private NewtonRaphsonSolver<double> _newton = null!;

    /// <summary>
    /// Initializes solver instances.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _bisection = new BisectionSolver<double>(functionTolerance: 1e-12, bracketTolerance: 1e-12);
        _brent = new BrentSolver<double>(tolerance: 1e-12);
        _newton = new NewtonRaphsonSolver<double>(functionTolerance: 1e-12, bracketTolerance: 1e-12, stepTolerance: 0);
    }

    /// <summary>Bisection solver: find sqrt(5) via f(x) = x^2 - 5.</summary>
    [Benchmark(Baseline = true)]
    public double BisectionSolve()
    {
        return _bisection.Solve(x => (x * x) - 5d, 1d, 3d).Root;
    }

    /// <summary>Brent solver: find sqrt(5) via f(x) = x^2 - 5.</summary>
    [Benchmark]
    public double BrentSolve()
    {
        return _brent.Solve(x => (x * x) - 5d, 1d, 3d).Root;
    }

    /// <summary>Newton-Raphson solver: find sqrt(5) via f(x) = x^2 - 5.</summary>
    [Benchmark]
    public double NewtonRaphsonSolve()
    {
        return _newton.Solve(x => (x * x) - 5d, 1d, 3d).Root;
    }
}
