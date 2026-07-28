using System.Text;
using Apocrypha.Sdk.Hashes;

namespace Apocrypha.Sdk.Tests.Hashes;

// TUnit0059 is a false alarm: every hasher has a concrete [InheritsTests] subclass
// (Sha1Tests, FNV1a16/32/64Tests, Md5Tests), so these tests do run. TUnit0300 likewise:
// those subclasses close every generic combination at compile time.
#pragma warning disable TUnit0059, TUnit0300
public abstract class HasherTestBase<THash, THasher>
    where THash : unmanaged, IEquatable<THash>
    where THasher : IHasher<THash, THasher>
{
    [Test]
    // InstanceMethodDataSource instead of MethodDataSource: an attribute type argument
    // cannot use type parameters (CS0416); https://github.com/thomhurst/TUnit/issues/3604
    [InstanceMethodDataSource(nameof(GetTestData))]
    public async Task Test_Hasher(string input, THash expected)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var hashFromBytes = THasher.Hash(bytes);
        await Assert.That(hashFromBytes).IsEqualTo(expected);
    }

    public abstract IEnumerable<(string input, THash expected)> GetTestData();
}
#pragma warning restore TUnit0059, TUnit0300
