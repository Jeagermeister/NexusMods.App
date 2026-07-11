using BenchmarkDotNet.Attributes;
using Apocrypha.Abstractions.FileExtractor;
using Apocrypha.Backend.FileExtractor.FileSignatures;
using Apocrypha.Benchmarks.Interfaces;
using Apocrypha.Sdk.FileExtractor;

namespace Apocrypha.Benchmarks.Benchmarks;

[BenchmarkInfo("Signature Check", "Tests signature checker performance.")]
[MemoryDiagnoser]
public class SignatureChecking : IBenchmark
{
    private MemoryStream _data7Z = null!;
    private MemoryStream _dataZip = null!;
    private MemoryStream _notFound = null!;
    private ISignatureChecker _signatureChecker = null!;

    [GlobalSetup]
    public void Setup()
    {
        _data7Z = new MemoryStream(File.ReadAllBytes(Assets.Sample7zFile));
        _dataZip = new MemoryStream(File.ReadAllBytes(Assets.SampleZipFile));
        _notFound = new MemoryStream(new byte[1024]);
        _signatureChecker = new SignatureChecker(Enum.GetValues<FileType>());
    }

    [Benchmark]
    public async ValueTask<IReadOnlyList<FileType>> Find7Z()
    {
        _data7Z.Position = 0;
        return await _signatureChecker.MatchesAsync(_data7Z);
    }

    [Benchmark]
    public async ValueTask<IReadOnlyList<FileType>> FindZip()
    {
        _dataZip.Position = 0;
        return await _signatureChecker.MatchesAsync(_dataZip);
    }

    [Benchmark(Baseline = true)]
    public async ValueTask<IReadOnlyList<FileType>> FindNone()
    {
        _notFound.Position = 0;
        return await _signatureChecker.MatchesAsync(_notFound);
    }
}
