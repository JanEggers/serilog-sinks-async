using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;
using Serilog.Sinks.Async.PerformanceTests;
using System.Xml;
using Xunit;


//BenchmarkRunner.Run<ThroughputBenchmark>(); // channel 4x faster // immutable -20% slower
BenchmarkRunner.Run<LatencyBenchmark>(); // channel -27 slower % 

var i = 0;
